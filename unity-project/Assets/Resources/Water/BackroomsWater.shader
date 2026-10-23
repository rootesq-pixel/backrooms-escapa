// Agua realista para el nivel inundado / Poolrooms (Built-in pipeline).
// - Olas Gerstner de 3 octavas (desplazamiento de vértices) + dos normal maps desplazándose + ondas circulares (ripples) por pisadas.
// - Fresnel, reflejo del entorno (ReflectionProbe en tiempo real), brillos especulares de las luces del techo (4 más cercanas, por script),
//   tinte por profundidad (color de vértice: G = profundidad), espuma suave junto a paredes (color de vértice: R = cercanía a pared).
// - Se ve también desde debajo (cara trasera): tinte submarino. Niebla de Unity aplicada.
Shader "Hidden/BackroomsWater"
{
    Properties
    {
        _Color ("Shallow colour", Color) = (0.30, 0.55, 0.62, 0.45)
        _Deep ("Deep colour", Color) = (0.02, 0.10, 0.14, 0.95)
        _FoamCol ("Foam colour", Color) = (0.85, 0.92, 0.95, 1)
        _UnderCol ("Underwater colour", Color) = (0.05, 0.25, 0.30, 0.9)
        _NormalMap ("Normal map", 2D) = "bump" {}
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Smooth ("Reflectivity", Range(0,1)) = 0.9
        _WaveAmp ("Wave amplitude", Range(0,2)) = 1
        _Absorb ("Absorption", Range(0,4)) = 1
        _Refract ("Refraction strength", Range(0,0.2)) = 0.05
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        // v6.7: refracción del fondo (una sola copia de pantalla por frame, compartida)
        GrabPass { "_WaterGrab" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Stencil { Ref 64 ReadMask 64 Comp NotEqual }   // v7.3.0: no dibujar dentro del casco de la barca (Hidden/BoatWaterMask)

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _NormalMap, _NoiseTex, _WaterGrab;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            half _Absorb, _Refract;
            float4 _PoolRoomRect;
            float _PoolLookAll;   // v7.1.3: 1 en todo el nivel Poolrooms (toda el agua del nivel con el aspecto de la sala)
            float InLook(float3 p) { return (_PoolLookAll > 0.5 || (p.x > _PoolRoomRect.x && p.x < _PoolRoomRect.z && p.z > _PoolRoomRect.y && p.z < _PoolRoomRect.w)) ? 1.0 : 0.0; }
            sampler2D _PoolPlanarTex; float _PoolPlanarOn;   // reflexión planar de la sala (Medio/Alto)   // sala de piscina de Blender (xz min/max mundo): agua más clara
            float _WDepthOn;
            fixed4 _Color, _Deep, _FoamCol, _UnderCol;
            half _Smooth, _WaveAmp;
            float4 _WRip[16];       // x, z, t0, amplitud
            float4 _WLPos[4];       // xyz, rango
            float4 _WLCol[4];       // rgb * intensidad
            float _WTime;
            // v7.1.3: aspecto de la sala de piscina ajustado a los renders de Cycles (pool-render/pool_1..3)
            float4 _PoolWaterTint;     // rgb: tinte del fondo visto a través del agua
            float4 _PoolWaterAbs;      // rgb: absorción por metro (pared/fondo), w: claridad global
            float4 _PoolWaterScatter;  // rgb: color de dispersión (luz que vuelve del agua), w: cantidad
            float4 _PoolEdgeP;         // v7.1.3: línea de agua oscura junto a las paredes del vaso: x ancho (m), y oscuridad
            float4 _PoolWaterRectW;    // rectángulo del agua del vaso (mundo xz)
            float4 _PoolWaveK;         // x: k de la primera onda (rad/m), y: razón entre ondas, z: peso de los normal maps genéricos
            float4 _PoolWaveP;         // x: fuerza de las normales pequeñas, y: refracción física, z: distorsión del reflejo, w: brillo del reflejo

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float3 nrm : TEXCOORD1;
                fixed4 col : COLOR;
                UNITY_FOG_COORDS(2)
                float4 grabPos : TEXCOORD3;
                float4 scrPos : TEXCOORD4;
            };

            // 3 olas: dirección, longitud de onda, amplitud
            static const float2 WD[3] = { float2(0.80, 0.60), float2(-0.50, 0.866), float2(0.20, -0.98) };
            static const float WL[3] = { 5.3, 2.9, 1.7 };
            static const float WA[3] = { 0.012, 0.008, 0.0045 };

            float3 Waves(float2 p, float t, out float3 n)
            {
                float3 d = float3(0, 0, 0);
                float2 slope = float2(0, 0);
                for (int i = 0; i < 3; i++)
                {
                    float2 dir = normalize(WD[i]);
                    float k = 6.2831853 / WL[i];
                    float w = sqrt(9.8 * k) * 0.55;
                    float th = k * dot(dir, p) - w * t;
                    float a = WA[i] * _WaveAmp;
                    float c = cos(th), s = sin(th);
                    d.xz += 0.55 * a * dir * c;
                    d.y += a * s;
                    slope += a * k * dir * c;
                }
                n = normalize(float3(-slope.x, 1.0, -slope.y));
                return d;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float4 wp = mul(unity_ObjectToWorld, v.vertex);
                float3 n;
                float3 d = Waves(wp.xz, _WTime, n);
                wp.xyz += d;
                o.wpos = wp.xyz;
                o.nrm = n;
                o.col = v.color;
                o.pos = UnityWorldToClipPos(wp);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                o.scrPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.scrPos.z);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 N = normalize(i.nrm);

                // normal maps desplazándose
                float2 uv1 = i.wpos.xz * 0.33 + _WTime * float2(0.030, 0.021);
                float2 uv2 = i.wpos.xz * 0.87 + _WTime * float2(-0.022, 0.036);
                float3 n1 = UnpackNormal(tex2D(_NormalMap, uv1));
                float3 n2 = UnpackNormal(tex2D(_NormalMap, uv2));
                // las normales se suavizan con la distancia (menos distorsión/aliasing lejos)
                float nFade = saturate(1.2 - length(_WorldSpaceCameraPos - i.wpos) / 28.0);
                N = normalize(N + float3(n1.x + n2.x, 0, n1.y + n2.y) * 0.32 * nFade);
                // v6.9: movimiento suave constante en la sala de piscina (ondas lentas largas)
                if (InLook(i.wpos) > 0.5)
                {
                    float3 n3 = UnpackNormal(tex2D(_NormalMap, i.wpos.xz * 0.12 + _WTime * float2(0.011, -0.008)));
                    N = normalize(N + float3(n3.x, 0, n3.y) * 0.22);
                    // v7.1.3: oleaje como el de Cycles (pool-render/build.py: suma de senos k 5-30 rad/m, A = 0,0028/(k/6)^0,8):
                    // 8 senos analíticos (amplitud ×√5 para la misma pendiente rms que los 40 de Blender), dispersión de aguas profundas
                    if (_PoolWaterAbs.w > 0.0)
                    {
                        float2 sl = 0;
                        float nNear = saturate(1.35 - length(_WorldSpaceCameraPos - i.wpos) / 24.0);
                        int nw = _PoolWaveK.w >= 1.0 ? (int)_PoolWaveK.w : 8;   // Bajo: 4 ondas
                        for (int w = 0; w < nw; w++)
                        {
                            float k = _PoolWaveK.x * pow(_PoolWaveK.y, w);
                            float ang = w * 2.39996 + 0.4;
                            float2 dir = float2(cos(ang), sin(ang));
                            float A = 0.0028 * 2.236 / pow(k / 6.0, 0.8);
                            float ph = dot(dir, i.wpos.xz) * k - sqrt(9.81 * k) * 0.55 * _WTime + w * 1.7;
                            sl += dir * (A * k * cos(ph));
                        }
                        sl *= sqrt(8.0 / nw);
                        N = normalize(float3(0, 1, 0) * (1.0 - _PoolWaveK.z) + N * _PoolWaveK.z - float3(sl.x, 0, sl.y) * _PoolWaveP.x * nNear);
                    }
                }

                // ondas circulares (pisadas, chapoteos, objetos)
                float crest = 0;
                for (int r = 0; r < 16; r++)
                {
                    float4 rp = _WRip[r];
                    if (rp.w <= 0.0001) continue;
                    float age = _WTime - rp.z;
                    if (age < 0 || age > 3.2) continue;
                    float2 dv = i.wpos.xz - rp.xy;
                    float dist = length(dv) + 0.0001;
                    float d = dist - age * 1.5;
                    float env = rp.w * exp(-age * 1.3) * exp(-d * d * 7.0);
                    float slope = cos(d * 17.0) * 17.0 * env * 0.035;
                    N.xz += (dv / dist) * slope;
                    crest += saturate(env * (0.5 + 0.5 * cos(d * 17.0))) * 0.6;
                }
                N = normalize(N);

                float depth01 = saturate(i.col.g);
                float foamZone = saturate(i.col.r);

                // iluminación aproximada (ambiente + luces cercanas) para el color base
                float3 amb = UNITY_LIGHTMODEL_AMBIENT.rgb * 1.5 + 0.02;
                float3 diff = amb;
                float3 spec = 0;
                for (int k = 0; k < 4; k++)
                {
                    float rng = _WLPos[k].w;
                    if (rng <= 0.01) continue;
                    float3 Lv = _WLPos[k].xyz - i.wpos;
                    float dl = length(Lv);
                    float at = saturate(1.0 - dl / rng); at *= at;
                    float3 L = Lv / max(dl, 0.001);
                    diff += saturate(dot(N, L)) * at * _WLCol[k].rgb * 0.45;
                    float3 H = normalize(L + V);
                    spec += pow(saturate(dot(N, H)), 520.0) * at * _WLCol[k].rgb * 2.6;
                }

                fixed4 res;
                if (facing < 0)
                {
                    // vista desde debajo de la superficie: tinte submarino + reflexión total a ángulos rasantes
                    float ndv = saturate(dot(-N, V));
                    float tir = pow(1.0 - ndv, 3.0);
                    float3 ucol = _UnderCol.rgb * (0.35 + 0.65 * diff) + tir * _UnderCol.rgb * 0.8;
                    res = fixed4(ucol, saturate(_UnderCol.a + tir * 0.1));
                    UNITY_APPLY_FOG(i.fogCoord, res);
                    return res;
                }

                // grosor de agua hasta el fondo (textura de profundidad de la cámara; si no hay, color de vértice)
                float surfZ = i.scrPos.z;
                float thick = depth01 * 2.3;
                if (_WDepthOn > 0.5)
                {
                    float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.scrPos)));
                    thick = max(0.0, sceneZ - surfZ) * saturate(dot(V, float3(0, 1, 0)) + 0.25);
                }
                // refracción: el fondo desplazado por la normal de las ondas (menos en aguas muy someras)
                float inRoomR = InLook(i.wpos);
                float2 off = N.xz * _Refract * saturate(thick * 3.0 + 0.15);
                // v7.1.3: en la sala, desplazamiento físico: en el fondo ~ profundidad × pendiente × (1 - 1/1,33), en pantalla / distancia
                if (inRoomR > 0.5 && _PoolWaterAbs.w > 0.0) off = N.xz * _PoolWaveP.y * 0.25 * min(thick, 2.0) / max(surfZ, 0.35);
                float2 guv = i.grabPos.xy / i.grabPos.w + off;
                if (_WDepthOn > 0.5)
                {
                    // no refractar objetos por delante del agua
                    float zOff = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.scrPos.xy / i.scrPos.w + off));
                    if (zOff < surfZ) guv = i.grabPos.xy / i.grabPos.w;
                }
                float3 refr = tex2D(_WaterGrab, guv).rgb;
                // absorción azul-verdosa por profundidad (el rojo se pierde antes) + dispersión hacia el color profundo
                float inRoom = InLook(i.wpos);
                float rawThick = thick;
                thick *= lerp(1.0, 0.24, inRoom);   // v6.9: agua de la sala más clara (como pool_1..3 de Cycles)
                float3 trans = exp(-thick * float3(1.10, 0.36, 0.30) * _Absorb);
                float3 scatter = lerp(lerp(_Color.rgb, float3(0.74, 0.90, 0.92), inRoom), lerp(_Deep.rgb, float3(0.40, 0.66, 0.74), inRoom), saturate(thick / 2.0)) * diff;
                float3 baseCol = refr * trans * lerp(float3(1,1,1), _Color.rgb * 1.6, 0.35) + scatter * (1.0 - trans);
                if (inRoom > 0.5 && _PoolWaterAbs.w > 0.0)
                {   // v7.1.3: agua de la sala: azul acero (fondo azul visto a través, absorción del rojo, poca dispersión)
                    float3 tr2 = exp(-min(rawThick, 3.0) * _PoolWaterAbs.rgb);
                    baseCol = refr * tr2 * _PoolWaterTint.rgb + _PoolWaterScatter.rgb * _PoolWaterScatter.w * (1.0 - tr2) * diff;
                }
                float alpha = 1.0;

                float f = 0.02 + 0.98 * pow(1.0 - saturate(dot(N, V)), 5.0);
                float3 R = reflect(-V, N);
                half4 pr = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, R, 1.0);
                float3 env = DecodeHDR(pr, unity_SpecCube0_HDR);
                if (inRoom > 0.5)
                {
                    if (_PoolPlanarOn > 0.5)
                    {
                        float2 puv = i.scrPos.xy / i.scrPos.w + N.xz * (_PoolWaterAbs.w > 0.0 ? _PoolWaveP.z : 0.035);
                        env = lerp(env, tex2D(_PoolPlanarTex, puv).rgb * (_PoolWaterAbs.w > 0.0 ? _PoolWaveP.w : 1.0), 0.9);
                    }
                    f = max(f, 0.05);
                    if (_PoolWaterAbs.w <= 0.0) baseCol *= 1.12;   // agua de piscina iluminada por los tragaluces
                }
                float3 col = lerp(baseCol, env, saturate(f * (inRoom > 0.5 && _PoolWaterAbs.w > 0.0 ? 1.0 : _Smooth)));
                col += spec;
                // v6.9: línea de agua suave (se funde con el fondo en el borde) en la sala de piscina
                if (inRoom > 0.5 && _WDepthOn > 0.5) col = lerp(refr, col, saturate(rawThick / 0.09));
                if (inRoom > 0.5 && _PoolEdgeP.x > 0.0)
                {   // v7.1.3: como en Cycles, una línea oscura donde el agua toca la pared del vaso (rendija en sombra)
                    float2 e2 = min(i.wpos.xz - _PoolWaterRectW.xy, _PoolWaterRectW.zw - i.wpos.xz);
                    float ed = min(e2.x, e2.y);
                    col *= 1.0 - _PoolEdgeP.y * (1.0 - smoothstep(0.0, _PoolEdgeP.x, ed)) * step(-0.05, ed);
                }

                // espuma: junto a paredes + crestas de ondas circulares
                float nz = tex2D(_NoiseTex, i.wpos.xz * 0.7 + _WTime * float2(0.015, -0.01)).r;
                float nz2 = tex2D(_NoiseTex, i.wpos.xz * 1.9 - _WTime * 0.02).r;
                float foam = saturate(foamZone * (0.35 + 1.1 * nz * nz2 * 2.0) - 0.18) + saturate(crest) * 0.7;
                foam = saturate(foam);
                foam *= 1.0 - 0.85 * inRoom;
                float3 foamCol = _FoamCol.rgb * saturate(diff * 0.9 + 0.12);
                col = lerp(col, foamCol, foam);
                alpha = max(alpha, foam * 0.85);

                res = fixed4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, res);
                return res;
            }
            ENDCG
        }
    }
    Fallback Off
}
