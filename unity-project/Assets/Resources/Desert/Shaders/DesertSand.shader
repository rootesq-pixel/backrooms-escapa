// v7.7.0 Desierto Cálido: arena de las dunas (Standard). Todo es PERIÓDICO con el tamaño del desierto (_L): al dar la vuelta
// (te teletransportas L metros) la arena no cambia ni salta.
//  · grano: textura escaneada CC0 (Poly Haven sand_03 / dense_sand) a 2 m + variación de color a 32 m y 128 m;
//  · ondulaciones de viento (ripples) analíticas, asimétricas (cara suave al viento, cara corta a sotavento), con bifurcaciones
//    (fase deformada), que se apagan en las caras de avalancha, a lo lejos (sin parpadeo: se filtran con fwidth) y a manchas;
//  · destellos de granos al sol; arenas movedizas (manchas más oscuras, lisas y húmedas con ondas lentas);
//  · clima: nieve que cubre la arena (arriba de las dunas primero), arena mojada por la lluvia y el «Clima Hermoso» (colores).
Shader "Backrooms/DesertSand"
{
    Properties
    {
        _GrainA ("Grano (albedo)", 2D) = "gray" {}
        _GrainN ("Grano (normal)", 2D) = "bump" {}
        _MacroA ("Variación (albedo)", 2D) = "gray" {}
        _SandCol ("Color de la arena", Color) = (0.80, 0.62, 0.42, 1)
        _SandCol2 ("Color de las hondonadas", Color) = (0.70, 0.50, 0.33, 1)
        _CrestCol ("Color de las crestas", Color) = (0.86, 0.70, 0.50, 1)
        _L ("Periodo del desierto (m)", Float) = 512
        _RippleK ("Ondas (kx, kz, kx2, kz2) rad/m", Vector) = (30, 6, 24, -9)
        _RippleAmp ("Pendiente de las ondas", Float) = 0.32
        _GrainScale ("Grano (m)", Float) = 2
        _Glint ("Destellos", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.5
        sampler2D _GrainA, _GrainN, _MacroA;
        float4 _SandCol, _SandCol2, _CrestCol, _RippleK;
        float _L, _RippleAmp, _GrainScale, _Glint;
        // globales (DesertSky / DesertWorld)
        float4 _DesertOrigin;          // posición de mundo de la raíz del nivel
        float4 _DesertWeather;         // x nieve 0-1, y mojado 0-1, z «Clima Hermoso» 0-1, w luz del sol 0-1 (destellos)
        float4 _DesertQuick[12];       // arenas movedizas: x, z, radio, 0
        float _DesertQuickN, _DesertTime;

        struct Input { float3 worldPos; float3 worldNormal; INTERNAL_DATA float lpx; float lpz; };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz - _DesertOrigin.xyz;
            o.lpx = wp.x; o.lpz = wp.z;
        }
        float hash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 lp = float2(IN.lpx, IN.lpz);
            float3 wn = WorldNormalVector(IN, float3(0, 0, 1));
            float dist = distance(IN.worldPos, _WorldSpaceCameraPos);
            // --- color: grano + variación periódica (32 m y 128 m)
            float2 uvG = lp / _GrainScale;
            float3 g = tex2D(_GrainA, uvG).rgb;
            float3 g2 = tex2D(_GrainA, uvG * 0.23 + 0.37).rgb;
            float m1 = dot(tex2D(_MacroA, lp / 32.0).rgb, 0.333), m2 = dot(tex2D(_MacroA, lp / 128.0 + 0.5).rgb, 0.333);
            float gl = dot(lerp(g, g2, 0.4), float3(0.3, 0.59, 0.11));
            float3 base = lerp(_SandCol2.rgb, _SandCol.rgb, saturate(m2 * 2.2 - 0.6));
            base = lerp(base, _CrestCol.rgb, saturate((wn.y - 0.9) * 3.0) * saturate(m1 * 1.8 - 0.5) * 0.6);
            float3 alb = base * (0.72 + 0.62 * gl) * (0.84 + 0.32 * m1) * (0.92 + 0.16 * m2) * 0.78;   // v7.7.0 a: albedo real de la arena (~0,4)
            // v7.7.0 g: menos plana a mediodía: caras de avalancha algo más oscuras y cálidas (grano más grueso), crestas más claras
            float steep = saturate((0.97 - wn.y) * 6.0);
            alb *= lerp(float3(1.04, 1.03, 1.02), float3(0.86, 0.80, 0.74), steep);
            // --- normal: grano (se aplana a lo lejos) + ondulaciones de viento
            float3 n = UnpackNormal(tex2D(_GrainN, uvG));
            n.xy *= 0.55 * saturate(1.0 - dist / 30.0);
            float warp = (m1 - 0.5) * 9.0 + (m2 - 0.5) * 14.0;
            float ph1 = dot(lp, _RippleK.xy) + warp;
            float ph2 = dot(lp, _RippleK.zw) + warp * 0.8 + 1.7;
            float aa = saturate(1.6 - fwidth(ph1) * 0.9);            // sin parpadeo: fuera cuando cada píxel cubre varias ondas
            float slopeOK = saturate((wn.y - 0.86) * 9.0);           // en las caras de avalancha no hay ondas
            float patch = saturate(m2 * 2.6 - 0.45) * 0.75 + 0.25;
            float amp = _RippleAmp * aa * slopeOK * patch * (1.0 - _DesertWeather.x * 0.9);
            // fase asimétrica: sin(φ + 0.65 sin φ) -> derivada (1 + 0.65 cos φ) cos(...)
            float d1 = cos(ph1 + 0.65 * sin(ph1)) * (1.0 + 0.65 * cos(ph1));
            float d2 = cos(ph2 + 0.65 * sin(ph2)) * (1.0 + 0.65 * cos(ph2));
            float mix2 = saturate(m1 * 2.0 - 0.6) * 0.55;
            float2 grad = (_RippleK.xy * d1 / length(_RippleK.xy)) * (1 - mix2) + (_RippleK.zw * d2 / length(_RippleK.zw)) * mix2;
            n.xy -= grad * amp;
            // oclusión en los valles de las ondas (sombra fina)
            float valley = 0.5 + 0.5 * sin(ph1 + 0.65 * sin(ph1));
            alb *= lerp(1.0, 0.8 + 0.2 * valley, amp / max(_RippleAmp, 1e-3));
            // a lo lejos, donde las ondas ya no se resuelven, queda su huella como vetas de tono (sin parpadeo: baja frecuencia)
            float far = (1.0 - aa) * slopeOK * patch * (1.0 - _DesertWeather.x * 0.9);
            float streak = tex2D(_MacroA, float2(dot(lp, normalize(_RippleK.xy)) / 3.0, dot(lp, normalize(_RippleK.xy).yx * float2(-1, 1)) / 40.0) + 0.21).r;
            alb *= 1.0 - far * (0.10 * streak);
            float smooth = 0.12 + 0.05 * gl;
            // --- arenas movedizas: más oscuras, lisas y húmedas, con ondas lentas concéntricas
            float qk = 0;
            for (int i = 0; i < 12; i++)
            {
                if (i >= (int)_DesertQuickN) break;
                float2 d = lp - _DesertQuick[i].xy; d -= _L * round(d / _L);
                float r = length(d), R = _DesertQuick[i].z;
                float k = 1.0 - smoothstep(R * 0.75, R * 1.08, r + (hash12(floor(lp * 3.0)) - 0.5) * 0.25);
                if (k > qk)
                {
                    qk = k;
                    float w = sin(r * 5.0 - _DesertTime * 1.3) * 0.5 + 0.5;
                    n.xy = lerp(n.xy, normalize(d + 1e-4) * (w - 0.5) * 0.12, k);
                }
            }
            alb = lerp(alb, alb * float3(0.62, 0.58, 0.55), qk);
            smooth = lerp(smooth, 0.48, qk);
            // --- clima
            float wet = _DesertWeather.y * (0.7 + 0.3 * m1);
            alb *= lerp(1.0, 0.58, wet); smooth = lerp(smooth, 0.55, wet);
            float3 beauty = lerp(float3(1.05, 0.62, 0.78), float3(0.55, 0.78, 1.05), saturate(m2 * 2.0 - 0.5));
            beauty = lerp(beauty, float3(1.1, 0.9, 0.55), saturate(m1 * 2.0 - 0.6));
            alb = lerp(alb, alb * beauty * 1.08, _DesertWeather.z);
            float snowMask = saturate(_DesertWeather.x * 1.4 - (1.0 - wn.y) * 3.0 - (0.5 - m1) * 0.6);
            alb = lerp(alb, float3(0.86, 0.88, 0.92) * (0.92 + 0.1 * gl), snowMask);
            smooth = lerp(smooth, 0.35, snowMask);
            n.xy *= 1.0 - snowMask * 0.7;
            // --- destellos de granos al sol (solo con sol directo; filtrados a lo lejos)
            float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos);
            float3 Ld = normalize(_WorldSpaceLightPos0.xyz);
            float h = hash12(floor(lp * 90.0) + floor(_DesertTime * 0.0));
            float spark = step(0.9965, h) * pow(saturate(dot(normalize(V + Ld), wn)), 24.0) * saturate(1.0 - dist / 18.0) * (1.0 - snowMask) * (1.0 - qk);
            o.Emission = spark * _Glint * _DesertWeather.w * 1.6 * float3(1.0, 0.95, 0.85);
            o.Albedo = alb;
            o.Normal = normalize(n);
            o.Smoothness = smooth;
            o.Metallic = 0;
            o.Occlusion = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
