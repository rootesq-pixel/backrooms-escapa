using UnityEngine;

// v7.3.0: el casco de buzo de un compañero oculta la cabeza del traje; al quitarlo (fin del nivel) se vuelve a mostrar
public class HelmetHeadRestore : MonoBehaviour
{
    public Renderer hidden;
    private void OnDestroy() { if (hidden != null) hidden.enabled = true; }
}
