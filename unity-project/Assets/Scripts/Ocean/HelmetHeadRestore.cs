using UnityEngine;

// v7.3.0: el casco de buzo de un compañero oculta la cabeza del traje; al quitarlo (fin del nivel) se vuelve a mostrar
public class HelmetHeadRestore : MonoBehaviour
{
    public Renderer hidden;
    private void OnDestroy() { if (hidden != null) hidden.enabled = true; }
}

// v7.11.1: con el casco puesto, la capucha del personaje CC5 (más voluminosa que una cabeza) se encoge un poco dentro de la
// cúpula para que el casco pueda tener un tamaño de casco de buzo de verdad. Se reaplica cada frame (después de animaciones y
// de PlayerBody) y se deshace al quitar el casco.
[DefaultExecutionOrder(1000)]
public class HelmetHoodShrink : MonoBehaviour
{
    public Transform bone; public Vector3 baseScale = Vector3.one; public float factor = 1f;
    private void LateUpdate() { if (bone != null) bone.localScale = baseScale * factor; }
    private void OnDestroy() { if (bone != null) bone.localScale = baseScale; }
}
