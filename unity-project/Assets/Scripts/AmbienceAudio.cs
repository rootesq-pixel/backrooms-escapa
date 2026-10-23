using UnityEngine;

// Ambiente sonoro del nivel: drone en bucle + sucesos aleatorios en 3D alrededor del jugador
// (gotas y golpes metálicos). Todo generado por código.
public class AmbienceAudio : MonoBehaviour
{
    private AudioSource drone;
    private AudioSource events;
    private AudioClip droneClip;
    private AudioClip eventClip;
    private Transform listener;
    private AmbientEvent kind;
    private Vector2 interval;
    private float timer;
    private float droneBase;

    public void Setup(LevelConfig cfg, Transform listenerTransform)
    {
        listener = listenerTransform;
        kind = cfg.ambientEvent;
        interval = cfg.eventInterval;

        droneClip = ProceduralAudio.CreateDrone(cfg.droneHz, cfg.droneNoise, 1000 + cfg.index * 17);
        drone = gameObject.AddComponent<AudioSource>();
        drone.clip = droneClip;
        drone.loop = true;
        drone.spatialBlend = 0f;
        droneBase = cfg.droneVolume;
        drone.volume = AudioMix.Drone(droneBase);
        drone.playOnAwake = false;
        drone.Play();

        switch (kind)
        {
            case AmbientEvent.Drip: eventClip = ProceduralAudio.CreateDrip(); break;
            case AmbientEvent.Clank: eventClip = ProceduralAudio.CreateClank(); break;
        }
        if (eventClip != null)
        {
            GameObject go = new GameObject("AmbienceEvents");
            go.transform.SetParent(transform, false);
            events = go.AddComponent<AudioSource>();
            events.spatialBlend = 1f;
            events.rolloffMode = AudioRolloffMode.Linear;
            events.minDistance = 2f;
            events.maxDistance = 30f;
            events.playOnAwake = false;
        }
        timer = Random.Range(interval.x, interval.y) * 0.5f;
    }

    private void OnDestroy()
    {
        ProceduralAssets.SafeDestroy(droneClip);
        ProceduralAssets.SafeDestroy(eventClip);
    }

    private void Update()
    {
        if (drone != null) drone.volume = AudioMix.Drone(droneBase);   // sigue al deslizador «Ambiente»
        if (events == null || eventClip == null || listener == null) return;
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = Random.Range(interval.x, interval.y);

        Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(5f, 16f);
        events.transform.position = listener.position + new Vector3(dir.x, 0.5f, dir.y);
        events.pitch = Random.Range(0.8f, 1.2f);
        events.PlayOneShot(eventClip, AudioMix.Event(Random.Range(0.4f, 0.9f))); AudioEventLog.Note(eventClip.name);
    }
}
