// v6.9: post-procesado propio (built-in, sin paquetes): bloom suave (cadena de mips), SSAO barato con la profundidad,
// tonemapping ACES (Narkowicz), gradación (exposición, contraste, saturación, tinte). Va ANTES del efecto VHS.
// El proyecto está en espacio de color Gamma: se pasa a lineal para el tonemap y se vuelve a gamma.
Shader "Hidden/BackroomsPost"
{
    Properties { _MainTex ("Src", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _BloomTex, _CameraDepthTexture;
    float4 _MainTex_TexelSize;
    float _Threshold, _Knee, _BloomInt, _Exposure, _Contrast, _Saturation, _AOInt, _AORadius, _AOOn, _ToneOn;
    float4 _Tint, _Lift;
    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
    // v7.3.0b: un solo píxel NaN/Inf de la escena (p. ej. un especular degenerado) se extendía por la cadena de mips del bloom
    // y salía como rectángulos negros apilados: se sanea en el prefiltro y en la composición
    half3 Safe(half3 c) { return (dot(c, half3(1, 1, 1)) >= 0 && dot(c, half3(1, 1, 1)) < 65000) ? c : half3(0, 0, 0); }
    half3 Box4(float2 uv, float2 t) {
        half3 c = Safe(tex2D(_MainTex, uv + t * float2(-1,-1)).rgb) + Safe(tex2D(_MainTex, uv + t * float2(1,-1)).rgb)
                + Safe(tex2D(_MainTex, uv + t * float2(-1,1)).rgb) + Safe(tex2D(_MainTex, uv + t * float2(1,1)).rgb);
        return c * 0.25; }
    half3 ToLin(half3 c) { return c * c * (c * 0.305306 + 0.682171) + c * 0.012522; }
    half3 ToGam(half3 c) { return max(1.055 * pow(max(c, 1e-5), 0.416666667) - 0.055, 0); }
    half3 Aces(half3 x) { return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14)); }
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        // 0: prefiltro (umbral suave) + downsample
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target {
                half3 c = ToLin(Box4(i.uv, _MainTex_TexelSize.xy));
                half br = max(c.r, max(c.g, c.b));
                half soft = clamp(br - _Threshold + _Knee, 0, 2 * _Knee); soft = soft * soft / (4 * _Knee + 1e-4);
                half k = max(soft, br - _Threshold) / max(br, 1e-4);
                return half4(c * k, 1); }
            ENDCG }
        // 1: downsample
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target { return half4(Box4(i.uv, _MainTex_TexelSize.xy), 1); }
            ENDCG }
        // 2: upsample (tienda 4 muestras) aditivo
        Pass { Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target { return half4(Box4(i.uv, _MainTex_TexelSize.xy * 0.5), 1); }
            ENDCG }
        // 3: composición: AO + bloom + ACES + gradación
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            float Lin(float2 uv) { return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv)); }
            half4 frag(v2f i) : SV_Target {
                half4 src = tex2D(_MainTex, i.uv); src.rgb = Safe(src.rgb);
                half3 c = ToLin(src.rgb);
                if (_AOOn > 0.5) {
                    float d = Lin(i.uv);
                    if (d < 60) {
                        float r = _AORadius / max(d, 0.3);
                        float occ = 0;
                        const float2 K[8] = { float2(1,0), float2(0.707,0.707), float2(0,1), float2(-0.707,0.707), float2(-1,0), float2(-0.707,-0.707), float2(0,-1), float2(0.707,-0.707) };
                        float rot = frac(sin(dot(i.uv * _ScreenParams.xy, float2(12.9898, 78.233))) * 43758.5453) * 6.283;
                        float cs = cos(rot), sn = sin(rot);
                        [unroll] for (int k = 0; k < 8; k++) {
                            float2 o = float2(K[k].x * cs - K[k].y * sn, K[k].x * sn + K[k].y * cs) * r * (0.45 + 0.55 * (k & 1));
                            o.y *= _ScreenParams.x / _ScreenParams.y;
                            float ds = Lin(i.uv + o);
                            float dz = d - ds;
                            occ += saturate((dz - 0.03) / 0.25) * saturate(1 - (dz - 0.25) / 0.6);
                        }
                        c *= 1 - saturate(occ / 8 * _AOInt);
                    }
                }
                c += Safe(tex2D(_BloomTex, i.uv).rgb) * _BloomInt;
                if (_ToneOn > 0.5) {
                    c *= _Exposure;
                    c = Aces(c);
                    half l = dot(c, half3(0.2126, 0.7152, 0.0722));
                    c = lerp(l.xxx, c, _Saturation);
                    c = saturate((c - 0.18) * _Contrast + 0.18);
                    c = c * _Tint.rgb + _Lift.rgb * (1 - c);
                }
                return half4(ToGam(c), src.a); }
            ENDCG }
    }
    Fallback Off
}
