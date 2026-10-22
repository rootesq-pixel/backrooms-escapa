// v7.3.0 Talasofobia: brillo aditivo que atraviesa la niebla (linterna del faro, bengalas, señuelo del pez).
// _Fog: densidad de niebla propia (mucho menor que la de la escena) para que se vea de lejos sin "recortarse".
Shader "Ocean/Glow"
{
    Properties
    {
        _Color ("Color", Color) = (1,0.9,0.7,1)
        _Intensity ("Intensity", Float) = 1
        _Fog ("Own fog density", Float) = 0.004
        _Soft ("Edge softness (sprites)", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color; float _Intensity, _Fog, _Soft;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float dist : TEXCOORD1; fixed4 color : COLOR; };
            v2f vert(appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.dist = distance(wp, _WorldSpaceCameraPos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 c = i.uv * 2 - 1;
                float r = saturate(1 - dot(c, c));
                float a = lerp(1, r * r, _Soft) * i.color.a;
                float f = exp(-i.dist * _Fog);
                return fixed4(_Color.rgb * i.color.rgb * (_Intensity * a * f), 1);
            }
            ENDCG
        }
    }
}
