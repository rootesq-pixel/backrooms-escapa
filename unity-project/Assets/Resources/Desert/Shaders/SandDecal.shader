// v7.8.0: huellas y rodadas sobre la arena (decal: mezcla con la arena de debajo, iluminado, con niebla, sin sombras)
// v7.9.0: relieve por mapa de alturas (hueco + reborde de arena apartada, sombreado suave con la luz) y erosión del viento:
// color.r = hondura de la marca, color.g = erosión (0 recién hecha → 1 borrada: bordes cada vez más blandos y planos).
// v7.9.2 (usuario: «rayas amarillas»): antes la marca pintaba su propio color de arena (fijo, más saturado que la duna y
// sin su luz PBR) y se veía como una banda naranja/amarilla. Ahora es MULTIPLICATIVA sobre la arena real de debajo
// (Blend DstColor SrcColor = ×2): conserva el color exacto de la duna y solo la oscurece/aclara un poco con el relieve
// (surco algo más oscuro, reborde un poco más claro), así la rodada es sutil y del mismo tono en cualquier sitio y hora.
Shader "Backrooms/SandDecal"
{
    Properties
    {
        _MainTex ("Huella (RGB sombreado, A forma)", 2D) = "white" {}
        _HeightTex ("Alturas (R: 0,5 = arena plana)", 2D) = "gray" {}
        _Color ("(sin uso: la marca toma el color de la arena)", Color) = (1, 1, 1, 1)
        _Bump ("Relieve", Float) = 7
        _Strength ("Intensidad del sombreado", Float) = 0.4
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest+60" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        LOD 200
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Offset -2, -2
            ZWrite Off
            Blend DstColor SrcColor
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; sampler2D _HeightTex; float4 _HeightTex_TexelSize; float _Bump, _Strength;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; float3 n : TEXCOORD1; float3 t : TEXCOORD2; float3 b : TEXCOORD3; UNITY_FOG_COORDS(4) };
            v2f vert (appdata_full v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.texcoord, _MainTex); o.col = v.color;
                o.n = UnityObjectToWorldNormal(v.normal); o.t = UnityObjectToWorldDir(v.tangent.xyz); o.b = cross(o.n, o.t) * v.tangent.w;
                UNITY_TRANSFER_FOG(o, o.pos); return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float er = saturate(i.col.g), bias = er * 2.5;
                fixed4 t = tex2Dbias(_MainTex, float4(i.uv, 0, bias));
                float2 d = _HeightTex_TexelSize.xy * (1.5 + er * 3.0);
                float hx = tex2Dbias(_HeightTex, float4(i.uv + float2(d.x, 0), 0, bias)).r - tex2Dbias(_HeightTex, float4(i.uv - float2(d.x, 0), 0, bias)).r;
                float hy = tex2Dbias(_HeightTex, float4(i.uv + float2(0, d.y), 0, bias)).r - tex2Dbias(_HeightTex, float4(i.uv - float2(0, d.y), 0, bias)).r;
                float k = _Bump * i.col.r * (1.0 - er * 0.75);
                float3 N = normalize(i.n), T = normalize(i.t), B = normalize(i.b);
                float3 Np = normalize(N - (T * hx + B * hy) * k);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float sun = saturate(L.y * 4.0 + 0.2);                                   // de noche casi solo el tono del surco
                float l0 = max(dot(N, L), 0.25), l1 = max(dot(Np, L), 0.0);
                float relief = lerp(1.0, clamp(l1 / l0, 0.55, 1.35), sun);
                float tone = lerp(1.0, dot(t.rgb, float3(0.333, 0.333, 0.334)), (1.0 - er * 0.6) * 0.6);   // surco un poco más oscuro, sin tinte
                float m = lerp(1.0, relief * tone, saturate(t.a * i.col.a) * _Strength * 1.8);
                m = clamp(m, 0.6, 1.25);
                fixed4 c = fixed4(m, m, m, 1) * 0.5;                                     // ×2 en la mezcla: 0,5 = sin cambio
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0.5, 0.5, 0.5, 0.5));
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
