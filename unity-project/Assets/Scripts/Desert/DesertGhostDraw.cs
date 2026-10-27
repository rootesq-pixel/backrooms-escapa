using UnityEngine;

// v7.9.4: las copias del desierto alrededor del borde (DesertWorld.DrawGhosts) se recortan con la cámara de ESTE fotograma.
// Antes se dibujaban en el LateUpdate de DesertWorld, antes de que la cámara exterior del coche (orden 1000) se moviera;
// al cruzar el borde con la cámara por detrás faltaba un trozo de duna durante un fotograma.
[DefaultExecutionOrder(2000)]
public class DesertGhostDraw : MonoBehaviour
{
    public DesertWorld world;
    private void LateUpdate() { if (world != null) world.DrawGhosts(); }
}
