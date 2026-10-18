// v7.1.0: superficies del parque de bolas horneadas en Blender Cycles (como Backrooms/PoolBaked) con tinte y recorte alfa (redes).
Shader "Backrooms/PlayBaked"
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
        _AOStrength ("Tex AO", Range(0,1)) = 0.6
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0
        _MetalScale ("Metal from mask R", Float) = 1
        _EmitAdd ("Self emission", Color) = (0,0,0,0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Standard noambient nolightmap nodynlightmap nodirlightmap vertex:vert
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap, _MaskMap, _LightMap;
        fixed4 _Color, _EmitAdd; half _Glossiness, _LMScale, _AOStrength, _BumpScale, _Cutoff, _MetalScale;
        struct Input { float2 uv_MainTex; float2 lmuv; };
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input, o); o.lmuv = v.texcoord1.xy; }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            clip(c.a - _Cutoff);
            fixed4 m = tex2D(_MaskMap, IN.uv_MainTex);
            half3 lm = tex2D(_LightMap, IN.lmuv).rgb * _LMScale;
            half ao = lerp(1, m.g, _AOStrength);
            o.Albedo = c.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            o.Smoothness = _Glossiness * m.a;
            o.Metallic = m.r * _MetalScale;
            o.Occlusion = ao;
            o.Emission = c.rgb * lm * ao + _EmitAdd.rgb;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
