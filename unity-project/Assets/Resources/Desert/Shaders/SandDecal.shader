// v7.8.0: huellas y rodadas sobre la arena (decal: mezcla con la arena de debajo, iluminado, con niebla, sin sombras)
// v7.9.0: relieve por mapa de alturas (hueco + reborde de arena apartada, sombreado suave con la luz) y erosión del viento:
// color.r = hondura de la marca, color.g = erosión (0 recién hecha → 1 borrada: bordes cada vez más blandos y planos).
Shader "Backrooms/SandDecal"
{
    Properties
    {
        _MainTex ("Huella (RGB sombreado, A forma)", 2D) = "white" {}
        _HeightTex ("Alturas (R: 0,5 = arena plana)", 2D) = "gray" {}
        _Color ("Color de la arena", Color) = (0.78, 0.62, 0.45, 1)
        _Bump ("Relieve", Float) = 7
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
        sampler2D _MainTex; sampler2D _HeightTex; float4 _HeightTex_TexelSize; fixed4 _Color; float _Bump;
        struct Input { float2 uv_MainTex; float4 color : COLOR; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            float er = saturate(IN.color.g);
            float bias = er * 2.5;
            float2 uv = IN.uv_MainTex;
            fixed4 t = tex2Dbias(_MainTex, float4(uv, 0, bias));
            float2 d = _HeightTex_TexelSize.xy * (1.5 + er * 3.0);
            float hx = tex2Dbias(_HeightTex, float4(uv + float2(d.x, 0), 0, bias)).r - tex2Dbias(_HeightTex, float4(uv - float2(d.x, 0), 0, bias)).r;
            float hy = tex2Dbias(_HeightTex, float4(uv + float2(0, d.y), 0, bias)).r - tex2Dbias(_HeightTex, float4(uv - float2(0, d.y), 0, bias)).r;
            float k = _Bump * IN.color.r * (1.0 - er * 0.75);
            o.Normal = normalize(float3(-hx * k, -hy * k, 1.0));
            o.Albedo = _Color.rgb * lerp(t.rgb, fixed3(1, 1, 1), er * 0.55);
            o.Alpha = t.a * IN.color.a;
        }
        ENDCG
    }
    Fallback Off
}
