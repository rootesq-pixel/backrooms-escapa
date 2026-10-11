using UnityEngine;

// Jugador local derribado (multijugador): no puede moverse, la cámara cae al suelo y se inclina, tinte rojo.
// Un compañero lo reanima quedándose a su lado unos segundos (lo decide el anfitrión).
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
        Blend = Mathf.MoveTowards(Blend, Down ? 1f : 0f, Time.unscaledDeltaTime * 2.2f);
        if (Blend <= 0f || fpc == null || fpc.cameraPivot == null) return;
        Transform cam = fpc.cameraPivot;
        cam.localPosition = new Vector3(basePos.x, Mathf.Lerp(basePos.y, 0.28f, Blend), basePos.z);
        cam.localRotation = baseRot * Quaternion.Euler(0f, 0f, 62f * Blend);
        if (VhsEffect.Active != null) VhsEffect.Active.Spike(0.18f * Blend);
    }

    private void OnDestroy()
    {
        if (Down && fpc != null) fpc.InputEnabled = true;
    }
}
