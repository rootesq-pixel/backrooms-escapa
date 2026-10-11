using System.Collections.Generic;
using UnityEngine;

// Cámara del fondo del menú principal: recorre lentamente los pasillos del nivel 0 siguiendo caminos entre celdas.
public class MenuCameraRig : MonoBehaviour
{
    private BackroomsGenerator gen;
    private readonly List<Vector3> path = new List<Vector3>();
    private int index;
    private Vector3 pos;
    private Quaternion rot;
    private float speed = 0.85f;
    private float t;
    public bool HasPath { get { return path.Count > 1; } }
    public float Travelled { get; private set; }

    public void Setup(BackroomsGenerator generator)
    {
        gen = generator;
        pos = gen.PlayerStart; pos.y = 1.6f;
        rot = Quaternion.Euler(0f, gen.PlayerStartYaw, 0f);
        transform.SetPositionAndRotation(pos, rot);
        PickPath();
    }

    private void PickPath()
    {
        path.Clear();
        for (int attempt = 0; attempt < 6 && path.Count < 2; attempt++)
        {
            Vector3 dest = gen.GetRandomCellCenter();
            if (gen.TryGetPath(pos, dest, path) && path.Count > 1) break;
        }
        index = 0;
    }

    private void Update()
    {
        if (gen == null) return;
        float dt = Time.unscaledDeltaTime;
        t += dt;
        if (path.Count < 2 || index >= path.Count) { PickPath(); if (path.Count < 2) return; }

        Vector3 target = path[Mathf.Min(index, path.Count - 1)];
        target.y = 1.6f;
        Vector3 to = target - pos;
        float d = to.magnitude;
        if (d < 0.25f) { index++; if (index >= path.Count) { PickPath(); } return; }
        Vector3 step = to / d * speed * dt;
        pos += step;
        Travelled += step.magnitude;
        // mirada suave hacia el siguiente punto, con ligero balanceo
        Vector3 look = to; look.y = 0f;
        if (look.sqrMagnitude > 0.001f)
            rot = Quaternion.Slerp(rot, Quaternion.LookRotation(look), 1f - Mathf.Exp(-1.6f * dt));
        Vector3 bob = new Vector3(0f, Mathf.Sin(t * 1.9f) * 0.02f, 0f);
        transform.position = pos + bob;
        transform.rotation = rot * Quaternion.Euler(Mathf.Sin(t * 0.6f) * 1.5f, 0f, Mathf.Sin(t * 0.43f) * 1.0f);
    }
}
