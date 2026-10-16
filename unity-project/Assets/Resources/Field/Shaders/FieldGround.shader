// v7.0 Campo infinito: suelo (textura PBR escaneada CC0, Poly Haven «sparse_grass») con variación a gran escala y,
// a lo lejos, fundido hacia el color de la hierba (la hierba solo se dibuja hasta cierta distancia).
Shader "Backrooms/FieldGround"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _Tile ("Metros por repetición", Float) = 2.5
        _FarCol ("Color de hierba lejana", Color) = (0.36, 0.40, 0.22, 1)
        _DryCol ("Color seco", Color) = (0.55, 0.50, 0.32, 1)
        _FarStart ("Inicio del fundido", Float) = 14
        _FarEnd ("Fin del fundido", Float) = 45
        _GroundTint ("Tinte del suelo", Color) = (0.8, 0.85, 0.7, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert noforwardadd
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap;
        float _Tile, _FarStart, _FarEnd;
        float4 _FarCol, _DryCol, _GroundTint;
        struct Input { float3 worldPos; };
        void surf(Input IN, inout SurfaceOutput o)
        {
            float2 uv = IN.worldPos.xz / _Tile;
            float3 c = tex2D(_MainTex, uv).rgb;
            float3 c2 = tex2D(_MainTex, uv * 0.137 + 0.31).rgb;
            float macro = dot(tex2D(_MainTex, IN.worldPos.xz / 61.0).rgb, float3(0.33, 0.33, 0.33));
            c = lerp(c, c2, 0.35) * (0.8 + 0.45 * macro);
            // el escaneado es tierra rojiza con hierba rala: se lleva hacia tierra/paja verde oliva (como bajo una pradera)
            float lum0 = dot(c, float3(0.3, 0.59, 0.11));
            c = lerp(c, lum0 * float3(0.78, 0.86, 0.5) * 1.1, 0.65) * _GroundTint.rgb;
            float d = distance(IN.worldPos, _WorldSpaceCameraPos);
            float far = saturate((d - _FarStart) / (_FarEnd - _FarStart));
            float dryK = saturate(macro * 1.6 - 0.4);
            float3 farc = lerp(_FarCol.rgb, _DryCol.rgb, dryK * 0.6) * (0.85 + 0.3 * c2.g);
            o.Albedo = lerp(c, farc, saturate(far * 0.9 + 0.15));
            o.Normal = UnpackNormal(tex2D(_BumpMap, uv));
            o.Normal = normalize(lerp(o.Normal, float3(0, 0, 1), far));
        }
        ENDCG
    }
    Fallback "Diffuse"
}
