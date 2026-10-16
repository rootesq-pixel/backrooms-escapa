// v7.0 Campo infinito: cielo nublado (HDRI equirectangular CC0, Poly Haven «kloofendal_overcast_puresky») con bruma hacia el horizonte
// del mismo color que la niebla, para que el campo se pierda en la distancia sin un corte.
Shader "Backrooms/FieldSky"
{
    Properties
    {
        _Tex ("HDRI", 2D) = "grey" {}
        _Exposure ("Exposición", Float) = 1.0
        _HazeCol ("Bruma", Color) = (0.66, 0.69, 0.69, 1)
        _HazeH ("Altura de la bruma", Float) = 0.22
        _Rot ("Giro", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Tex; float4 _Tex_HDR;
            float _Exposure, _HazeH, _Rot; float4 _HazeCol;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float u = atan2(d.x, d.z) / (2.0 * UNITY_PI) + 0.5 + _Rot;
                float vv = asin(clamp(d.y, -1, 1)) / UNITY_PI + 0.5;
                float3 c = DecodeHDR(tex2Dlod(_Tex, float4(u, max(vv, 0.5), 0, 0)), _Tex_HDR) * _Exposure;
                float h = saturate(d.y / _HazeH);
                c = lerp(_HazeCol.rgb, c, smoothstep(0.0, 1.0, h));
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
