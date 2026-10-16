// v7.0.1: corteza / madera con mapeo triplanar en espacio de objeto (los modelos de Blender no llevan UV de corteza).
// PBR CC0 (Poly Haven): albedo, normal (GL), máscara G = AO, A = suavidad. Base enterrada oscurecida y con tierra.
Shader "Backrooms/FieldBark"
{
    Properties
    {
        _Color ("Tinte", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _Mask ("Mascara (G AO, A suavidad)", 2D) = "white" {}
        _Scale ("Escala (rep/m)", Float) = 0.8
        _Stretch ("Estirado vertical", Float) = 0.5
        _Gloss ("Suavidad", Range(0,1)) = 0.5
        _DirtCol ("Tierra", Color) = (0.17,0.14,0.10,1)
        _DirtH ("Altura tierra (m)", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.5
        sampler2D _MainTex, _BumpMap, _Mask;
        float4 _Color, _DirtCol; float _Scale, _Stretch, _Gloss, _DirtH;
        struct Input { float3 op; float3 on; };
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input, o); o.op = v.vertex.xyz; o.on = v.normal; }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 w = pow(abs(normalize(IN.on)), 4); w /= (w.x + w.y + w.z);
            float3 p = IN.op * _Scale;
            float2 ux = float2(p.z, p.y * _Stretch), uy = p.xz, uz = float2(p.x, p.y * _Stretch);
            float4 a = tex2D(_MainTex, ux) * w.x + tex2D(_MainTex, uy) * w.y + tex2D(_MainTex, uz) * w.z;
            float4 m = tex2D(_Mask, ux) * w.x + tex2D(_Mask, uy) * w.y + tex2D(_Mask, uz) * w.z;
            float3 nx = UnpackNormal(tex2D(_BumpMap, ux)), ny = UnpackNormal(tex2D(_BumpMap, uy)), nz = UnpackNormal(tex2D(_BumpMap, uz));
            float3 n = normalize(float3(0, 0, 1) + float3(nx.xy * w.x + ny.xy * w.y + nz.xy * w.z, 0) * 1.2);
            float dirt = 1 - smoothstep(0.0, _DirtH, IN.op.y);
            o.Albedo = lerp(a.rgb * _Color.rgb, _DirtCol.rgb, dirt * 0.8);
            o.Normal = n;
            o.Occlusion = m.g * lerp(1, 0.45, dirt);
            o.Smoothness = m.a * _Gloss * (1 - dirt * 0.7);
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Standard"
}
