// Efecto de cámara VHS / found-footage (Built-in pipeline, pantalla completa vía Graphics.Blit):
// scanlines, aberración cromática/sangrado de color horizontal, ruido de cinta, jitter horizontal, bandas de tracking,
// baja resolución (blur horizontal + cuantización vertical), viñeta, tono cálido/desaturado y ligera distorsión de lente.
Shader "Hidden/BackroomsVHS"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0,1)) = 0.7
        _Glitch ("Glitch (monster proximity)", Range(0,1)) = 0
        _Scream ("Jump-scare glitch", Range(0,1)) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Intensity;
            float _Glitch;
            float _Scream;
            float _VhsTime;

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float I = _Intensity;
                float G = saturate(_Glitch);
                float t = _VhsTime;
                float S = saturate(_Scream);
                float4 orig = tex2D(_MainTex, uv);

                // --- Susto: salto vertical de la imagen (pérdida de tracking) y bloques desplazados ---
                float jt = floor(t * 12.0);
                uv.y = frac(uv.y + step(0.82, hash11(jt * 2.3)) * (hash11(jt * 5.1) - 0.5) * 0.5 * S);
                float2 blkId = floor(uv * float2(10.0, 7.0));
                float bh = hash21(blkId + jt);
                uv.x += step(1.0 - 0.24 * S, bh) * (hash11(bh * 77.0) - 0.5) * 0.28 * S;

                // --- Distorsión de lente suave (fisheye ligero) ---
                float2 c = uv - 0.5;
                float r2 = dot(c, c);
                float2 duv = 0.5 + c * (1.0 + r2 * 0.12 * I);
                uv = lerp(uv, duv, 1.0);

                // --- Baja resolución vertical (líneas de la cinta) ---
                float lines = lerp(_ScreenParams.y, 420.0, I * 0.8);
                float ly = floor(uv.y * lines) / lines;

                // --- Jitter horizontal por línea + onda lenta ---
                float wob = (hash11(floor(uv.y * 240.0) + floor(t * 24.0) * 7.13) - 0.5) * 0.0016 * I;
                wob += sin(uv.y * 6.0 + t * 1.3) * 0.0007 * I;

                // --- Banda de tracking que sube de vez en cuando ---
                float cyc = frac(t * 0.11);
                float bandY = frac(1.0 - cyc * 1.6);
                float bandOn = step(cyc, 0.45) * I;
                float inBand = smoothstep(0.0, 0.01, uv.y - bandY) * (1.0 - smoothstep(0.045, 0.09, uv.y - bandY));
                inBand *= bandOn;
                // glitch por cercanía del monstruo: bandas aleatorias extra
                float gl = floor(t * 14.0);
                float gBandY = hash11(gl * 3.7);
                float gBandH = 0.02 + hash11(gl * 9.1) * 0.08;
                float inG = step(abs(uv.y - gBandY), gBandH) * step(0.55, hash11(gl * 1.9)) * G;
                float shift = (hash11(floor(uv.y * 90.0) + gl) - 0.5) * 0.06 * inBand
                            + (hash11(floor(uv.y * 60.0) + gl * 2.0) - 0.5) * 0.18 * inG;
                wob += shift;

                float2 suv = float2(uv.x + wob, ly + (uv.y - ly) * (1.0 - I * 0.8));

                // --- Aberración cromática y sangrado de color (blur horizontal) ---
                float ca = (0.0016 + 0.0035 * G + 0.003 * inBand) * (0.4 + I) + 0.022 * S * (0.6 + 0.4 * hash11(jt));
                float px = _MainTex_TexelSize.x * (1.0 + 2.5 * I);
                float3 col;
                col.r = tex2D(_MainTex, suv + float2(ca, 0)).r * 0.5 + tex2D(_MainTex, suv + float2(ca + px * 1.5, 0)).r * 0.25 + tex2D(_MainTex, suv + float2(ca - px * 1.5, 0)).r * 0.25;
                col.g = tex2D(_MainTex, suv).g * 0.5 + tex2D(_MainTex, suv + float2(px * 1.5, 0)).g * 0.25 + tex2D(_MainTex, suv - float2(px * 1.5, 0)).g * 0.25;
                col.b = tex2D(_MainTex, suv - float2(ca, 0)).b * 0.5 + tex2D(_MainTex, suv + float2(-ca + px * 1.5, 0)).b * 0.25 + tex2D(_MainTex, suv + float2(-ca - px * 1.5, 0)).b * 0.25;

                // --- Tono: ligeramente desaturado y cálido, negros levantados ---
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(float3(luma, luma, luma), col, 1.0 - 0.22 * I - 0.25 * G);
                col *= lerp(float3(1, 1, 1), float3(1.07, 1.0, 0.86), I);
                col = col * (1.0 - 0.06 * I) + 0.025 * I;
                col = (col - 0.5) * (1.0 + 0.08 * I) + 0.5;

                // --- Scanlines + barra de refresco lenta ---
                float scan = 0.5 + 0.5 * sin(uv.y * _ScreenParams.y * 3.14159);
                col *= 1.0 - (0.10 + 0.10 * G) * I * scan;
                float roll = sin(uv.y * 3.0 - t * 0.9);
                col *= 1.0 + 0.03 * I * smoothstep(0.7, 1.0, roll);

                // --- Ruido de cinta / estática ---
                float2 np = uv * float2(_ScreenParams.x * 0.5, _ScreenParams.y * 0.5) + float2(t * 91.7, t * 53.3);
                float n = hash21(floor(np)) - 0.5;
                float grain = (0.06 + 0.04 * inBand + 0.28 * G) * I;
                col += n * grain;
                // franja de ruido en el borde inferior (cabezales)
                float foot = smoothstep(0.035, 0.0, uv.y);
                col = lerp(col, float3(1, 1, 1) * (hash21(floor(np * 0.5) + t) * 0.8), foot * 0.55 * I);
                // estática con monstruos cerca
                float st = step(0.985 - 0.04 * G, hash21(floor(np * 0.25) + floor(t * 30.0))) * G;
                col = lerp(col, float3(1, 1, 1) * hash21(floor(np) + t), st * 0.6);

                // --- Susto: barra rodante, bloques de estática y ruido pesado ---
                float rb = frac(t * 1.35);
                float inRb = smoothstep(0.0, 0.02, uv.y - rb) * (1.0 - smoothstep(0.10, 0.17, uv.y - rb));
                col = lerp(col, 1.0 - col, inRb * S * 0.75);
                col *= 1.0 + inRb * S * 0.6;
                float blkN = step(1.0 - 0.14 * S, hash21(floor(uv * float2(16.0, 9.0)) + floor(t * 20.0)));
                col = lerp(col, float3(1, 1, 1) * hash21(floor(np) + t), blkN * 0.9);
                col += (hash21(floor(np * 2.0) + t * 1.7) - 0.5) * 0.55 * S;

                // --- Viñeta ---
                float vig = smoothstep(0.85, 0.25, length(c) * (1.0 + 0.2 * G));
                col *= lerp(1.0, vig, 0.55 * I + 0.2);

                // --- Bordes fuera del fisheye ---
                float2 inside = step(float2(0, 0), suv) * step(suv, float2(1, 1));
                col *= inside.x * inside.y;

                col = lerp(orig.rgb, col, saturate(I * 1.35));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
