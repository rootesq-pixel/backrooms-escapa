using UnityEditor;
using UnityEngine;

public static class ElevatorProbe
{
    public static void Run()
    {
        GameObject p = Resources.Load<GameObject>("Elevator/Elevator");
        if (p == null) { Debug.LogError("[ElevatorProbe] modelo NO cargado"); return; }
        GameObject g = Object.Instantiate(p);
        Debug.Log("[ElevatorProbe] root " + g.name + " rot=" + g.transform.rotation.eulerAngles + " scale=" + g.transform.localScale);
        foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
        {
            string mats = ""; foreach (Material m in r.sharedMaterials) mats += (m != null ? m.name : "null") + ",";
            Debug.Log("[ElevatorProbe] " + r.name + " lpos=" + r.transform.localPosition + " lrot=" + r.transform.localEulerAngles + " lscale=" + r.transform.localScale + " bounds=" + r.bounds.min + ".." + r.bounds.max + " mats=" + mats);
        }
        Object.DestroyImmediate(g);
    }
}
