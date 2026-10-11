using UnityEngine;

public enum ItemType { None = 0, Flashlight = 1, Battery = 2, AlmondWater = 3, Glowstick = 4, VhsTape = 5 }

// Datos de los objetos del inventario (textos en español)
public static class ItemDefs
{
    public class Def
    {
        public ItemType type;
        public string name;
        public string description;
        public string useLabel;       // texto del botón "Usar"
        public int maxStack;
        public bool usable, droppable;
        public int preferredSlot;     // casilla preferida (0-4 = barra rápida)
    }

    private static readonly Def[] defs =
    {
        new Def { type = ItemType.None, name = "", description = "", maxStack = 0 },
        new Def { type = ItemType.Flashlight, name = "Linterna", useLabel = "Encender / apagar", maxStack = 1, usable = true, droppable = false, preferredSlot = 0,
            description = "Tu única compañera. Se gasta con el uso y parpadea cuando se queda sin batería. Recárgala con pilas. (F: encender/apagar)" },
        new Def { type = ItemType.Battery, name = "Pila", useLabel = "Recargar linterna", maxStack = 9, usable = true, droppable = true, preferredSlot = 1,
            description = "Pila alcalina que recarga la linterna. Se conservan al pasar de nivel. (B: usar una)" },
        new Def { type = ItemType.AlmondWater, name = "Agua de almendras", useLabel = "Beber", maxStack = 5, usable = true, droppable = true, preferredSlot = 2,
            description = "El clásico de las Backrooms. Dulce y tibia: recupera toda la resistencia y devuelve algo de cordura." },
        new Def { type = ItemType.Glowstick, name = "Barra luminosa", useLabel = "Lanzar", maxStack = 5, usable = true, droppable = true, preferredSlot = 3,
            description = "Ilumina una zona pequeña con luz verde durante unos 2 minutos. Usar: la lanzas hacia delante. Soltar: la dejas en el suelo." },
        new Def { type = ItemType.VhsTape, name = "Cinta de vídeo", useLabel = "Guardar", maxStack = 9, usable = false, droppable = true, preferredSlot = 4,
            description = "Una cinta VHS sin etiqueta. Alguien grabó aquí antes que tú... Solo es un recuerdo inquietante: no sirve para nada, pero no la tires." },
    };

    public static readonly ItemType[] All = { ItemType.Flashlight, ItemType.Battery, ItemType.AlmondWater, ItemType.Glowstick, ItemType.VhsTape };
    public static int Count { get { return defs.Length; } }

    public static Def Get(ItemType t)
    {
        int i = (int)t;
        return i >= 0 && i < defs.Length ? defs[i] : defs[0];
    }
}

// Notas de ambiente (3 por nivel) que se encuentran por los niveles y se leen desde el inventario
public static class LoreNotes
{
    public struct Note { public int id, level; public string title, text; }

    public const int PerLevel = 3;

    private static readonly string[][] data =
    {
        // Nivel 0 — Oficinas
        new[] {
            "Memo interno|A todo el personal: el pasillo este queda cerrado desde las 18:00. Si oyen pasos detrás de ustedes, no se giren. Sigan caminando hasta que el zumbido se calme.",
            "Post-it amarillo|Llevo tres días en la misma planta. Las puertas de los despachos dan siempre al mismo despacho. La moqueta está húmeda. ¿Por qué está húmeda?",
            "Nota arrugada|Lo vi desde la fotocopiadora: alto, de espaldas, demasiado pálido. No se mueve cuando lo miras. Cuando parpadeas, está más cerca." },
        // Nivel 1 — Aparcamiento
        new[] {
            "Tique de aparcamiento|Entrada 23:41. Salida: sin registrar. Planta -1, plaza 118. La barrera no se levanta. Las luces del fondo llevan días muertas.",
            "Nota sobre el capó|Algo enorme respira al otro lado de los pilares. Apagué la linterna y se quedó quieto. Creo que oye mejor de lo que ve.",
            "Aviso de la comunidad|Se recuerda que está prohibido estacionar en la rampa. Sobre todo de noche. Sobre todo si el suelo tiembla." },
        // Nivel 2 — Poolrooms
        new[] {
            "Cuaderno mojado|El agua sube un centímetro por hora. No sé de dónde viene. Está tibia. Anoche algo con demasiadas patas cruzó el pasillo sin hacer ruido.",
            "Cartel de la piscina|Prohibido correr. Prohibido bucear. Prohibido bañarse sin vigilancia. Aquí no hay vigilante desde hace años.",
            "Diario de J.|Si ves una puerta con un LED verde diminuto, no la busques: ella te encuentra a ti. Yo la crucé por casualidad. Me alegro de haberlo hecho." },
        // Nivel 3 — Hotel
        new[] {
            "Carta de recepción|Estimado huésped: su habitación 304 está lista desde hace años. El minibar no funciona. El pasillo tampoco termina. Disfrute de su estancia.",
            "Servilleta|Las lámparas se apagan cuando pasa el huésped de la 12. Lleva el cuello torcido. Siempre sonríe. Nunca parpadea.",
            "Registro de visitas|Entradas: 1.041. Salidas: 0. Última firma: la mía. Letra temblorosa. Mañana ya no sabré firmar." },
        // Nivel 4 — Industrial
        new[] {
            "Parte de turno|Caldera 3 sin presión. Turno de noche cancelado. Se oyen golpes dentro de las tuberías, a un ritmo constante. Parece que alguien cuenta.",
            "Etiqueta de seguridad|PELIGRO: no apagar las luces de emergencia. Lo que trabaja en la oscuridad nunca ha cobrado. Nunca ha cobrado.",
            "Última nota|Queda poco. La salida final está cerca, lo noto en el aire. Si lees esto, no corras donde ELLOS puedan oírte. Corre donde no." },
    };

    public static int Total { get { return data.Length * PerLevel; } }

    public static Note Get(int id)
    {
        int lv = id / 10, k = id % 10;
        Note n = new Note { id = id, level = lv, title = "?", text = "" };
        if (lv >= 0 && lv < data.Length && k >= 0 && k < data[lv].Length)
        {
            string[] p = data[lv][k].Split('|');
            n.title = p[0]; n.text = p[1];
        }
        return n;
    }

    public static int IdFor(int level, int k) { return level * 10 + k; }
    public static bool Valid(int id) { int lv = id / 10, k = id % 10; return lv >= 0 && lv < data.Length && k >= 0 && k < PerLevel; }
}
