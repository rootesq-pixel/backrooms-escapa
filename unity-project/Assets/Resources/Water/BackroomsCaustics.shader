// Cáusticas animadas bajo el agua (Projector de built-in, aditivo). Proyecta solo por debajo de la superficie (_WaterY),
// con muestreo triplanar en coordenadas de mundo (fondo y paredes) y atenuación con la profundidad y la niebla.
Shader "Hidden/BackroomsCaustics"
{
    Properties
    {
        _CausticTex ("Caustics", 2D) = "black" {}
        _CausticTex2 ("Caustics next", 2D) = "black" {}
        _Blend ("Frame blend", Range(0,1)) = 0
        _CausticCol ("Colour", Color) = (0.55, 0.85, 0.9, 1)
        _Scale ("World scale", Float) = 0.45
        _WaterY ("Water Y", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" }
        Pass
        {
            ZWrite Off
            Blend One One
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _CausticTex, _CausticTex2;
            float _Blend, _Scale, _WaterY;
            float4 _PoolRoomRect, _PoolWaterRect; float _PoolCausticsUp, _PoolWaterY;   // v6.9: reflejos en paredes/techo de la sala
            fixed4 _CausticCol;
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 wn : TEXCOORD1; UNITY_FOG_COORDS(2) };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            float Samp(float2 uv) { return lerp(tex2D(_CausticTex, uv).r, tex2D(_CausticTex2, uv).r, _Blend); }
            fixed4 frag(v2f i) : SV_Target
            {
                float under = _WaterY - i.wpos.y;
                if (under <= 0.0)
                {
                    // v6.9: cáusticas reflejadas por el agua sobre paredes y techo cerca de la piscina (sala fija)
                    if (_PoolCausticsUp < 0.5) return 0;
                    float3 p = i.wpos;
                    if (p.x < _PoolRoomRect.x || p.x > _PoolRoomRect.z || p.z < _PoolRoomRect.y || p.z > _PoolRoomRect.w) return 0;
                    float hgt = p.y - _PoolWaterY;
                    if (hgt < 0.02) return 0;
                    float2 dq = max(max(_PoolWaterRect.xy - p.xz, p.xz - _PoolWaterRect.zw), 0);
                    float dPool = length(dq);
                    float3 un = normalize(i.wn);
                    float3 an = abs(un);
                    float3 wu = pow(an, 4.0); wu /= (wu.x + wu.y + wu.z);
                    // patrón más grande y lento (reflejo), distorsionado con la altura
                    float sc = _Scale * 0.55;
                    float cu = Samp(p.xz * sc + hgt * 0.07) * wu.y + Samp(p.zy * sc) * wu.x + Samp(p.xy * sc) * wu.z;
                    float face = saturate(-un.y) * 1.0 + (1 - an.y) * 0.75 + saturate(un.y) * 0.25;   // techo > paredes > suelo
                    float fu2 = exp(-dPool * 0.55) * saturate(hgt * 3.0) * face;
                    fixed4 cu4 = fixed4(_CausticCol.rgb * cu * fu2 * 0.20, 0);
                    UNITY_APPLY_FOG_COLOR(i.fogCoord, cu4, fixed4(0, 0, 0, 0));
                    return cu4;
                }
                float3 n = abs(normalize(i.wn));
                float3 w = pow(n, 4.0); w /= (w.x + w.y + w.z);
                // suelo: separación cromática (R/G/B desplazados, más con la profundidad), paredes: monocromo
                float2 fu = i.wpos.xz * _Scale; float so = 0.004 + under * 0.003;
                float3 cf = float3(Samp(fu + float2(so, so)), Samp(fu), Samp(fu - float2(so, so)));
                float cw = Samp(i.wpos.zy * _Scale) * w.x + Samp(i.wpos.xy * _Scale) * w.z;
                float3 c = cf * w.y + cw;
                float fade = saturate(under * 6.0) * exp(-under * 0.35);
                fixed4 col = fixed4(_CausticCol.rgb * c * fade, 0);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0));
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
