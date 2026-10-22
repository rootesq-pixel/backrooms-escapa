// v6.8: sala de piscina fija con iluminación horneada en Blender Cycles (lightmap HDR en UV2, irradiancia difusa).
// Difuso = albedo × lightmap (emisión); especular/reflejo de la sonda y luces en tiempo real (linterna) se suman con Standard.
// v7.1.3: suelo de baldosa brillante como en Cycles: reflejo planar (el de la piscina) en las caras hacia arriba por encima
// del agua, con fresnel de barniz y borrosidad por rugosidad; banda oscura en la pared del vaso entre el agua y la playa.
Shader "Backrooms/PoolBaked"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal scale", Float) = 1
        _MaskMap ("Mask (G AO, A smooth)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.85
        _LightMap ("Baked irradiance (UV2)", 2D) = "white" {}
        _LMScale ("Bake exposure", Float) = 1
        _AOStrength ("Tile AO", Range(0,1)) = 0.6
        _PlanarK ("Planar reflection on up faces", Range(0,2)) = 0
        _WaterBand ("Dark band above water (pool walls)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard noambient nolightmap nodynlightmap nodirlightmap vertex:vert
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap, _MaskMap, _LightMap;
        sampler2D _PoolPlanarTex; float _PoolPlanarOn, _PoolWaterY;
        float4 _PoolDeckP;   // x: fuerza del reflejo, y: F0 del barniz, z: distorsión por la normal, w: borrosidad (mip por rugosidad)
        fixed4 _Color; half _Glossiness, _LMScale, _AOStrength, _BumpScale, _PlanarK, _WaterBand;
        struct Input { float2 uv_MainTex; float2 lmuv; float4 screenPos; float3 worldPos; float wny; };
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input, o); o.lmuv = v.texcoord1.xy; o.wny = UnityObjectToWorldNormal(v.normal).y; }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            fixed4 m = tex2D(_MaskMap, IN.uv_MainTex);
            half3 lm = tex2D(_LightMap, IN.lmuv).rgb * _LMScale;
            half ao = lerp(1, m.g, _AOStrength);
            float3 tn = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            o.Albedo = c.rgb;
            o.Normal = tn;
            o.Smoothness = _Glossiness * m.a;
            o.Metallic = 0;
            o.Occlusion = ao;
            o.Emission = c.rgb * lm * ao;
            if (_WaterBand > 0 && abs(IN.wny) < 0.3)
            {   // pared del vaso justo encima del agua: en sombra (en Cycles sale casi negra)
                float h = IN.worldPos.y - _PoolWaterY;
                float band = saturate(1.0 - h / 0.07) * saturate(h / 0.01 + 1.0);
                o.Emission *= 1.0 - _WaterBand * band; o.Albedo *= 1.0 - _WaterBand * band;
            }
            // bajo el agua la baldosa apenas refleja (interfaz agua-barniz: F0 ~0,004): sin el reflejo nítido de las ventanas del cubemap
            if (_WaterBand > 0 && IN.worldPos.y < _PoolWaterY - 0.01) o.Occlusion *= 0.15;
            if (_PlanarK > 0 && _PoolPlanarOn > 0.5 && IN.wny > 0.9 && IN.worldPos.y > _PoolWaterY + 0.02)
            {
                float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos);
                float ndv = saturate(V.y);
                float F = _PoolDeckP.y + (1.0 - _PoolDeckP.y) * pow(1.0 - ndv, 5.0);
                float2 suv = IN.screenPos.xy / max(IN.screenPos.w, 1e-4) + tn.xy * _PoolDeckP.z;
                float rough = 1.0 - o.Smoothness;
                float3 pr = tex2Dlod(_PoolPlanarTex, float4(suv, 0, rough * _PoolDeckP.w)).rgb;
                o.Emission += pr * F * _PlanarK * _PoolDeckP.x * m.a * ao;
                o.Smoothness *= 0.6;   // la sonda (cubemap) aporta menos: el reflejo nítido viene del planar
            }
        }
        ENDCG
    }
    Fallback "Diffuse"
}
