// v6.8: sala de piscina fija con iluminación horneada en Blender Cycles (lightmap HDR en UV2, irradiancia difusa).
// Difuso = albedo × lightmap (emisión); especular/reflejo de la sonda y luces en tiempo real (linterna) se suman con Standard.
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
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard noambient nolightmap nodynlightmap nodirlightmap vertex:vert
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap, _MaskMap, _LightMap;
        fixed4 _Color; half _Glossiness, _LMScale, _AOStrength, _BumpScale;
        struct Input { float2 uv_MainTex; float2 lmuv; };
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input, o); o.lmuv = v.texcoord1.xy; }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            fixed4 m = tex2D(_MaskMap, IN.uv_MainTex);
            half3 lm = tex2D(_LightMap, IN.lmuv).rgb * _LMScale;
            half ao = lerp(1, m.g, _AOStrength);
            o.Albedo = c.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            o.Smoothness = _Glossiness * m.a;
            o.Metallic = 0;
            o.Occlusion = ao;
            o.Emission = c.rgb * lm * ao;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
