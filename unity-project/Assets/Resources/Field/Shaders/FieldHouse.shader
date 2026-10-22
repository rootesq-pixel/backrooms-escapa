// v7.2.0 Campo infinito: casas con luz del cielo nublado horneada en Blender Cycles (GPU) en un atlas EXR (UV2).
// Difuso = albedo × lightmap (sin luz ambiente ni sol en tiempo real: ya están en el horneado, interiores oscuros incluidos);
// la linterna y otras luces puntuales se suman con Standard. Texturas PBR CC0 de Poly Haven (albedo con AO, normal GL, rugosidad).
// Sin lightmap (_UseLM = 0) se comporta como antes: ambiente + sol.
Shader "Backrooms/FieldHouse"
{
    Properties
    {
        _Color ("Tinte", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _RoughTex ("Rugosidad", 2D) = "gray" {}
        _Gloss ("Suavidad máx", Range(0,1)) = 0.35
        _LightMap ("Luz horneada (UV2)", 2D) = "white" {}
        _LMScale ("Exposición del horneado", Float) = 1
        _UseLM ("Usar horneado", Float) = 1
        _DirtCol ("Tierra en la base", Color) = (0.20,0.18,0.13,1)
        _DirtH ("Altura de la tierra (m, local)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf House vertex:vert
        #pragma target 3.0
        #include "UnityPBSLighting.cginc"
        sampler2D _MainTex, _BumpMap, _RoughTex, _LightMap;
        half4 _Color, _DirtCol; half _Gloss, _LMScale, _UseLM, _DirtH;
        struct Input { float2 uv_MainTex; float2 lmuv; float ly; };
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input, o); o.lmuv = v.texcoord1.xy; o.ly = v.vertex.y; }
        inline half4 LightingHouse(SurfaceOutputStandard s, float3 viewDir, UnityGI gi)
        {
            if (_UseLM > 0.5 && _WorldSpaceLightPos0.w < 0.5) gi.light.color = 0;   // sol: ya horneado
            return LightingStandard(s, viewDir, gi);
        }
        inline void LightingHouse_GI(SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandard_GI(s, data, gi);
            if (_UseLM > 0.5) gi.indirect.diffuse = 0;   // el ambiente lo da el lightmap (emisión)
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            half r = tex2D(_RoughTex, IN.uv_MainTex).r;
            if (_DirtH > 0) c.rgb = lerp(c.rgb, _DirtCol.rgb, (1 - smoothstep(-0.3, _DirtH, IN.ly)) * 0.7);
            o.Albedo = c.rgb;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            o.Smoothness = (1 - r) * _Gloss;
            o.Metallic = 0;
            if (_UseLM > 0.5)
            {
                half3 lm = tex2D(_LightMap, IN.lmuv).rgb * _LMScale;
                o.Emission = c.rgb * lm;
                o.Occlusion = saturate(dot(lm, half3(0.3, 0.59, 0.11)) * 2.0);   // reflejo del cielo solo donde llega su luz
            }
        }
        ENDCG
    }
    FallBack "Diffuse"
}
