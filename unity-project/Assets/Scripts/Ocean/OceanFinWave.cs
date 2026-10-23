using UnityEngine;

// v7.3.0: ondulación de la membrana de las aletas del Farolero (deforma los vértices en la CPU; pocas centenas por aleta).
// La raíz de la aleta es el origen de la malla: el desplazamiento crece con la distancia a la raíz.
public class OceanFinWave : MonoBehaviour
{
    private Mesh mesh;
    private Vector3[] rest, work;
    private float[] dist;
    private Vector3 nrm;
    private float amp, freq, phase, maxD = 1f;
    private Renderer rend;

    public static OceanFinWave Attach(Transform t, float amp, float freq, System.Action<Object> track)
    {
        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return null;
        OceanFinWave w = t.gameObject.AddComponent<OceanFinWave>();
        w.mesh = Object.Instantiate(mf.sharedMesh); w.mesh.name = mf.sharedMesh.name + "_Wave"; track(w.mesh);
        w.mesh.MarkDynamic(); mf.sharedMesh = w.mesh;
        w.rest = w.mesh.vertices; w.work = new Vector3[w.rest.Length]; w.dist = new float[w.rest.Length];
        Vector3[] ns = w.mesh.normals; Vector3 acc = Vector3.zero;
        for (int i = 0; i < ns.Length; i++) acc += Vector3.Dot(ns[i], ns.Length > 0 ? ns[0] : Vector3.right) >= 0f ? ns[i] : -ns[i];
        w.nrm = acc.sqrMagnitude > 1e-6f ? acc.normalized : Vector3.right;
        for (int i = 0; i < w.rest.Length; i++) { w.dist[i] = Vector3.ProjectOnPlane(w.rest[i], w.nrm).magnitude; w.maxD = Mathf.Max(w.maxD, w.dist[i]); }
        w.amp = amp; w.freq = freq; w.phase = Random.value * 6.28f;
        w.rend = t.GetComponent<Renderer>();
        return w;
    }

    private void LateUpdate()
    {
        if (mesh == null || (rend != null && !rend.isVisible)) return;
        float t = Time.time * freq + phase;
        for (int i = 0; i < rest.Length; i++)
        {
            float d = dist[i] / maxD;
            work[i] = rest[i] + nrm * (Mathf.Sin(t - d * 3.2f) * amp * d * maxD);
        }
        mesh.vertices = work;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}
