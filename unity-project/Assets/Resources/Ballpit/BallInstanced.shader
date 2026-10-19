// v7.1.0: bolas de plástico instanciadas en GPU (miles por piscina). Luz difusa = irradiancia horneada de la superficie de la
// piscina (por piscina) con oscurecimiento por profundidad; la linterna se suma con Standard. Los "empujadores" (jugadores,
// monstruo) apartan las bolas en el vértice: hueco alrededor de las piernas y un pequeño cerco levantado.
// v7.1.1: todas las bolas se mecen (oleaje + vaivén por instancia) y al vadear salen ondas que recorren la piscina.
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
        _Dyn ("Pit motion (0 = flying/rolling balls)", Float) = 1
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
        half _Glossiness, _Depth, _Fade; half4 _Irr; float _TopY; float _Dyn;
        float4 _BallPushers[6];
        float4 _BallRipples[8];      // v7.1.1: ondas al vadear (xyz origen en el mundo, w = instante de inicio; 0 = libre)
        float _BallAmbient;          // v7.1.1: amplitud del vaivén de todas las bolas (Alto 1, Bajo 0.8)
        struct Input { half shade; half spec; fixed3 col; };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            UNITY_SETUP_INSTANCE_ID(v);
            o.col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color).rgb;
            float3 c = float3(unity_ObjectToWorld[0].w, unity_ObjectToWorld[1].w, unity_ObjectToWorld[2].w);
            float3 off = 0;
            float t = _Time.y;
            float h = frac(sin(dot(c.xz, float2(12.9898, 78.233))) * 43758.5453);
            float sink = saturate((_TopY - c.y) / 0.2);                    // las capas de abajo se mueven menos
            float amb = _BallAmbient * _Dyn * (1 - 0.55 * sink);
            // v7.1.1: ninguna bola quieta: oleaje lento que recorre la piscina + vaivén propio de cada bola (fase por instancia)
            float sw = sin(dot(c.xz, float2(1.9, 1.2)) - t * 0.85) * 0.6 + sin(dot(c.xz, float2(-1.1, 2.6)) - t * 1.27 + 1.3) * 0.4;
            off.y += amb * (0.0075 * sw + 0.0055 * sin(t * (1.5 + h * 1.6) + h * 6.2832));
            off.xz += amb * 0.0045 * float2(sin(t * (1.1 + h) + h * 12.1), cos(t * (1.35 + h * 0.8) + h * 9.3));
            for (int j = 0; j < 8; j++)
            {
                float4 q = _BallRipples[j];
                if (q.w <= 0 || abs(c.y - q.y) > 1.2) continue;
                float age = t - q.w; if (age < 0 || age > 2.6) continue;
                float2 dq = c.xz - q.xz; float rq = length(dq) + 1e-4;
                float x = rq - (0.35 + age * 1.15);                         // frente de onda que se aleja a ~1,15 m/s
                float env = exp(-x * x * 22) * exp(-age * 1.35) * saturate(1.5 - rq * 0.32) * _Dyn * (1 - 0.5 * sink);
                off.y += 0.032 * env * cos(x * 16);
                off.xz += (dq / rq) * 0.014 * env;
            }
            for (int i = 0; i < 6; i++)
            {
                float4 p = _BallPushers[i];
                if (p.w == 0 || abs(c.y - p.y) > 1.2) continue;
                float2 d = c.xz - p.xz; float r = length(d) + 1e-4; float R = abs(p.w);
                float k = saturate(1 - r / R);
                if (p.w < 0)
                {   // algo se arrastra por debajo: bulto que levanta las bolas
                    off.y += k * k * (3 - 2 * k) * 0.24 * _Dyn;
                    off.xz += (d / r) * k * (1 - k) * 0.12 * _Dyn;
                    continue;
                }
                float ring = saturate(1 - abs(r - R * 1.1) / (R * 0.5));
                float k2 = saturate(1 - r / (R * 2.2));                   // v7.1.1: zona exterior más ancha: las bolas se apartan en cadena
                off.xz += (d / r) * (pow(k, 1.3) * R * 0.85 + k2 * k2 * 0.05) * _Dyn;
                off.y += (ring * 0.07 - k * 0.17 + k2 * k2 * 0.02) * _Dyn;
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
