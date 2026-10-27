using UnityEngine;

public enum ItemType { None = 0, Flashlight = 1, Battery = 2, AlmondWater = 3, Glowstick = 4, VhsTape = 5, Key0 = 6, Key1 = 7, Key2 = 8, Key3 = 9, Key4 = 10, FlareGun = 11, RoyalRations = 12, CashewWater = 13, NeonWater = 14 }

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
        public float weight;          // kg por unidad (solo informativo)
        public string condition;      // estado mostrado en la etiqueta del inventario
    }

    private static readonly Def[] defs =
    {
        new Def { type = ItemType.None, name = "", description = "", maxStack = 0 },
        new Def { type = ItemType.Flashlight, name = "Linterna", weight = 0.35f, condition = "Usada, abollada", useLabel = "Encender / apagar", maxStack = 1, usable = true, droppable = false, preferredSlot = 0,
            description = "Tu única compañera. Se gasta con el uso y parpadea cuando se queda sin batería. Recárgala con pilas. (F: encender/apagar)" },
        new Def { type = ItemType.Battery, name = "Pila", weight = 0.023f, condition = "Nueva", useLabel = "Recargar linterna", maxStack = 9, usable = true, droppable = true, preferredSlot = 1,
            description = "Pila alcalina AA que recarga la linterna. Las pilas se conservan al pasar de nivel. (B: usar una)" },
        new Def { type = ItemType.AlmondWater, name = "Agua de almendras", weight = 0.5f, condition = "Precintada", useLabel = "Beber", maxStack = 5, usable = true, droppable = true, preferredSlot = 2,
            description = "El clásico de las Backrooms. Dulce y tibia: recupera toda la resistencia y devuelve algo de cordura." },
        new Def { type = ItemType.Glowstick, name = "Barra luminosa", weight = 0.03f, condition = "Sin activar", useLabel = "Lanzar", maxStack = 5, usable = true, droppable = true, preferredSlot = 3,
            description = "Ilumina una zona pequeña con luz verde durante unos 2 minutos. Usar: la lanzas hacia delante. Soltar: la dejas en el suelo." },
        new Def { type = ItemType.VhsTape, name = "Cinta de vídeo", weight = 0.25f, condition = "Desgastada", useLabel = "Guardar", maxStack = 9, usable = false, droppable = true, preferredSlot = 4,
            description = "Una cinta VHS con una etiqueta escrita a mano: «NO ABRIR - 1994». Alguien grabó aquí antes que tú. No sirve para nada, pero no la tires." },
        // v7.0.4: llaves señuelo (parecidas; una sola abre la salida del nivel). No pasan al nivel siguiente.
        new Def { type = ItemType.Key0, name = "Llave de latón", weight = 0.012f, condition = "Desgastada", useLabel = "Probar en la puerta", maxStack = 1, usable = true, droppable = true, preferredSlot = 0,
            description = "Llave plana de latón con la cabeza redonda y «TESA» grabado. Solo una de las llaves que hay por el nivel abre la puerta de salida. Al llegar a la puerta se prueban solas; si no es, queda marcada." },
        new Def { type = ItemType.Key1, name = "Llave de acero", weight = 0.014f, condition = "Con arañazos", useLabel = "Probar en la puerta", maxStack = 1, usable = true, droppable = true, preferredSlot = 0,
            description = "Llave de acero con la cabeza cuadrada y una etiqueta grabada: «B-12». Solo una de las llaves que hay por el nivel abre la puerta de salida. Al llegar a la puerta se prueban solas; si no es, queda marcada." },
        new Def { type = ItemType.Key2, name = "Llave de hierro antigua", weight = 0.03f, condition = "Oxidada", useLabel = "Probar en la puerta", maxStack = 1, usable = true, droppable = true, preferredSlot = 0,
            description = "Llave de paletón, pesada y fría, de anilla redonda. Parece mucho más vieja que este sitio. Solo una de las llaves que hay por el nivel abre la puerta de salida. Al llegar a la puerta se prueban solas; si no es, queda marcada." },
        new Def { type = ItemType.Key3, name = "Llave de bronce", weight = 0.012f, condition = "Verdosa en los bordes", useLabel = "Probar en la puerta", maxStack = 1, usable = true, droppable = true, preferredSlot = 0,
            description = "Llave de bronce con la cabeza de trébol y «ACE» grabado. Solo una de las llaves que hay por el nivel abre la puerta de salida. Al llegar a la puerta se prueban solas; si no es, queda marcada." },
        new Def { type = ItemType.Key4, name = "Llave niquelada", weight = 0.013f, condition = "Brillante", useLabel = "Probar en la puerta", maxStack = 1, usable = true, droppable = true, preferredSlot = 0,
            description = "Llave niquelada con un llavero de cartón: un «4» escrito a boli. Solo una de las llaves que hay por el nivel abre la puerta de salida. Al llegar a la puerta se prueban solas; si no es, queda marcada." },
        // v7.3.0 Talasofobia: pistola de bengalas (cada unidad es un cartucho). No pasa al nivel siguiente.
        new Def { type = ItemType.FlareGun, name = "Pistola de bengalas", weight = 0.6f, condition = "Naranja, descolorida", useLabel = "Disparar al cielo", maxStack = 3, usable = true, droppable = true, preferredSlot = 3,
            description = "Pistola de señales de plástico naranja con tres cartuchos. Dispara una bengala roja que sube muy alto y se ve a través de la niebla: tus compañeros sabrán dónde estás." },
        // v7.7.0 Nivel 114 «El Desierto Cálido»: lo que aparece tirado en la arena
        new Def { type = ItemType.RoyalRations, name = "Raciones Reales", weight = 0.4f, condition = "Caducadas", useLabel = "Comer", maxStack = 5, usable = true, droppable = true, preferredSlot = 2,
            description = "Una lata de raciones con un sello real descolorido. Caducaron hace años, pero quitan el hambre: recuperas algo de resistencia y de cordura (a veces saben fatal)." },
        new Def { type = ItemType.CashewWater, name = "Agua de anacardo", weight = 0.5f, condition = "Tibia", useLabel = "Beber", maxStack = 5, usable = true, droppable = true, preferredSlot = 2,
            description = "Prima del agua de almendras, más espesa y con sabor a fruto seco tostado. Recupera bastante resistencia y un poco de cordura." },
        new Def { type = ItemType.NeonWater, name = "Agua de neón", weight = 0.5f, condition = "Brilla en la oscuridad", useLabel = "Beber", maxStack = 3, usable = true, droppable = true, preferredSlot = 2,
            description = "Rarísima: un líquido que brilla con luz propia. Fíjate en el suelo del desierto, a veces asoma entre la arena. Recupera toda la resistencia, mucha cordura y recarga algo la linterna." },
    };
    public static bool IsKey(ItemType t) { return t >= ItemType.Key0 && t <= ItemType.Key4; }
    public static readonly ItemType[] Keys = { ItemType.Key0, ItemType.Key1, ItemType.Key2, ItemType.Key3, ItemType.Key4 };

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
            "Tique de aparcamiento|Entrada 23:41. Salida: sin registrar. Planta -1, plaza 118. Los carteles de SALIDA apuntan a todas partes; yo bajaría. Hasta abajo del todo.",
            "Nota sobre el capó|Algo enorme respira al otro lado de los pilares. Apagué la linterna y se quedó quieto. Creo que oye mejor de lo que ve.",
            "Aviso de la comunidad|Se ruega a los usuarios que devuelvan las llaves al vigilante. Hay muchas copias perdidas por las plantas y solo una abre la puerta de abajo. Las demás entran, pero no giran." },
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
        // Nivel 5 — Campo infinito
        new[] {
            "Mapa dibujado a mano|He caminado recto tres horas. He vuelto a pasar junto a la misma casa. Misma ventana rota, mismo tablón suelto. Ya no dibujo, solo cuento casas.",
            "Hoja de cuaderno mojada|Hay algo alto y blanco a lo lejos, entre la hierba. Si lo miras, ya no está. Si dejas de mirarlo, está más cerca. No lo mires mucho. No dejes de mirarlo.",
            "Nota clavada en un poste|Las puertas sueltas no llevan a ninguna parte. Todas menos una. Esa zumba como una radio vieja y tiene una luz cansada al lado. Escucha el viento… y lo que no es viento." },
        // Nivel 6 — Parque de bolas
        new[] {
            "Pulsera de entrada|Pulsera de papel, color naranja: «Niño 4-8 años · Entrada 17:05 · Salida ____». Tiene una mancha de batido seca. Nadie rellenó la hora de salida.",
            "Hoja de incidencias|Día 12: falta una bola roja. Día 13: faltan doce. Día 14: la piscina grande tiene MÁS bolas que ayer y está más honda. No meter la mano hasta el fondo.",
            "Nota infantil a lápiz|Mi amigo vive debajo de las bolas. Si te quedas muy quieto en el borde sale a jugar. No le gusta que le pisen. Las llaves las guarda él." },
        // Nivel 7 — Talasofobia (v7.3.0)
        new[] {
            "Libreta hinchada de agua|El agua entró por debajo de la puerta sin hacer ruido y ya no se fue. Desde la ventana de arriba se ve una luz muy lejos, entre la niebla. Se enciende cuando te acercas, como si te esperara.",
            "Etiqueta de un bidón|NO NADAR ENTRE LAS CASAS. Lo blanco que asoma no es espuma. Si da vueltas a tu alrededor, ya te ha elegido: agárrate a una escalera o sube a la barca. Ahí no llegan.",
            "Cuaderno de bitácora|Bajé con el casco hasta el fondo de la fosa. Abajo hay una luz pequeña, verdosa, que se mueve sola. NO es una luz. Apaga la lámpara si la ves. La gruta está a la izquierda de la puerta de la jaula, un poco a la izquierda, y sube." },
        // Nivel 8 — Nivel 114, El Desierto Cálido (v7.7.0)
        new[] {
            "Diario cubierto de arena|Llevo tres días andando y la arena sigue tibia, de día y de noche. No hay nadie: ni bichos, ni monstruos, nada. Solo pájaros de día. Al que cacé le salió sangre amarilla. De noche no queda ni uno.",
            "Papel clavado en un pozo|Si la arena te traga, no luches: no te mueres, apareces en otro sitio del desierto. De noche lleva linterna y no corras cuesta abajo, que resbala. Lo blanco que cae a veces del cielo es nieve, aunque no haga frío.",
            "Hoja arrancada de un mapa|En uno de estos pueblos una casa no tiene puerta de madera: la tiene de PIEDRA. Esa es la salida. Busca el pueblo con la torre alta y mira bien todas las fachadas." },
        // Nivel 9 — Nivel 7460, El pasillo de las puertas (diseño propio: la wiki no tiene datos)
        new[] {
            "Hoja pegada con cinta|He contado 212 puertas y luego he dejado de contar. Son todas iguales: blancas, la manilla a la misma altura, el mismo arañazo en el marco. Casi todas están cerradas. Las que se abren dan a la misma habitación vacía.",
            "Tarjeta de visita en blanco|No hay nadie más aquí. Lo sé. Pero cuando camino, alguien camina detrás de mí, al mismo paso. Si me giro, se calla. Si me paro, se para. No te gires mucho: no le gusta que lo busquen.",
            "Nota doblada en cuatro|Detrás de una de estas puertas hay un muro de ladrillo. Detrás de otra, la salida: una puerta que no se abre con la manilla, sino con la llave buena. Las llaves están en las habitaciones. Solo una encaja." },
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
