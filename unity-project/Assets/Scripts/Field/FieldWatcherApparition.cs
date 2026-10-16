using UnityEngine;

// v7.0 Campo infinito: «El Vigía» en el horizonte. Además del monstruo real (MonsterAI, que se agazapa en la hierba alta),
// cada cierto tiempo aparece una silueta inmóvil del mismo modelo a 40-55 m (justo al borde de la bruma), medio hundida en la hierba, mirando al jugador.
// Si el jugador la mira fijamente un momento o se acerca, desaparece. Es solo visual y local (no afecta a la partida ni al MP).
public class FieldWatcherApparition : MonoBehaviour
{
    public static FieldWatcherApparition Active;
    public Transform model;
    public bool Visible { get; private set; }
    public int Appearances { get; private set; }
    private float timer, lookT, life;
    private Transform viewer;
    private BackroomsGenerator gen;

    public static FieldWatcherApparition Create(Transform parent, BackroomsGenerator g)
    {
        GameObject prefab = Resources.Load<GameObject>("Monsters/FieldWatcher");
        if (prefab == null) return null;
        GameObject go = new GameObject("FieldWatcherApparition");
        go.transform.SetParent(parent, false);
        FieldWatcherApparition a = go.AddComponent<FieldWatcherApparition>();
        a.gen = g;
        GameObject m = Instantiate(prefab, go.transform);
        foreach (Collider c in m.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (Behaviour b in m.GetComponentsInChildren<Behaviour>(true)) if (!(b is Animator)) b.enabled = false;
        foreach (AudioSource s in m.GetComponentsInChildren<AudioSource>(true)) s.enabled = false;
        a.model = m.transform;
        m.SetActive(false);
        a.timer = 25f + Random.value * 20f;
        return a;
    }

    private void OnEnable() { Active = this; }
    private void OnDisable() { if (Active == this) Active = null; }

    private void Update()
    {
        if (model == null) return;
        if (viewer == null) { Camera c = Camera.main; if (c != null) viewer = c.transform; else return; }
        float dt = Time.deltaTime;
        if (!Visible)
        {
            timer -= dt;
            if (timer <= 0f) Appear();
            return;
        }
        life += dt;
        Vector3 to = model.position - viewer.position;
        float dist = new Vector2(to.x, to.z).magnitude;
        float ang = Vector3.Angle(viewer.forward, to);
        if (ang < 9f) lookT += dt; else lookT = Mathf.Max(0f, lookT - dt * 0.5f);
        // mira fijamente ~1,2 s, te acercas a menos de 35 m o pasan 40 s: se va (en el siguiente parpadeo de la vista)
        if (lookT > 1.2f || dist < 24f || life > 40f) Hide();
        else
        {
            Vector3 f = -to; f.y = 0f;
            if (f.sqrMagnitude > 0.01f) model.rotation = Quaternion.LookRotation(f.normalized);
        }
    }

    private void Appear()
    {
        Vector3 p = viewer.position;
        Vector3 fwd = viewer.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
        // en el borde de la visión (40-75° a un lado) para que se descubra de reojo
        float side = Random.value < 0.5f ? -1f : 1f;
        Vector3 dir = Quaternion.Euler(0f, side * Random.Range(40f, 75f), 0f) * fwd;
        Vector3 pos = p + dir * Random.Range(40f, 55f);
        Transform root = gen != null ? gen.Root : null;
        if (root != null)
        {
            Vector3 lp = root.InverseTransformPoint(pos);
            FieldWorld fw = FieldWorld.Active;
            if (fw != null && fw.plan != null && fw.plan.NoGrass(FieldHeight.Mod(lp.x), FieldHeight.Mod(lp.z))) { timer = 3f; return; }
            lp.y = FieldHeight.H(lp.x, lp.z) - 0.35f;   // medio hundido en la hierba
            pos = root.TransformPoint(lp);
        }
        model.position = pos;
        model.gameObject.SetActive(true);
        Visible = true; life = 0f; lookT = 0f; Appearances++;
    }

    private void Hide()
    {
        model.gameObject.SetActive(false);
        Visible = false;
        timer = 35f + Random.value * 40f;
    }
}
