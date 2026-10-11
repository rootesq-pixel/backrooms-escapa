using System.Collections.Generic;
using UnityEngine;

// Limita las luces en tiempo real: solo las N más cercanas al jugador están encendidas;
// el resto de accesorios siguen siendo emisivos (paneles brillantes) pero no iluminan.
// El cambio es gradual (fade) para que no se note el "pop".
public class LightCuller : MonoBehaviour
{
    public Transform viewer;
    public int maxActive = 10;
    public float updateInterval = 0.2f;
    public float fadeSpeed = 3f;

    private readonly List<FlickeringLight> fixtures = new List<FlickeringLight>();
    private readonly List<float> sqrDist = new List<float>();
    private readonly List<int> order = new List<int>();
    private float timer;
    private bool[] wanted = new bool[0];

    public int FixtureCount { get { return fixtures.Count; } }

    public int ActiveCount
    {
        get
        {
            int c = 0;
            for (int i = 0; i < fixtures.Count; i++)
            {
                if (fixtures[i] != null && fixtures[i].targetLight != null && fixtures[i].targetLight.enabled) c++;
            }
            return c;
        }
    }

    public void Register(FlickeringLight f)
    {
        fixtures.Add(f);
        f.externalFade = 0f;
        if (f.targetLight != null) f.targetLight.enabled = false;
    }

    private void Start()
    {
        Evaluate(true);
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            timer = updateInterval;
            Evaluate(false);
        }

        float step = fadeSpeed * Time.deltaTime;
        for (int i = 0; i < fixtures.Count && i < wanted.Length; i++)
        {
            FlickeringLight f = fixtures[i];
            if (f == null) continue;
            float target = wanted[i] ? 1f : 0f;
            if (!Mathf.Approximately(f.externalFade, target))
            {
                f.externalFade = Mathf.MoveTowards(f.externalFade, target, step);
            }
        }
    }

    private void Evaluate(bool snap)
    {
        if (viewer == null)
        {
            Camera cam = Camera.main;
            if (cam != null) viewer = cam.transform;
        }
        int n = fixtures.Count;
        if (wanted.Length != n) wanted = new bool[n];
        if (viewer == null) return;

        Vector3 p = viewer.position;
        sqrDist.Clear();
        order.Clear();
        for (int i = 0; i < n; i++)
        {
            FlickeringLight f = fixtures[i];
            float d = f == null ? float.MaxValue : (f.transform.position - p).sqrMagnitude;
            sqrDist.Add(d);
            order.Add(i);
        }
        order.Sort((a, b) => sqrDist[a].CompareTo(sqrDist[b]));
        for (int i = 0; i < n; i++) wanted[i] = false;
        for (int k = 0; k < order.Count && k < maxActive; k++)
        {
            wanted[order[k]] = true;
        }

        if (snap)
        {
            for (int i = 0; i < n; i++)
            {
                if (fixtures[i] != null) fixtures[i].externalFade = wanted[i] ? 1f : 0f;
            }
        }
    }
}
