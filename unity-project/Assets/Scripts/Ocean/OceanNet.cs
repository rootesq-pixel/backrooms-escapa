using System.Collections.Generic;
using UnityEngine;

// v7.3.0 Talasofobia: capa de red del nivel. El anfitrión (o el jugador en solitario) es la autoridad de la barca, la jaula,
// los cascos, los peces y el Farolero. Los clientes piden cosas (subir/bajar de la barca, timón, coger casco, bengala)
// y aplican lo que manda el anfitrión (eventos + bloque «mar» del snapshot).
public static class OceanNet
{
    // operaciones (cliente → anfitrión)
    public const int OpBoard = 1, OpLeave = 2, OpDrive = 3, OpHelmet = 4, OpFlare = 5, OpDrown = 6;
    // eventos (anfitrión → todos)
    public const int EvHelmet = 1, EvFlare = 2, EvSeat = 3, EvKilled = 4, EvLighthouse = 5;
    // causas de muerte por el mar
    public const int KillDrown = 1, KillFish = 2, KillAngler = 3;

    public static bool Authority { get { return !NetSession.Active || NetSession.IsHost; } }
    public static int LocalId { get { return NetSession.Active ? NetSession.Instance.LocalId : 0; } }
    public static bool ForceTargetsForTests;

    public static string KillText(int code)
    {
        switch (code)
        {
            case KillDrown: return "TE HAS AHOGADO";
            case KillFish: return "ALGO PÁLIDO TE HA ARRASTRADO AL FONDO";
            case KillAngler: return "LA LUZ ERA UN CEBO";
            default: return "EL MAR TE HA ENGULLIDO";
        }
    }

    // ---- peticiones del jugador local
    public static void Request(int op, int a, float x, float y) { Request(op, a, x, y, Vector3.zero, Vector3.zero); }
    public static void Request(int op, int a, float x, float y, Vector3 p, Vector3 d)
    {
        OceanWorld w = OceanWorld.Active;
        if (w == null) return;
        if (Authority) { w.HostRequest(LocalId, op, a, x, y, p, d); return; }
        if (NetSession.Instance != null) NetSession.Instance.SendOceanReq(op, a, x, y, p, d);
    }

    // ---- eventos del anfitrión (se aplican aquí y se envían a los clientes)
    public static void Event(int ev, int a, int b, Vector3 p, Vector3 d)
    {
        if (NetSession.Active && NetSession.IsHost) NetSession.Instance.BroadcastOceanEvent(ev, a, b, p, d);
        if (OceanWorld.Active != null) OceanWorld.Active.ApplyEvent(ev, a, b, p, d);
    }

    // ---- objetivos (jugadores) para los peces y el Farolero (solo en la autoridad)
    public struct Target
    {
        public int id; public Transform t; public Vector3 pos; public bool swimming, mounted, helmet, lamp, downed, local, climbing;
    }
    private static readonly List<Target> targets = new List<Target>();
    public static List<Target> Targets()
    {
        targets.Clear();
        OceanWorld w = OceanWorld.Active;
        OceanPlayer lp = OceanPlayer.Local;
        if (!NetSession.Active)
        {
            if (lp != null && lp.fpc != null)
                targets.Add(new Target { id = 0, t = lp.transform, pos = lp.transform.position, swimming = lp.fpc.IsSwimming, mounted = lp.fpc.IsMounted, climbing = lp.fpc.IsClimbingLadder, helmet = lp.HasHelmet, lamp = lp.LampOn, downed = false, local = true });
            return targets;
        }
        foreach (NetPlayer p in NetSession.Instance.Players)
        {
            if (p.isLocal)
            {
                if (lp == null || lp.fpc == null) continue;
                targets.Add(new Target { id = p.id, t = lp.transform, pos = lp.transform.position, swimming = lp.fpc.IsSwimming, mounted = lp.fpc.IsMounted, climbing = lp.fpc.IsClimbingLadder, helmet = lp.HasHelmet, lamp = lp.LampOn, downed = p.downed, local = true });
            }
            else if (p.avatar != null)
            {
                Vector3 pos = p.avatar.LatestPos;
                bool mounted = w != null && w.IsMountedHost(p.id);
                targets.Add(new Target { id = p.id, t = p.avatar.transform, pos = pos, swimming = (p.flags & RemotePlayer.FlagSwim) != 0, mounted = mounted, climbing = false, helmet = w != null && w.HasHelmetHost(p.id), lamp = (p.flags & RemotePlayer.FlagFlash) != 0, downed = p.downed, local = false });
            }
        }
        return targets;
    }

    // la autoridad mata/derriba a un jugador por el mar
    public static void HostKill(int playerId, int code)
    {
        if (!Authority) return;
        if (!NetSession.Active)
        {
            if (GameManager.Instance != null) GameManager.Instance.PlayerKilled(KillText(code));
            return;
        }
        NetSession.Instance.HostHazardCaught(playerId, code);
    }
}
