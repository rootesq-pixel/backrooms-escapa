// v7.0 Campo infinito: hierba instanciada (DrawMeshInstancedIndirect). Matas escaneadas con alfa (Poly Haven, CC0).
// Viento (ruido + ráfagas + aleteo), flexión al pasar jugadores/monstruos, color variado y puntas secas, AO en la base,
// translucidez a contraluz, luz ambiente del cielo nublado (SH) + sol difuso, niebla. Recorte alfa con alpha-to-coverage (MSAA).
Shader "Backrooms/FieldGrass"
{
    Properties
    {
        _MainTex ("Matas (RGBA)", 2D) = "white" {}
        _Cutoff ("Recorte alfa", Range(0,1)) = 0.42
        _MaxH ("Altura de la malla", Float) = 0.3
        _DryCol ("Color seco", Color) = (0.72, 0.62, 0.38, 1)
        _Tint ("Tinte", Color) = (0.92, 1.0, 0.86, 1)
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" "DisableBatching"="True" }
        Cull Off
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            AlphaToMask On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct Inst { float4 pos; float4 prm; };   // pos: xyz local del nivel (=mundo), w escala | prm: giro, tono, seco, altura
            StructuredBuffer<Inst> _Inst;
            sampler2D _MainTex, _WindNoise;
            float _Cutoff, _MaxH, _WTime;
            float4 _DryCol, _Tint, _WindDir;
            float4 _GrassPush[8];

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 light : TEXCOORD1;
                float4 tint : TEXCOORD2;     // rgb tinte, a = seco en esta altura
                UNITY_FOG_COORDS(3)
            };

            v2f vert(appdata v, uint iid : SV_InstanceID)
            {
                Inst I = _Inst[iid];
                float s = I.pos.w, c = cos(I.prm.x), sn = sin(I.prm.x);
                float3 lp = v.vertex.xyz; lp.y *= I.prm.w;
                float h01 = saturate(v.vertex.y / _MaxH);
                float3 p = float3(lp.x * c - lp.z * sn, lp.y, lp.x * sn + lp.z * c) * s;
                float3 n = float3(v.normal.x * c - v.normal.z * sn, v.normal.y, v.normal.x * sn + v.normal.z * c);
                float3 root = I.pos.xyz;
                float hh = max(p.y, 0.0);
                float tall = max(_MaxH * s * I.prm.w, 0.05);
                float k = hh * hh / tall;

                // viento: ruido que se desplaza + ráfaga que recorre el campo + aleteo rápido
                float2 wuv = root.xz * 0.012 - _WindDir.xy * _WTime * 0.035;
                float nz = tex2Dlod(_WindNoise, float4(wuv, 0, 0)).r;
                float wave = 0.5 + 0.5 * sin(dot(root.xz, _WindDir.xy) * 0.09 - _WTime * 1.6);
                float bend = (nz - 0.3) * _WindDir.z * 1.4 + _WindDir.w * wave * 1.3 + 0.12;
                float flutter = sin(_WTime * (5.0 + I.prm.y * 3.0) + root.x * 3.1 + root.z * 2.3 + hh * 6.0) * (0.05 + 0.08 * _WindDir.w);
                p.xz += _WindDir.xy * (bend * k) + float2(flutter, flutter * 0.6) * k;

                // jugadores y monstruos aplastan/apartan la hierba
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float4 pu = _GrassPush[i];
                    if (pu.w <= 0.0) continue;
                    float2 dv = (root.xz + p.xz) - pu.xz;
                    float dl = length(dv);
                    float f = saturate(1.0 - dl / pu.w) * saturate(1.6 - abs(root.y - pu.y));
                    p.xz += dv / max(dl, 0.01) * f * hh * 1.1;
                    p.y *= 1.0 - f * 0.6;
                }
                // la flexión acorta (no estira) la mata
                float bl = length(p.xz - float2(lp.x * c - lp.z * sn, lp.x * sn + lp.z * c) * s);
                p.y -= min(bl * bl / max(hh + 0.3, 0.3) * 0.5, hh * 0.5);

                float3 wp = root + p;
                v2f o;
                o.pos = UnityWorldToClipPos(wp);
                o.uv = v.uv;

                // luz: cielo (SH) con normal casi vertical + sol difuso + translucidez a contraluz, AO en la base
                float3 N = normalize(lerp(float3(0, 1, 0), n, 0.3));
                float3 V = normalize(_WorldSpaceCameraPos - wp);
                float3 Ls = _WorldSpaceLightPos0.xyz;
                float3 amb = ShadeSH9(float4(N, 1));
                float dif = saturate(dot(N, Ls) * 0.5 + 0.5);
                float trans = pow(saturate(dot(-V, Ls)), 4.0) * 0.55 * h01;
                float ao = lerp(0.32, 1.0, pow(h01, 0.65));
                o.light = (amb * 1.05 + _LightColor0.rgb * (dif * 0.75 + trans)) * ao;

                // variación: tono por mata, manchas secas y puntas secas
                float3 hue = lerp(float3(0.88, 1.0, 0.82), float3(1.06, 1.0, 0.86), I.prm.y);
                o.tint = float4(hue * _Tint.rgb, saturate(I.prm.z * (0.35 + 0.65 * pow(h01, 1.3)) + pow(h01, 4.0) * 0.25));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                clip(t.a - _Cutoff * 0.75);
                float3 alb = t.rgb * i.tint.rgb;
                float lum = dot(alb, float3(0.3, 0.59, 0.11));
                alb = lerp(alb, _DryCol.rgb * (lum * 1.7 + 0.08), i.tint.a);
                fixed4 col = fixed4(alb * i.light, 1);
                // alpha-to-coverage: borde nítido pero suave con MSAA
                col.a = saturate((t.a - _Cutoff) / max(fwidth(t.a), 1e-4) + 0.5);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
