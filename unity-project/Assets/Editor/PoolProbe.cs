using UnityEngine;
using UnityEditor;
public static class PoolProbe
{
    public static void Run()
    {
        GameObject g = Resources.Load<GameObject>("Poolrooms/PoolHall");
        if (g == null) { Debug.Log("PROBE null"); return; }
        GameObject i = (GameObject)Object.Instantiate(g);
        Debug.Log("PROBE root scale " + i.transform.localScale + " rot " + i.transform.rotation.eulerAngles);
        foreach (Renderer r in i.GetComponentsInChildren<Renderer>())
            Debug.Log("PROBE " + r.name + " min " + r.bounds.min + " max " + r.bounds.max);
        foreach (MeshFilter mf in i.GetComponentsInChildren<MeshFilter>())
            Debug.Log("PROBE mesh " + mf.name + " " + mf.sharedMesh.bounds + " verts " + mf.sharedMesh.vertexCount + " lscale " + mf.transform.localScale + " rot " + mf.transform.localRotation.eulerAngles);
    }
}
