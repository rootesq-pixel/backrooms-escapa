// v7.7.0 Desierto Cálido: cielo procedural con ciclo de día y noche (lo controla DesertSky.cs):
//  degradado cénit/horizonte según la altura del sol, halo de dispersión alrededor del sol, disco solar con oscurecimiento
//  del borde, nubes (fbm proyectado en un plano, se mueven con el viento; cobertura según el clima, borde plateado junto
//  al sol), noche muy oscura con estrellas que titilan, Vía Láctea tenue y luna con fase y halo, auroras («Clima Hermoso»)
//  y bruma en el horizonte del MISMO color que la niebla (las dunas lejanas se funden con el cielo sin corte).
Shader "Backrooms/DesertSky"
{
    Properties { _Exposure ("Exposición", Float) = 1 }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            float _Exposure;
            float4 _SkySunDir, _SkyMoonDir, _SkySunCol, _SkyZenith, _SkyHorizon, _SkyFog, _SkyCloudLit, _SkyCloudDark;
            float4 _SkyParams;    // x cobertura de nubes 0-1, y estrellas 0-1, z fase lunar 0-1 (0.5 llena), w auroras 0-1
            float4 _SkyParams2;   // x tiempo (s), y densidad de nubes (oscuridad), z bruma extra (tormenta) 0-1, w brillo de la luna
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }
            float hash13(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
            float hash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), f.x), lerp(hash12(i + float2(0, 1)), hash12(i + float2(1, 1)), f.x), f.y); }
            float fbm(float2 p) { float s = 0, a = 0.5; for (int i = 0; i < 5; i++) { s += a * vn(p); p = p * 2.03 + 17.1; a *= 0.5; } return s; }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sd = normalize(_SkySunDir.xyz), md = normalize(_SkyMoonDir.xyz);
                float t = _SkyParams2.x;
                float h = saturate(d.y);
                // --- degradado y dispersión
                float3 c = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, pow(h, 0.42));
                float cs = dot(d, sd);
                float sunUp = saturate(sd.y * 4 + 0.3);
                c += _SkySunCol.rgb * (pow(saturate(cs), 6.0) * 0.32 + pow(saturate(cs), 64.0) * 0.6) * sunUp * (1 - 0.6 * h);
                // --- estrellas, Vía Láctea y luna (de noche)
                float stars = _SkyParams.y * saturate(d.y * 6.0);
                if (stars > 0.001)
                {
                    float3 q = d * 420.0; float3 cell = floor(q);
                    float hs = hash13(cell);
                    float3 fp = frac(q) - 0.5;
                    float st = step(0.9955, hs) * smoothstep(0.45, 0.0, length(fp)) * (1.1 + 7.0 * pow(hash13(cell + 7.1), 6.0));
                    // segunda capa: muchas estrellas débiles (cielo del desierto sin contaminación)
                    float3 q2 = d * 900.0; float3 c2 = floor(q2); float h2 = hash13(c2 + 11.7);
                    st += step(0.985, h2) * smoothstep(0.5, 0.0, length(frac(q2) - 0.5)) * 0.35;
                    st *= 0.7 + 0.3 * sin(t * (2.0 + hs * 5.0) + hs * 40.0);   // titileo
                    float3 scol = lerp(float3(0.75, 0.85, 1.1), float3(1.1, 0.95, 0.8), hash13(cell + 3.3));
                    // Vía Láctea: banda tenue inclinada con nubes de polvo
                    float3 gal = normalize(float3(0.35, 0.25, 0.9));
                    float band = exp(-pow(dot(d, gal) * 3.4, 2.0));
                    float2 gp = float2(atan2(d.x, d.z) * 5.0, d.y * 9.0);
                    float dustL = smoothstep(0.35, 0.75, fbm(gp * 2.3 + 3.0));              // nubes oscuras de polvo
                    float mw = band * (0.45 + 0.55 * fbm(gp)) * (1.0 - 0.45 * dustL * band) * 0.10 + exp(-pow(dot(d, gal) * 1.8, 2.0)) * 0.02;
                    c += (st * scol + mw * float3(0.86, 0.85, 0.88)) * stars;
                    // luna con fase (terminador según la fase) y halo
                    float mc = dot(d, md);
                    float disk = smoothstep(0.99955, 0.99975, mc);
                    if (disk > 0)
                    {
                        float3 right = normalize(cross(md, float3(0, 1, 0))), up = cross(right, md);
                        float2 mp = float2(dot(d - md * mc, right), dot(d - md * mc, up)) / 0.0215;
                        float ph = _SkyParams.z * 2 - 1;           // -1 nueva, 0 llena... (aprox.)
                        float lit = saturate((mp.x * cos(_SkyParams.z * 6.2832) + sqrt(saturate(1 - dot(mp, mp))) * (0.5 - abs(_SkyParams.z - 0.5)) * 2) * 3 + 0.5);
                        float maria = 0.75 + 0.25 * fbm(mp * 3.0 + 5.0);
                        c = lerp(c, float3(0.95, 0.93, 0.88) * maria * lit * _SkyParams2.w * 2.2, disk);
                    }
                    c += float3(0.55, 0.62, 0.8) * pow(saturate(mc), 400.0) * 0.18 * _SkyParams2.w * stars;
                }
                // --- disco solar
                float sunDisk = smoothstep(0.99965, 0.99985, cs);
                float limb = 0.6 + 0.4 * saturate((cs - 0.99965) / 0.0002);
                c += _SkySunCol.rgb * sunDisk * limb * 22.0 * saturate(sd.y * 20 + 0.6);
                // --- auroras (rarísimas, «Clima Hermoso»): cortinas en capas (8 planos a distinta altura) con rayos finos
                if (_SkyParams.w > 0.001 && d.y > 0.02)
                {
                    float3 acc = 0;
                    float jit = hash12(d.xz * float2(937.1, 611.3));   // capas con altura tramada: sin filas de puntos en el horizonte
                    [loop] for (int k = 0; k < 24; k++)
                    {
                        float fk = (k + jit) / 24.0;
                        float hk = 1.0 + fk * 2.4;                                      // cortina alta (capas de 1.0 a 3.4)
                        float2 p = d.xz * (hk / d.y) * 0.5 + float2(t * 0.004, t * 0.002);
                        float w = vn(p * 0.55) * 0.65 + vn(p * 1.3 + 7.0) * 0.35;
                        float rib = smoothstep(0.045, 0.0, abs(w - 0.5));               // cinta fina que serpentea
                        // rayos: ruido en la posición de la cortina (igual en todas las capas -> estrías verticales continuas)
                        float ray = 0.35 + 0.65 * vn(p * 7.0 + float2(t * 0.02, 0.0)) * (0.6 + 0.4 * vn(p * 2.1 + 3.0));
                        float3 col = lerp(float3(0.15, 1.0, 0.45), float3(0.55, 0.25, 1.0), smoothstep(0.45, 1.0, fk));
                        acc += col * rib * ray * pow(1.0 - fk, 1.5) * (0.085 + 0.085 * smoothstep(0.0, 0.1, fk));
                    }
                    c += acc * smoothstep(0.04, 0.32, d.y) * _SkyParams.w;
                }
                // --- nubes (plano a ~2 km): cobertura y luz según el clima y la hora
                if (d.y > 0.0)
                {
                    float2 uv = d.xz / (d.y + 0.12) * 1.6 + float2(t * 0.004, t * 0.0015);
                    float n = fbm(uv) * 0.75 + fbm(uv * 3.1 + 4.0) * 0.25;
                    float cov = _SkyParams.x;
                    float dens = smoothstep(1.0 - cov * 0.85 - 0.12, 1.0 - cov * 0.85 + 0.22, n);
                    dens *= smoothstep(0.0, 0.18, d.y);            // se pierden en la bruma del horizonte
                    float thick = saturate((n - (1.0 - cov * 0.85)) * 3.0);
                    float3 ccol = lerp(_SkyCloudLit.rgb, _SkyCloudDark.rgb, saturate(thick * (0.6 + _SkyParams2.y)));
                    ccol += _SkySunCol.rgb * pow(saturate(cs), 12.0) * (1 - thick) * 0.6 * sunUp;   // borde plateado
                    c = lerp(c, ccol, dens * 0.96);
                }
                // --- bruma: el horizonte (y lo que hay por debajo) acaba en el color exacto de la niebla
                float hz = smoothstep(-0.02, 0.16 + _SkyParams2.z * 0.6, d.y);
                c = lerp(_SkyFog.rgb, c, hz);
                c = lerp(c, _SkyFog.rgb, _SkyParams2.z * 0.85);
                return fixed4(c * _Exposure, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
