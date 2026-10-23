// v7.3.0: máscara de agua de la barca (técnica estándar «boat water mask»). Una tapa invisible a la altura de la borda escribe
// un bit del stencil (64) donde se ve el interior del casco; la superficie del mar y las cáusticas descartan esos píxeles,
// así el interior queda seco desde el asiento, en tercera persona y para los jugadores remotos (cada cliente dibuja su barca).
// Va después del opaco (prueba de profundidad contra el casco) y antes de las cáusticas (Transparent-10) y del agua (Transparent).
Shader "Hidden/BoatWaterMask"
{
    SubShader
    {
        Tags { "Queue"="Transparent-40" "RenderType"="Opaque" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        Pass
        {
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Cull Off
            Stencil { Ref 64 WriteMask 64 Comp Always Pass Replace }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return 0; }
            ENDCG
        }
    }
}
