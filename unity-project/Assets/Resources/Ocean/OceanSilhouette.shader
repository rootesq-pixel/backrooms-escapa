// v7.3.0 Talasofobia: silueta lejana (el faro y las casas grandes se intuyen en la niebla mucho antes de verse de verdad).
// Se dibuja encima de la geometría real solo a partir de _Near metros, con una niebla propia más suave.
Shader "Ocean/Silhouette"
{
    Properties
    {
        _Color ("Silhouette", Color) = (0.2,0.22,0.24,1)
        _FogCol ("Fog color", Color) = (0.47,0.52,0.54,1)
        _Fog ("Own fog density", Float) = 0.009
        _Near ("Fade in from", Float) = 70
        _Far ("Full at", Float) = 140
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color, _FogCol; float _Fog, _Near, _Far;
            struct v2f { float4 pos : SV_POSITION; float dist : TEXCOORD0; };
            v2f vert(float4 v : POSITION)
            {
                v2f o; o.pos = UnityObjectToClipPos(v);
                o.dist = distance(mul(unity_ObjectToWorld, v).xyz, _WorldSpaceCameraPos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float f = exp(-i.dist * _Fog);
                float a = smoothstep(_Near, _Far, i.dist) * f;
                return fixed4(lerp(_FogCol.rgb, _Color.rgb, 0.85), a);
            }
            ENDCG
        }
    }
}
