using UnityEngine;

// Jugador local derribado (multijugador): no puede moverse, la cámara cae al suelo y se inclina, tinte rojo.
// Un compañero lo reanima manteniendo E a su lado unos segundos (v7.2.0; lo decide el anfitrión).
public class NetDownedFx : MonoBehaviour
{
    public FirstPersonController fpc;
    public bool Down { get; private set; }
    public float Blend { get; private set; }
    private Vector3 basePos; private Quaternion baseRot = Quaternion.identity;

    public void SetDown(bool d)
    {
        if (Down == d) return;
        Down = d;
        if (d && fpc != null && fpc.cameraPivot != null) { basePos = fpc.cameraPivot.localPosition; baseRot = fpc.cameraPivot.localRotation; }
        if (fpc != null) fpc.InputEnabled = !d;
        GameManager gm = GameManager.Instance;
        if (gm != null && gm.overlay != null && gm.State == GameManager.GameState.Playing)
            gm.overlay.color = d ? new Color(0.45f, 0f, 0f, 0.38f) : new Color(0f, 0f, 0f, 0f);
    }

    private void LateUpdate()
    {
        // v7.2.0: al ser reanimado se incorpora más despacio (apoyándose: primero se endereza y luego sube)
        Blend = Mathf.MoveTowards(Blend, Down ? 1f : 0f, Time.unscaledDeltaTime * (Down ? 2.2f : 1.4f));
        if (Blend <= 0f || fpc == null || fpc.cameraPivot == null) return;
        Transform cam = fpc.cameraPivot;
        float hk = Down ? Blend : Mathf.SmoothStep(0f, 1f, Blend);
        float rk = Down ? Blend : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Blend * 1.6f - 0.6f));
        float lean = Down ? 0f : Mathf.Sin(Blend * Mathf.PI) * 14f;
        cam.localPosition = new Vector3(basePos.x, Mathf.Lerp(basePos.y, 0.28f, hk), basePos.z);
        cam.localRotation = baseRot * Quaternion.Euler(lean, 0f, 62f * rk);
        if (VhsEffect.Active != null) VhsEffect.Active.Spike(0.18f * Blend);
    }

    private void OnDestroy()
    {
        if (Down && fpc != null) fpc.InputEnabled = true;
    }
}
