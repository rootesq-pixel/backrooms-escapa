// v7.1.0: bolas de plástico instanciadas en GPU (miles por piscina). Luz difusa = irradiancia horneada de la superficie de la
// piscina (por piscina) con oscurecimiento por profundidad; la linterna se suma con Standard. Los "empujadores" (jugadores,
// monstruo) apartan las bolas en el vértice: hueco alrededor de las piernas y un pequeño cerco levantado.
Shader "Backrooms/BallInstanced"
{
    Properties
    {
        _Color ("Color", Color) = (1,0,0,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.72
        _Irr ("Baked irradiance", Color) = (1,1,1,1)
        _TopY ("Ball surface Y (world)", Float) = 0
        _Depth ("Darkening depth", Float) = 0.22
        _Fade ("Dust/fade", Range(0,1)) = 0.22
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard noambient nolightmap nodynlightmap nodirlightmap vertex:vert
        #pragma multi_compile_instancing
        #pragma instancing_options assumeuniformscaling
        #pragma target 3.0
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
        UNITY_INSTANCING_BUFFER_END(Props)
        half _Glossiness, _Depth, _Fade; half4 _Irr; float _TopY;
        float4 _BallPushers[6];
        struct Input { half shade; half spec; fixed3 col; };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            UNITY_SETUP_INSTANCE_ID(v);
            o.col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color).rgb;
            float3 c = float3(unity_ObjectToWorld[0].w, unity_ObjectToWorld[1].w, unity_ObjectToWorld[2].w);
            float3 off = 0;
            for (int i = 0; i < 6; i++)
            {
                float4 p = _BallPushers[i];
                if (p.w == 0 || abs(c.y - p.y) > 1.2) continue;
                float2 d = c.xz - p.xz; float r = length(d) + 1e-4; float R = abs(p.w);
                float k = saturate(1 - r / R);
                if (p.w < 0)
                {   // algo se arrastra por debajo: bulto que levanta las bolas
                    off.y += k * k * (3 - 2 * k) * 0.24;
                    off.xz += (d / r) * k * (1 - k) * 0.12;
                    continue;
                }
                float ring = saturate(1 - abs(r - R * 1.1) / (R * 0.5));
                off.xz += (d / r) * pow(k, 1.3) * R * 0.85;
                off.y += ring * 0.06 - k * 0.17;
            }
            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz + off;
            v.vertex.xyz = mul(unity_WorldToObject, float4(wp, 1)).xyz;
            float depth = saturate((_TopY - c.y - off.y) / _Depth);
            float3 n = UnityObjectToWorldNormal(v.normal);
            o.shade = lerp(1, 0.18, depth) * lerp(0.45, 1.0, saturate(n.y * 0.6 + 0.4));
            o.spec = 1 - depth;
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed3 col = lerp(IN.col, dot(IN.col, fixed3(0.3, 0.5, 0.2)).xxx * 0.9 + 0.08, _Fade);
            o.Albedo = col;
            o.Smoothness = _Glossiness * IN.spec;
            o.Metallic = 0;
            o.Occlusion = IN.shade;
            o.Emission = col * _Irr.rgb * IN.shade;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
