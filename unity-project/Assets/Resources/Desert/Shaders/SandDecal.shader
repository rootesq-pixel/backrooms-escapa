// v7.8.0: huellas y rodadas sobre la arena (decal: mezcla con la arena de debajo, iluminado, con niebla, sin sombras)
Shader "Backrooms/SandDecal"
{
    Properties
    {
        _MainTex ("Huella (RGB sombreado, A forma)", 2D) = "white" {}
        _Color ("Color de la arena", Color) = (0.78, 0.62, 0.45, 1)
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest+60" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        LOD 200
        Offset -2, -2
        ZWrite Off
        CGPROGRAM
        #pragma surface surf Lambert decal:blend nolightmap nodynlightmap nodirlightmap noshadowmask
        #pragma target 3.0
        sampler2D _MainTex; fixed4 _Color;
        struct Input { float2 uv_MainTex; float4 color : COLOR; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 t = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = _Color.rgb * t.rgb;
            o.Alpha = t.a * IN.color.a;
        }
        ENDCG
    }
    Fallback Off
}
