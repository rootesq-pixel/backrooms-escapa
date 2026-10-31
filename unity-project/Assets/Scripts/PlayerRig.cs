using System.Collections.Generic;
using UnityEngine;

// v7.11.0: el cuerpo del jugador es el personaje de Character Creator 5 (esqueleto CC_Base_*). El código del cuerpo, del
// conductor, de los avatares remotos y de las pruebas busca los huesos con los nombres del traje antiguo (Quaternius:
// «Head», «UpperArm.R», «Middle1.R», «Foot.L»...). PlayerRig.Find devuelve el hueso equivalente de cualquiera de los dos
// esqueletos (primero el nombre exacto; si no existe, el hueso CC).
public static class PlayerRig
{
    public static readonly Dictionary<string, string> CC = new Dictionary<string, string>
    {
        { "Hips", "CC_Base_Hip" }, { "Torso", "CC_Base_Spine01" }, { "Chest", "CC_Base_Spine02" },
        { "Neck", "CC_Base_NeckTwist01" }, { "Head", "CC_Base_Head" },
        { "Shoulder.R", "CC_Base_R_Clavicle" }, { "Shoulder.L", "CC_Base_L_Clavicle" },
        { "UpperArm.R", "CC_Base_R_Upperarm" }, { "UpperArm.L", "CC_Base_L_Upperarm" },
        { "LowerArm.R", "CC_Base_R_Forearm" }, { "LowerArm.L", "CC_Base_L_Forearm" },
        { "Hand.R", "CC_Base_R_Hand" }, { "Hand.L", "CC_Base_L_Hand" },
        { "Middle1.R", "CC_Base_R_Mid1" }, { "Middle2.R", "CC_Base_R_Mid2" }, { "Middle3.R", "CC_Base_R_Mid3" },
        { "Middle1.L", "CC_Base_L_Mid1" }, { "Middle2.L", "CC_Base_L_Mid2" }, { "Middle3.L", "CC_Base_L_Mid3" },
        { "Index1.R", "CC_Base_R_Index1" }, { "Index1.L", "CC_Base_L_Index1" },
        { "UpperLeg.R", "CC_Base_R_Thigh" }, { "UpperLeg.L", "CC_Base_L_Thigh" },
        { "LowerLeg.R", "CC_Base_R_Calf" }, { "LowerLeg.L", "CC_Base_L_Calf" },
        { "Foot.R", "CC_Base_R_Foot" }, { "Foot.L", "CC_Base_L_Foot" },
    };

    // escala del prefab CC: persona de 1,76 m (1,81 m con capucha y suela), como pide el diseño (1,75–1,80 m)
    public const float CcScale = 0.98f;

    public static Transform Find(Transform root, string name)
    {
        if (root == null) return null;
        Transform t = Deep(root, name);
        if (t != null) return t;
        string cc;
        return CC.TryGetValue(name, out cc) ? Deep(root, cc) : null;
    }

    // ¿es este transform el hueso «name» (en cualquiera de los dos esqueletos)?
    public static bool Is(Transform t, string name)
    {
        if (t == null) return false;
        if (t.name == name) return true;
        string cc;
        return CC.TryGetValue(name, out cc) && t.name == cc;
    }

    public static bool IsCC(Transform root) { return Deep(root, "CC_Base_Hip") != null; }

    private static Transform Deep(Transform r, string n)
    {
        if (r.name == n) return r;
        for (int i = 0; i < r.childCount; i++) { Transform t = Deep(r.GetChild(i), n); if (t != null) return t; }
        return null;
    }
}
