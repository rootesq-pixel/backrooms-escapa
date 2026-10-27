// Nivel 7460: pasillo de puertas horneado en Blender Cycles (Tools/c7460/build_corridor.py).
// Difuso = albedo × irradiancia horneada (UV2, EXR HDR) como emisión; especular de la sonda de reflexión (caja que sigue al
// jugador por módulos) y luces en tiempo real (linterna) con el Standard. Suelo de resina: reflejo planar (CorridorPlanar) con
// fresnel de barniz y borrosidad por rugosidad (mips), distorsionado un poco por la normal del suelo.
Shader "Backrooms/CorridorBaked"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal scale", Float) = 1
        _MaskMap ("Mask (G AO, A smooth)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _LightMap ("Baked irradiance (UV2)", 2D) = "white" {}
        _LMScale ("Bake exposure", Float) = 1
        _LMMin ("Min irradiance (huecos de jamba)", Float) = 0
        _PlanarK ("Planar reflection (up faces)", Range(0,2)) = 0
        _ProbeK ("Probe specular", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard noambient nolightmap nodynlightmap nodirlightmap vertex:vert
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap, _MaskMap, _LightMap;
        sampler2D _C7460PlanarTex; float _C7460PlanarOn; float4 _C7460PlanarP;   // x fuerza, y F0, z distorsión, w mip por rugosidad
        fixed4 _Color; half _Glossiness, _LMScale, _LMMin, _BumpScale, _PlanarK, _ProbeK;
        struct Input { float2 uv_MainTex; float2 lmuv; float4 screenPos; float3 worldPos; float wny; };
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input, o); o.lmuv = v.texcoord1.xy; o.wny = UnityObjectToWorldNormal(v.normal).y; }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            fixed4 m = tex2D(_MaskMap, IN.uv_MainTex);
            half3 lm = max(tex2D(_LightMap, IN.lmuv).rgb, _LMMin) * _LMScale;   // _LMMin: jambas que en el horneado quedaron tras la hoja cerrada
            float3 tn = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            o.Albedo = c.rgb;
            o.Normal = tn;
            o.Smoothness = _Glossiness * m.a;
            o.Metallic = 0;
            o.Occlusion = _ProbeK * m.g;
            o.Emission = c.rgb * lm * m.g;
            if (_PlanarK > 0 && _C7460PlanarOn > 0.5 && IN.wny > 0.9)
            {
                float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos);
                float ndv = saturate(V.y);
                float F = _C7460PlanarP.y + (1.0 - _C7460PlanarP.y) * pow(1.0 - ndv, 5.0);
                float2 suv = IN.screenPos.xy / max(IN.screenPos.w, 1e-4) + tn.xy * _C7460PlanarP.z;
                float rough = 1.0 - o.Smoothness;
                float3 pr = tex2Dlod(_C7460PlanarTex, float4(suv, 0, rough * _C7460PlanarP.w)).rgb;
                o.Emission = o.Emission * (1.0 - F) + pr * F * _PlanarK * _C7460PlanarP.x;   // energía: lo que refleja no se difunde
                o.Occlusion *= 0.12;   // la sonda casi no suma: el reflejo nítido viene del planar
            }
        }
        ENDCG
    }
    Fallback "Diffuse"
}
