// v7.11.0: forro interior de las mangas del traje CC5 (caras vueltas del revés). Tela oscura mate, sin recibir sombras
// (las sombras propias dentro de la manga daban dientes de sierra negros en primera persona).
Shader "Backrooms/SuitLining"
{
    Properties
    {
        _Color ("Color", Color) = (0.3,0.3,0.3,1)
        _MainTex ("Albedo", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 150
        CGPROGRAM
        #pragma surface surf Lambert noshadow nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.0
        sampler2D _MainTex; fixed4 _Color;
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb; o.Alpha = 1;
        }
        ENDCG
    }
    FallBack Off
}
