using UnityEngine;

// Desplaza lentamente el normal map del agua falsa (Standard comparte el offset de _MainTex_ST).
public class WaterScroller : MonoBehaviour
{
    public Material material;
    public Vector2 speed = new Vector2(0.012f, 0.007f);

    private void Update()
    {
        if (material != null)
        {
            material.mainTextureOffset += speed * Time.deltaTime;
        }
    }
}
