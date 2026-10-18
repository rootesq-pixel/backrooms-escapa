using UnityEngine;

// v7.0.4: dibuja (y hace sonar) un compañero, un monstruo o la copia del imitador en su imagen más cercana a la cámara
// local. La lógica (IA, red, colisiones del objeto raíz) sigue en la posición canónica; solo se desplaza el hijo «WrapView»,
// que contiene el modelo, la linterna, la etiqueta con el nombre y las fuentes de audio.
[DefaultExecutionOrder(30000)]
public class WrapView : MonoBehaviour
{
    public Transform view;
    public Vector3 CurrentShift { get; private set; }
    public static int ActiveShifts;   // pruebas / diagnóstico

    // mete todos los hijos actuales de 'root' bajo un contenedor WrapView (identidad local) y añade el componente
    public static WrapView Attach(GameObject root)
    {
        WrapView wv = root.GetComponent<WrapView>();
        if (wv != null) return wv;
        Transform v = new GameObject("WrapView").transform;
        v.SetParent(root.transform, false);
        int n = root.transform.childCount;
        Transform[] kids = new Transform[n];
        for (int i = 0; i < n; i++) kids[i] = root.transform.GetChild(i);
        foreach (Transform k in kids) if (k != v) k.SetParent(v, false);
        wv = root.AddComponent<WrapView>();
        wv.view = v;
        return wv;
    }

    // la posición donde se ve (mundo)
    public Vector3 VisiblePosition { get { return transform.position + CurrentShift; } }

    private void LateUpdate()
    {
        if (view == null) return;
        Vector3 shift = Vector3.zero;
        Camera cam = Camera.main;
        if (FieldWrap.On && cam != null) shift = FieldWrap.Shift(transform.position, cam.transform.position);
        if (shift.sqrMagnitude < 1e-6f) shift = Vector3.zero;
        if ((shift == Vector3.zero) != (CurrentShift == Vector3.zero)) ActiveShifts += shift == Vector3.zero ? -1 : 1;
        CurrentShift = shift;
        view.localPosition = shift == Vector3.zero ? Vector3.zero : transform.InverseTransformVector(shift);
    }

    private void OnDestroy() { if (CurrentShift != Vector3.zero) ActiveShifts--; }
}
