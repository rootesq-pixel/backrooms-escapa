// v7.12.0 Nivel 188: material de la Ventana. Standard + un borde translúcido (fresnel) del color de la contraluz: con la luz
// por detrás, los pliegues del camisón (normal de tela) y el contorno se leen como tela al trasluz en vez de un recorte negro.
Shader "W188/FigureRim"
{
    Properties
    {
        _Color ("Color", Color) = (0.07,0.065,0.06,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal scale", Float) = 1.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.4
        _RimColor ("Rim", Color) = (0.55,0.6,0.7,1)
        _RimPower ("Rim power", Range(0.5,8)) = 2.6
        _RimStrength ("Rim strength", Range(0,3)) = 0.5
        _Trans ("Translucency", Range(0,1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap;
        fixed4 _Color, _RimColor;
        half _Glossiness, _RimPower, _RimStrength, _BumpScale, _Trans;
        struct Input { float2 uv_MainTex; float3 viewDir; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            half ndv = saturate(dot(normalize(IN.viewDir), o.Normal));
            half rim = pow(1.0 - ndv, _RimPower);
            // translucidez barata: la tela fina deja pasar algo de la contraluz donde se dobla (variación de la normal de tela)
            half thin = (1.0 - ndv) * _Trans * (0.6 + 0.4 * c.r / max(_Color.r, 0.001));
            o.Emission = _RimColor.rgb * (rim * _RimStrength + thin * 0.35);
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
