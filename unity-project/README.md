# Backrooms Escape (esqueleto de proyecto Unity)

Juego de terror en primera persona para PC: **escapa de las Backrooms** a través de **10 niveles** encadenados.
Cada nivel es un laberinto (o aparcamiento abierto) procedural con su propia estética, luces, dificultad y
entidades que patrullan y te persiguen. La salida es una puerta discreta que **solo se descubre explorando**
(sin cartel, sin flecha, sin indicador de distancia).

Casi todo se genera **desde código** (mallas, texturas PBR con ruido Perlin/fBm, audio); los únicos assets externos son los modelos de monstruos, el traje hazmat y la cabina del ascensor (CC0, ver `CREDITS.md`).

## Niveles

| Nivel | Nombre | Tema y mecánicas | Monstruos | Linterna / pilas |
|---|---|---|---|---|
| 0 | Oficinas amarillas | Clásico: papel pintado amarillento húmedo, moqueta marrón, techo de placas y fluorescentes parpadeantes. Laberinto 16×16 con salas abiertas. | 1 «El Alto» (patrulla 1.8, persecución 4.0) | Drenaje 0.5/s, 4 pilas |
| 1 | Aparcamiento | Hormigón gris con manchas de aceite y líneas de plaza, rejilla abierta 20×20 con pilares y muros sueltos, techo alto, muy oscuro; tubos fluorescentes muy separados y a menudo muertos. | 1 «El Coloso» (2.0 / 4.4, ve hasta 24 m) | Drenaje 0.7/s, 5 pilas |
| 2 | **Poolrooms** | Azulejos blancos de piscina con franja azul y **agua realista** (olas Gerstner, reflejos, espuma, cáusticas, vadeo, restos flotantes); luz azulada; caminas más lento y los pasos chapotean. | 1 «El Ahogado» (2.0 / 4.5, oído ×1.3) | Drenaje 0.75/s, sin recarga pasiva, 5 pilas |
| 3 | Hotel abandonado | Pasillos estrechos con moqueta roja, papel damasco y zócalo de madera, **solo lámparas de pared** casi muertas y niebla densa: la linterna es imprescindible. | 2 «El Huésped» (2.0 / 4.4, ven menos a oscuras, oído ×1.2) | Drenaje 0.9/s, sin recarga, 7 pilas de +40 |
| 4 | Complejo industrial | Chapa oxidada con remaches, suelo de chapa lagrimada, **tuberías**, luces de emergencia rojas que pulsan. | 2 «El Hollín», **más rápidos** (2.3 / 5.3, oído ×1.4) | Drenaje 0.95/s, sin recarga, 6 pilas de +40 |

Flujo de juego: al llegar a la puerta de salida se carga el siguiente nivel; tras el nivel 4 aparece la pantalla de
victoria (tiempo total e intentos fallidos) y **R** vuelve a empezar desde el Nivel 0. Si te atrapan, el nivel actual
se reinicia solo a los pocos segundos (o al instante con **R**). Cada nivel empieza con una tarjeta de título
(«NIVEL 2 — POOLROOMS») durante unos segundos.

## Requisitos

- **Unity 2022.3 LTS o superior** (el proyecto se creó con `2022.3.62f3`; funciona con cualquier 2022.3.x).
- **Pipeline Built-in (el estándar)**. No se necesita URP.
  - Si conviertes el proyecto a URP, `ProceduralAssets` detecta el pipeline y usa `Universal Render Pipeline/Lit`
    automáticamente, aunque conviene revisar luces y niebla.
- **Input Manager clásico** (`Input.GetAxis`). No se usa el paquete *Input System*.
  - Comprueba `Edit > Project Settings > Player > Other Settings > Active Input Handling` = **Input Manager (Old)**
    o **Both**. (En Unity 6 las plantillas nuevas lo ponen en "Input System", cámbialo.)
- Paquetes (ya incluidos en `Packages/manifest.json`):
  - `com.unity.ugui` (UI legacy: `Text`, `Image`).
  - `com.unity.ai.navigation` 1.1.5 (NavMeshSurface para hornear la NavMesh en runtime; **opcional**, ver más abajo).

## Puesta en marcha

1. Abre **Unity Hub** → *Add* → *Add project from disk* → selecciona la carpeta `BackroomsEscape`.
   Si no tienes exactamente `2022.3.62f3`, elige otra 2022.3.x (o más reciente) cuando Unity Hub lo pregunte
   y acepta la actualización del proyecto.
2. Espera a que Unity importe y resuelva los paquetes (necesita internet la primera vez por `com.unity.ai.navigation`).
3. Abre la escena `Assets/Scenes/Main.unity` (ya contiene un objeto vacío `GameBootstrap` con el script).
4. Pulsa **Play**. Haz clic en la ventana *Game* si el cursor no se captura.

### Si la escena no funciona (montaje manual en 3 pasos)

1. `File > New Scene` (Basic o Empty). Borra la `Main Camera` (el bootstrap crea la suya).
2. `GameObject > Create Empty`, llámalo `GameBootstrap` y arrastra encima `Assets/Scripts/GameBootstrap.cs`.
3. Guarda la escena (por ejemplo `Assets/Scenes/Main.unity`) y pulsa **Play**.

## Controles

| Acción | Tecla |
|---|---|
| Moverse (con aceleración / frenado suaves) | `W` `A` `S` `D` |
| Mirar | Ratón |
| Correr (stamina limitada; FOV más abierto y respiración más pesada al cansarte) | `Shift` |
| Agacharse (mantener / alternar) | `Ctrl` / `C` |
| Saltar el ascensor (solo tras morir y reiniciar el nivel) | `Espacio` / `E` |
| Reanimar a un compañero derribado (multijugador, mantener cerca) | `E` |
| Linterna on/off | `F` |
| Inventario en rejilla 5×3 + notas (pausa el juego; `I` o `Esc` lo cierra; flechas / clic para elegir, `Tab` cambia Objetos/Notas, botones Usar / Soltar) | `I` |
| Barra rápida: usar el objeto de la casilla 1-5 (1 = linterna, resto según lo recogido) | `1`-`5` |
| Usar una pila (recarga la linterna) | `B` (o botón «Recargar linterna» en el inventario) |
| Silbar (los monstruos cercanos van a investigar; los compañeros lo oyen) | `G` |
| Modo cámara VHS on/off | `V` |
| Nadar / bucear en agua profunda: avanzar + `Ctrl`/`C` para sumergirte | `W` + `Ctrl` |
| Menú de pausa (Continuar / Reiniciar nivel / Opciones / Menú principal / Salir). En multijugador la partida **no** se detiene | `Esc` |
| Chat de texto (solo multijugador; `Enter` envía, `Esc` cancela) | `Enter` |
| Reiniciar el nivel actual (en la pantalla final: volver al Nivel 0) | `R` |

Menús (todo en español, construido por código con `UnityEngine.UI`): **menú principal** «BACKROOMS: ESCAPA» (Jugar, Seleccionar nivel,
Multijugador, Opciones, Créditos, Salir) con un pasillo vivo de fondo; **pausa** con `Esc` (congela tiempo y audio, libera el cursor; `Esc` otra vez
continúa); **inventario** con `I`. `Esc` cierra primero los submenús (Opciones/Créditos/Niveles) y no interfiere con las pantallas de
derrota o victoria.

**Opciones** (se guardan en `PlayerPrefs`): volumen general, sensibilidad del ratón, pantalla completa, calidad (Bajo/Medio/Alto),
invertir eje Y, balanceo de cabeza (head-bob) on/off, suavizado del ratón (apagado por defecto), modo VHS on/off e intensidad del VHS,
**«Susto suave»** (reduce el susto de muerte: menos volumen, destellos, sacudida y glitch) y **«Mostrar cuerpo»** (cuerpo en primera persona, activado por defecto).

## Cómo funciona

| Script | Qué hace |
|---|---|
| `GameBootstrap` | Construye todo en runtime para el nivel actual: atmósfera (niebla exponencial, ambiente, viñeta), generador, NavMesh, jugador + cámara + linterna + pasos, monstruos, pilas, ambiente sonoro, HUD y `GameManager`. Carga/reinicia niveles con pantalla de carga. Inspector: nivel inicial (`startLevel`, o argumento `-level N`), semilla, monstruo sí/no, teclas de depuración (`N` salta de nivel). |
| `LevelConfig` | Datos de los 8 niveles (tamaño, estilo, texturas, luces, niebla, monstruos, batería, audio, puerta). Para añadir un nivel: sube `Count` y crea otro `LevelN()`. |
| `BackroomsGenerator` | Layout (laberinto con backtracker + bucles + salas, o rejilla abierta con pilares) y geometría por *chunks* con UV en metros del mundo (cada pared/suelo/techo tiene la escala correcta). Accesorios de luz emisivos, puerta de salida discreta (entre el 20 % de celdas más lejanas), pilas y apariciones de monstruos. |
| `ProceduralTextures` / `Noise` | Desde la v6.6 se cargan las texturas horneadas con Blender (`Assets/Resources/Textures`, ver «Cambios de la v6.6»); si faltan, texturas de 512–1024 px con Perlin/fBm periódico (albedo + normal + máscara metálico/oclusión/suavidad, mipmaps, filtro trilineal y anisotrópico). Sin costuras al repetir. |
| `LightCuller` | Solo las N luces de techo más cercanas al jugador son luces reales (con fundido); el resto de accesorios siguen siendo emisivos. |
| `ExitDoorCue` | LED verde diminuto y luz tenue de la puerta: solo existen a menos de 8 m. |
| `BatteryPickup` | Pilas recogibles que recargan la linterna. |
| `AmbienceAudio` / `FootstepAudio` / `ProceduralAudio` | Drone ambiental por nivel, gotas/crujidos/golpes metálicos 3D aleatorios y pasos según superficie (todo generado). |
| `FirstPersonController` | `CharacterController`: WASD + ratón, sprint con stamina, gravedad, cursor bloqueado, balanceo de cámara. Expone el "ruido" que oye el monstruo. |
| `Flashlight` | Spot Light con `F`; la batería se gasta y a poca carga parpadea y se corta; recarga lenta apagada. |
| `FlickeringLight` | Parpadeo aleatorio de luz + panel emisivo + volumen de un `AudioSource` de zumbido opcional. |
| `MonsterAI` | Estados Patrulla / Persecución / Búsqueda / Matar. Vista (distancia, ángulo, línea de visión; ve menos si apagas la linterna) y oído (correr hace más ruido). |
| `Screamer` | Susto de muerte dirigido por tiempo (embestida, corte a negro, fotograma de la cara, grito en capas, glitch VHS extremo, pantalla «SIGNAL LOST / HAS MUERTO»). |
| `PlayerBody` | Cuerpo hazmat en primera persona: animación Idle/Walk/Run/Duck sincronizada con las pisadas, IK del brazo derecho, linterna en la mano, empapado en el agua. |
| `WaterVolume` / `WaterFx` / `FloatingDebris` | Agua realista (shader `BackroomsWater`, ondas, espuma, reflejos, restos flotantes) y efectos de cámara (bajo el agua, gotas en la lente, cáusticas de la linterna). |
| `GameManager` | Flujo de niveles (siguiente nivel / victoria final / derrota → reinicio), tarjeta de título, HUD (`UnityEngine.UI`), reinicio con `R`. |
| `ProceduralAssets` | Materiales Standard (albedo + `_BumpMap` + `_MetallicGlossMap`/`_OcclusionMap`), agua transparente y primitivas (detecta URP). |

### NavMesh del monstruo

`MonsterAI` usa un `NavMeshAgent`. Hay tres niveles, de mejor a peor, y **ninguno rompe el juego**:

1. **NavMesh en runtime (por defecto):** con el paquete `com.unity.ai.navigation` en `manifest.json`,
   `GameBootstrap` añade un `NavMeshSurface` al nivel y lo hornea al generarlo (por reflexión, así el código
   compila aunque quites el paquete).
2. **NavMesh horneada a mano** (si montas tu propia escena con el generador): añade `NavMeshSurface`
   al objeto `BackroomsLevel` (o a un padre) y pulsa **Bake**. `MonsterAI` solo crea el `NavMeshAgent`
   si encuentra NavMesh bajo el monstruo.
3. **Modo alternativo sin NavMesh:** el monstruo sigue el camino por la cuadrícula del generador (BFS)
   moviéndose por los centros de celda. Se avisa con un `Debug.LogWarning`.

### Parámetros útiles

- **Semilla fija:** en `GameBootstrap`, desmarca `Use Random Seed` y pon `Seed` (cada nivel usa `Seed + 101·nivel`).
- **Empezar en otro nivel:** `Start Level` en el Inspector o `BackroomsEscape.exe -level 3`.
- **Niveles:** todo en `LevelConfig.cs` (tamaño, `loopChance`, estilo de texturas, luces, niebla, velocidad/número/sentidos de
  los monstruos, drenaje de linterna, nº de pilas...).
- **Rendimiento:** `maxRealtimeLights` por nivel (luces reales simultáneas, 8–10), `lightSpacing`, tamaño de rejilla.
  Las texturas se cachean (`TextureLibrary`): reiniciar un nivel no las regenera.

## Cambios de esta versión

- **Monstruos con modelos 3D animados (CC0), uno distinto por nivel:** El Alto (nivel 0), El Coloso (1), El Ahogado (2,
  Poolrooms), El Huésped (3) y El Hollín (4, sustituye al robot «El Operario»). Cada uno tiene su rig con Idle / Walk / Run / Attack (`Animator`), escala y pivote normalizados, ojos brillantes
  procedurales, gruñidos y pasos propios, y un **jump-scare** al atraparte (la cámara se clava en su cara, golpe de FOV, temblor e
  interferencia VHS). Si un modelo no carga, se usa una criatura procedural de reserva. Autores, fuentes y modificaciones: `CREDITS.md`
  y pantalla «Créditos» del juego (más una línea en el menú principal). Perfiles en `MonsterProfile.cs`; los prefabs se generan con
  `MonsterAssetBuilder.BuildAll` desde `Assets/Monsters/Models` (FBX convertidos con Blender) a `Assets/Resources/Monsters`.
- **Menú principal, pausa, opciones y créditos** (ver Controles) y **inventario** (`I`): pilas recogidas, % de batería de la linterna,
  nivel actual y botón para usar una pila. Las pilas recogidas van al componente `Inventory` (se conservan entre niveles).
- **Movimiento realista** (`FirstPersonController`): aceleración/frenado suaves, head-bob sincronizado con las pisadas (sonido por
  superficie), balanceo e inclinación lateral al hacer strafe, FOV kick al correr, respiración según la stamina, agacharse
  (v7.2.0: ya no hay inclinación con Q/E), caída con rebote, linterna con retardo/balanceo, más lento y con chapoteos en el nivel inundado. Todo sutil, con head-bob
  desactivable en Opciones.
- **Modo cámara VHS / found-footage** (tecla `V`, activado por defecto): post-efecto `OnRenderImage` + `Graphics.Blit` con un `Material`
  creado en runtime desde `Assets/Resources/VHS/VhsPost.shader` (scanlines, sangrado de color/aberración cromática horizontal, ruido de
  cinta, jitter, bandas de tracking, baja resolución, viñeta, tono cálido desaturado, ligera lente «ojo de pez»). Overlay con REC
  parpadeante, código de tiempo, «PLAY ►», icono de batería y fecha. La interferencia sube cuando un monstruo está cerca (más
  glitch/ruido, filtro paso-bajo y siseo de cinta). Si el shader no carga, se usa un overlay de UI de reserva. El shader está en
  `Resources`, en *Always Included Shaders* (`BuildScript`) y referenciado por `ShaderVariantKeeper` (material `VhsPostKeeper`) para que
  no se elimine del build.
- **Salida discreta:** eliminado el cartel 3D «SALIDA», la luz verde visible desde lejos y el texto «puerta verde de SALIDA».
- **Texturas más realistas:** 512–1024 px, generadas con Perlin/fBm periódico: papel pintado con rayas, manchas de humedad,
  suciedad y moho abajo, rodapié, papel despegado; moqueta con fibra y manchas; techo acústico con rejilla, vetas y puntitos;
  hormigón, azulejos, chapa oxidada, damasco de hotel... con normal map, suavidad y oclusión. UV por metros del mundo
  (sin estiramientos), mipmaps, trilineal y anisotrópico x8.
- **Atmósfera:** niebla exponencial de color por nivel, ambiente bajo, oscurecimiento de esquinas horneado en las texturas y en
  el mapa de oclusión, viñeta (overlay de UI), reflejos del cielo desactivados.
- **10 niveles** con progresión, tarjeta de título, pilas recogibles, audio ambiente y pasos por superficie.
- `R` reinicia el nivel actual; perder reinicia el nivel; ganar el último muestra la victoria y `R` vuelve al Nivel 0.
- Editor: `Backrooms > ...` (menú) y pruebas por línea de comandos (ver abajo).

## Cambios de la v4

- **Agua realista** (nivel 2, Poolrooms): shader propio `Resources/Water/BackroomsWater.shader` (3 olas Gerstner, normal maps móviles,
  fresnel, reflejos de una *reflection probe*, brillos de las luces del techo, tinte por profundidad, espuma en muros/pilares,
  ondas circulares), con *fallback* a Standard transparente. Vadeo con arrastre según profundidad, **nado y buceo** en aguas profundas
  (`Ctrl` sumerge; el nivel actual solo tiene agua poco profunda, la mecánica sigue implementada y probada), salpicaduras + ondas + sonido en cada pisada, **gotas en la lente** al salir, **bajo el agua** (niebla azul,
  burbujas, audio apagado), **cáusticas** animadas en la linterna (cookie), monstruo ralentizado que chapotea y **restos flotantes**
  con flotabilidad que reaccionan a ondas y empujones. *No verificado visualmente en pantalla real (solo por pruebas automáticas).*
- **Susto de muerte (screamer)** muy mejorado: embestida con *ease-in*, golpe de FOV, sacudida violenta, linterna que parpadea y
  se corta, **corte a negro**, regreso con un **fogonazo de foto de la cara a pantalla completa**, grito procedural en capas
  (ráfaga de ruido distorsionada + chillidos con barrido + golpe grave, con saturación y bitcrush) **distinto por monstruo**,
  acúfeno + latido, glitch VHS extremo (RGB split, bloques, barra rodante, congelados) y pantalla «SIGNAL LOST / HAS MUERTO» con
  estática, botones **Reintentar / Menú principal** y reinicio automático. Respeta el volumen general; «Susto suave» lo atenúa.
- **Cuerpo en primera persona**: traje hazmat amarillo (Quaternius «Character Hazmat», CC0, ver `CREDITS.md`), visible al mirar
  hacia abajo (torso, piernas, botas) y con el brazo derecho (IK) sujetando la linterna delante de la cámara; la cabeza solo
  proyecta sombra; capa propia `PlayerBody` (sin colliders, ignorada por la visión del monstruo y por la linterna). Anima
  Idle/Walk/Run/Duck con la cadencia de las pisadas; se moja en el agua. Opción «Mostrar cuerpo».
- **El Hollín** sustituye a «El Operario» (nivel industrial) con un modelo de horror CC0 más adecuado.
- CGTrader: casi todo lo de «backrooms» es de pago y los gratuitos exigen cuenta; no se ha usado nada (ver `CREDITS.md`).

## Cambios de la v5

- **Inventario nuevo** (`Inventory`, `InventoryUI`, `ItemDefs`, `ItemIcons`, `UiStyle`): rejilla de 5×3 casillas con **iconos generados por
  código** (linterna, pila, agua de almendras, barra luminosa, cinta VHS, notas), apilado, selección por clic / ratón / flechas, panel de detalle
  con nombre, cantidad y descripción, botones **Usar / Soltar**, tooltip al pasar el ratón, pestaña de **Notas** (18 notas de lore en
  español, 3 por nivel), medidores de linterna / resistencia / cordura y objetivo del nivel. Tema amarillento oscuro con fuente monoespaciada,
  animación de apertura y cierre (pausa el juego, `I`/`Esc` cierran, sin conflicto con pausa, derrota o menú). Lo recogido se conserva al
  pasar de nivel y se revierte al reintentar (`CommitForNextLevel` / `RevertToLevelStart`).
- **Barra rápida 1-5** en el HUD (`HotbarHud`) con iconos, cantidades y medidores BAT / RES / COR.
- **Objetos nuevos en los niveles** (`ItemPickup`, `BackroomsGenerator.ExtraSpots`): notas, agua de almendras (recupera resistencia y
  cordura), **barras luminosas** (`Glowstick`: se lanzan o se sueltan, iluminan una zona pequeña ~130 s, máx. 6 activas) y una cinta VHS.
  Nueva mecánica de **cordura** (`Sanity`): baja con el monstruo cerca, a oscuras o bajo el agua, y aumenta el glitch del VHS.
- **Linterna visible en la mano**: modelo procedural (cuerpo torneado, empuñadura de goma, aro, reflector, lente emisiva, interruptor) en la
  mano derecha del cuerpo hazmat (`PlayerBody`), con IK del antebrazo, retraída al acercarse a una pared (sin atravesarla), brillo de lente,
  origen del cono de luz en la lente, balanceo con retardo y animación de «clic» al encender/apagar (`Flashlight.Toggled`). Va en la capa
  `PlayerBody` (solo proyecta/recibe luz propia).
- Previsualizaciones: `/workspace/monster-previews/flashlight_hand*.png`, `inventory_preview*.png`, `hud_hotbar.png` (render real con
  `InventoryPreview` / `FlashlightPreview` bajo xvfb + `-force-glcore`).

## Cambios de la v6: multijugador cooperativo (2-4 jugadores)

El modo de un jugador **no cambia** (la red solo se activa desde «Multijugador»).

### Cómo jugar con amigos (paso a paso)

1. **Todos** tenéis que usar la misma versión (la v6; el juego comprueba el protocolo `BackroomsEscape-MP-1` y rechaza versiones distintas).
2. **Anfitrión:** menú principal → **Multijugador** → escribe tu nombre (se guarda) → **Alojar partida** → deja el puerto **7777** (o cambia
   el número) → **Crear sala**. Windows pedirá permitir el juego en el **Firewall**: acepta. Desde la v6.1 el juego intenta **abrir el puerto
   solo** en tu router (UPnP / NAT-PMP) y te enseña qué IP pasar a tus amigos (ver «v6.1» más abajo).
3. Pasa a tus amigos la dirección que enseña la pantalla (botón **Copiar dirección** → `ip:puerto`):
   - **Misma red local (LAN / mismo wifi):** la IP de tu red local (suele empezar por `192.168.` o `10.`).
   - **Por Internet:** tu **IP pública** + puerto (el juego la detecta y la copia). Si el aviso dice «Puerto abierto automáticamente», ya está;
     si no, redirige **TCP 7777** en tu router hacia tu PC, o…
   - **Sin tocar el router: una VPN** (**Radmin VPN**, **ZeroTier**, **Hamachi**) y pasas la IP de la VPN; o el modo **con código** (relay, v6.1).
4. **Cliente:** Multijugador → nombre → **Unirse** → pega `ip:puerto` (o IP y puerto por separado) → **Conectar**. Entrarás en la sala de espera.
5. El anfitrión elige el nivel con ◄ ► y pulsa **Empezar** (los demás esperan). No se puede entrar a una partida ya empezada (se avisa).
6. Dentro: `Enter` abre el chat; `Esc` abre la pausa pero **el juego sigue** (pone «Pausa (la partida sigue)»); el inventario tampoco pausa.

### Qué se sincroniza

- **Mundo determinista:** el anfitrión elige nivel + semilla (+ monstruos extra) y cada cliente **genera el mismo mundo localmente** (nada de geometría
  por red). Cada cliente envía una huella (`BackroomsGenerator.WorldHash()`: posiciones de salida/inicio/monstruos/pilas/objetos + geometría) y el
  anfitrión avisa si no coincide. El generador usa solo `System.Random` con semilla, no `UnityEngine.Random`.
- **Jugadores remotos:** cuerpo hazmat amarillo con animaciones (idle/andar/correr/agacharse), linterna con luz, etiqueta con el nombre, estado
  derribado; posición interpolada (20 Hz, con ~0,1 s de retardo de interpolación). Pasos y chapoteos con sonido 3D.
- **Monstruos:** la IA solo corre en el **anfitrión**; los clientes ven «proxies» interpolados (posición, rotación, estado y velocidad para animar).
  El monstruo persigue al jugador más cercano / visible (con histéresis para no cambiar a cada rato) y oye/ve a los remotos (linterna, ruido).
- **Derribado y reanimación:** si te atrapan, caes («derribado», cámara en el suelo, no puedes moverte); un compañero que **mantenga E** a menos de
  ~2 m durante ~4 s te levanta (aviso «Mantén E para reanimar» con anillo de progreso; el que reanima se arrodilla y alarga la mano, los demás lo
  ven agachado sobre ti y tú te incorporas). Soltar E, moverse o alejarse lo cancela; lo decide el anfitrión. Si caen **todos**, screamer para todos y se reinicia el nivel.
- **Salida:** el nivel termina cuando **todos los jugadores vivos** están en la puerta («Jugadores en la salida: X/Y»); entonces carga el
  siguiente nivel para todos.
- **Objetos:** el primero en llegar se lleva la pila / objeto (se retira para todos); las notas se comparten; cada jugador tiene su inventario.
  Lo que sueltas aparece para todos. Las barras luminosas lanzadas se ven en todos los clientes.
- **Escalado:** 1-2 jugadores = igual que en solitario; con 3-4 jugadores **+1 monstruo** desde el nivel 3 (`NetSession.ExtraMonstersFor`).
- **Local (no se sincroniza):** cordura, efecto VHS, linterna de mano propia, opciones, el inventario.
- **Desconexiones:** si el anfitrión se va, los clientes vuelven al menú principal con un aviso; si un cliente se va, desaparece su avatar.
  Mensajes de error en español: IP no válida, conexión rechazada, tiempo de espera agotado, red inaccesible, versión incompatible, sala llena,
  partida ya empezada, anfitrión desconectado, puerto ocupado.

### Por qué una capa propia sobre TCP (y no Netcode for GameObjects)

El juego **construye todo por código en tiempo de ejecución** (sin prefabs ni escenas con `NetworkObject`), que es el caso más incómodo para
Netcode for GameObjects (registro de prefabs, spawn de `NetworkObject`, `NetworkManager` en escena…) y añade dependencias de paquetes en un
proyecto que, hasta ahora, solo necesitaba `com.unity.ai.navigation`. Como el mundo es determinista por semilla, solo hay que mandar estados
pequeños (jugadores, monstruos, eventos). Por eso se implementó una capa mínima y fiable sobre `System.Net.Sockets` (`Assets/Scripts/Net/`):
**TCP** con mensajes con prefijo de longitud, sockets no bloqueantes y conexión en un hilo aparte (la ventana no se congela). TCP garantiza
entrega y orden (eventos críticos como recoger objetos o derribar jugadores no se pierden) y es lo más sencillo de abrir en un router.
Contras: más latencia que UDP si hay pérdida de paquetes (con 20 Hz de estados y interpolación es aceptable para 2-4 jugadores). No requiere
ningún paquete extra, así que **la build de Windows (Mono) incluye el modo multijugador sin cambios** en `Packages/manifest.json`.

Arquitectura: `NetSession` (sesión anfitrión/cliente, sala, bucle, snapshot a 20 Hz, reglas autoritativas), `NetTransport` (`NetConn`,
`NetListener`, `NetConnector`, serialización), `RemotePlayer` (avatar remoto), `NetDownedFx` (efecto local de derribado), `MultiplayerUI`
(menú, sala, chat, HUD de equipo). Ver los comentarios del código para el protocolo (`NetMsg`).

### Pruebas multijugador

- `SmokeTest.RunEditMode`: serialización, transporte TCP real por bucle local (ráfagas, mensajes de 150 KB, corte de conexión, puerto ocupado,
  errores de conexión), y **determinismo del hash del mundo por nivel** (misma semilla = mismo hash, otra semilla = otro, +1 monstruo extra, independiente de `UnityEngine.Random`).
- `SmokeTest.RunPlayMode`: menús multijugador, sala con 4 jugadores (clientes TCP crudos), «versión incompatible», «sala llena», salida de la sala.
- **Prueba automática de dos procesos** (`/workspace/mp-test.sh` + `Assets/Editor/MpTest.cs`): lanza dos Unity en batchmode (anfitrión + cliente,
  una copia del proyecto para el cliente) por localhost y comprueba: errores de conexión, sala, misma semilla y huella del mundo, avatares remotos,
  replicación de posición, monstruos coincidentes, retirada sincronizada de pilas, chat, barra luminosa, objeto soltado, pausa/inventario sin
  congelar, derribado/reanimación, todos derribados → screamer + reinicio, salida → siguiente nivel, desconexión de un cliente, rechazo a mitad
  de partida, y que el anfitrión pueda volver a abrir sala.
- Previsualizaciones: `/workspace/monster-previews/lobby_preview.png`, `mp_*.png` (con `LobbyPreview`).

### Limitaciones conocidas (multijugador)

- **No se ha probado por Internet real** (solo bucle local y relay local), ni con latencia/pérdida reales, ni con 3-4 jugadores reales (la prueba usa 2 procesos; 3-4 se
  simulan con clientes crudos en la sala).
- El `.exe` de Windows no se puede ejecutar en este entorno: se comprobó que se compila con Mono e incluye `Assembly-CSharp.dll` con la capa de red.
- Sin reconexión a mitad de partida, sin voz, sin migración de anfitrión (si el anfitrión cae, se acaba la partida).
- El anfitrión es autoritativo para monstruos, pilas, derribados y salida; los clientes son de confianza en su propia posición (no hay anti-trampas).
- Los clientes ven a los monstruos con ~0,1 s de retraso; el anfitrión los ve «en directo».
- Solo el anfitrión puede reiniciar nivel / pasar de nivel / volver a la sala; el menú «Reiniciar nivel» de un cliente avisa de ello.

## Cambios de la v6.1: multijugador por Internet (UPnP, IP pública, relay con código)

El multijugador de la v6 era «IP + puerto» a mano. La v6.1 añade, **sin cuentas ni servicios externos obligatorios**:

### 1) Apertura automática del puerto (UPnP / NAT-PMP)
- Al **Crear sala**, un hilo de fondo (la interfaz no se congela) busca el router por **SSDP** (multicast UDP 1900), lee su descripción
  (`WANIPConnection` / `WANPPPConnection`), y envía por **SOAP/HTTP `AddPortMapping`** para el **TCP** elegido (concesión permanente; si el router
  responde 725 reintenta con 2 h renovables; si hay conflicto 718 borra la entrada anterior y repite). Si UPnP no responde prueba **NAT-PMP** (UDP 5351).
  Todo con `System.Net`, con tiempos máximos y fallo limpio. Al **salir de la sala o cerrar el juego** se hace `DeletePortMapping` (el puerto no queda abierto).
- La pantalla del anfitrión (y la sala) muestra: IP locales con puerto, **IP pública** (consulta `https://api.ipify.org`, con respaldo `http://` y `icanhazip.com`,
  4 s de máximo; sin Internet pone «no disponible»), y el estado en español: **«Puerto abierto automáticamente (UPnP, TCP 7777)»** o
  **«No se pudo abrir el puerto automáticamente: abre TCP 7777 en tu router o usa una VPN»**.
- **Aviso CGNAT:** si la IP WAN que dice el router es privada / del rango 100.64.0.0/10 o no coincide con tu IP pública, aparece
  «tu operador usa CGNAT … necesitas una VPN o un servidor intermedio (modo con código)». (No hay autoprueba de alcanzabilidad desde fuera: sin un
  servidor externo que conecte de vuelta no se puede garantizar, y muchos routers no permiten probar la propia IP pública desde dentro.)
- **Copiar dirección:** copia al portapapeles `ippública:puerto` (o la IP local si no hay pública; el código de sala en modo relay).
- **Unirse:** el campo de IP acepta `ip:puerto` pegado (también `tcp://ip:puerto`); se separa solo en IP y puerto.

### 2) Transporte intercambiable y relay con código de sala
- `ITransport` (`Assets/Scripts/Net/Transports.cs`): `DirectTcpTransport` (el de siempre) y `RelayTransport`. `NetSession` solo ve sockets TCP ya conectados
  (`INetListener.Accept` / `INetConnector.Result`), así que el protocolo del juego es idéntico por ambos caminos.
- El **relay** es un servidor Node.js sin dependencias (`/workspace/backrooms-relay/`: `server.js`, `package.json`, `Dockerfile`, `fly.toml`, `README.md`, `test.js`).
  Anfitrión y clientes salen *hacia fuera* hacia el relay, que los empareja por un **código de 6 caracteres** (sin I, O, 0, 1) y reenvía los bytes. Así no hace
  falta abrir puertos, ni UPnP, y funciona incluso detrás de CGNAT. Protocolo y despliegue: ver el README del relay (VPS / Oracle Always Free / Fly.io / Railway; **necesita TCP crudo**).
- **Configurar el relay (vacío = desactivado):** (1) campo **«Servidor relay»** del menú Multijugador (se guarda en `PlayerPrefs`), (2) archivo **`relay.txt`** junto a
  `BackroomsEscape.exe` con `mi-relay.ejemplo.com:7780`, (3) variable de entorno `BACKROOMS_RELAY` (pruebas). Con relay válido se activan
  **Alojar con código** (la sala enseña el código y «Copiar dirección» lo copia) y **Unirse con código**. Sin relay esos botones están desactivados y el menú lo explica.
- Errores en español: relay inalcanzable, código inexistente / mal formado, sala llena, el anfitrión no respondió, relay lleno, protocolo distinto; si el relay
  cae con la sala abierta, los jugadores ya conectados siguen (cada uno tiene su propia conexión) pero el código deja de servir para nuevos.
- **Cómo desplegar el relay (resumen):** en una VM con IP pública: `node server.js` (o `docker run -d -p 7780:7780 backrooms-relay`) y abrir **TCP 7780**;
  luego en el juego `relay.txt` ← `IP-o-dominio:7780`. Render gratis **no sirve** (solo HTTP). Detalles y alternativas en `backrooms-relay/README.md`.

### Pruebas de la v6.1
- `SmokeTest.RunEditMode`: análisis de `ip:puerto`, códigos de sala, rangos privados/CGNAT, textos de la pantalla del anfitrión, descripción UPnP y mensajes SOAP,
  **router UPnP simulado** (SSDP + descripción + SOAP: AddPortMapping TCP, 725→lease 2 h, 718→borrar y reintentar, WANPPPConnection, descripción rota,
  eliminación al parar), sin router (fallo limpio y texto exacto), **NAT-PMP simulado** (mapeo y borrado), IP pública contra un eco HTTP simulado (ok / basura / sin conexión),
  configuración `relay.txt` / PlayerPrefs, y el **cliente del relay contra el `server.js` real** (código, 2 clientes, mensajes del juego, 120 KB, salida de un cliente, errores, relay caído).
- `SmokeTest.RunPlayMode`: botones «con código» desactivados sin relay, pegar `ip:puerto`, relay local: alojar con código → cliente real entra → salir; unirse con código (inexistente / mal escrito); relay caído al alojar.
- `backrooms-relay/test.js` (`npm test`): anfitrión + 2 clientes por el relay, 300 KB en cada sentido sin pérdidas, orden, códigos erróneos, sala llena, tiempos de espera, salud HTTP, límite por IP.
- `/workspace/mp-test.sh` (directo) y `/workspace/mp-test.sh relay` (dos procesos de Unity, anfitrión y cliente, **a través del relay local** con código de sala): mismas comprobaciones de la v6.

### Qué NO se ha podido verificar
Routers reales (UPnP/NAT-PMP solo contra simuladores; en este entorno no hay router IGD), CGNAT real, alcanzabilidad desde Internet, latencia por relay real
y el `.exe` de Windows en ejecución. La detección de IP pública sí se comprobó contra `api.ipify.org` en el entorno de desarrollo.

## Cambios de la v6.2: relay oficial integrado (cifrado) + token

El juego puede llevar integrado el relay «oficial» para que «Alojar con código» / «Unirse con código» funcionen sin configurar nada.

- **Prioridad de configuración:** variable de entorno `BACKROOMS_RELAY` > campo «Servidor relay» del menú (`PlayerPrefs`) > `relay.txt` junto al .exe > **relay integrado**.
  Si no escribes nada, se usa el integrado; si escribes otra dirección, se usa la tuya (y **no** se le envía el token integrado).
- **La interfaz nunca muestra la dirección integrada:** el menú dice «Servidor oficial (integrado)», el campo queda vacío y los logs/errores no la imprimen.
- **Cómo está integrada:** `Assets/Scripts/Net/RelayDefault.cs` descifra en tiempo de ejecución una carga **AES-256-CBC + HMAC-SHA256** (encrypt-then-MAC, `System.Security.Cryptography`).
  La clave se deriva (SHA-256 + HMAC) de tres piezas: constantes dispersas (A), un texto troceado y reordenado (B) y una pieza aleatoria por compilación (C) que vive,
  enmascarada y troceada, en el archivo **generado** `RelayDefault.Generated.cs`. Ese archivo (texto cifrado + pieza C) **no está en el repositorio** (`.gitignore`):
  el código público compila con `HasBuiltin = false` y el juego usa solo el relay del usuario / `relay.txt`.
- **Generarlo (compilación de release):** `RELAY_ADDR=host:puerto RELAY_TOKEN=secreto Tools/encrypt_relay.sh` (usa `RelayBlobTool`, un método de editor; también `Backrooms → Relay → Quitar relay integrado`),
  después compilar. Sin `RELAY_ADDR` el script borra el archivo generado. Para el paquete público: borra `RelayDefault.Generated.cs(.meta)` y compila.
- **Esto es OFUSCACIÓN, no secreto real.** El cliente necesita conocer la dirección para conectar: quien desensamble el juego y ejecute o replique `RelayDefault` puede obtenerla (y el token).
  Lo que se consigue es que no aparezca en claro con `strings`/`grep` (ASCII ni UTF-16) en el `.exe`, los `.dll`, los recursos, el zip o el repositorio público, ni en la interfaz o los logs.
  La protección real contra desconocidos es el **token del relay** (más los límites del servidor); si el token se filtra, cámbialo en el servidor y publica una versión nueva.
- **Token compartido:** el servidor (`backrooms-relay/server.js`) acepta `RELAY_TOKEN`; con él puesto, `HOST`/`JOIN`/`ACCEPT` sin `T=<token>` reciben `ERR auth`. Sin la variable,
  el servidor ignora el token (compatible con versiones anteriores). Pasos para actualizar un relay en Windows Server: `backrooms-relay/README.md`.
- **Pruebas:** edit-mode (cifrado ida y vuelta, manipulación/clave incorrecta/IV, precedencia, el token integrado no se manda a relays ajenos, relay Node con token real, ningún log contiene dirección ni token),
  play-mode (el menú con relay integrado no muestra su dirección), `mp-test.sh relay-token` (dos procesos con el relay protegido por token) y `test.js` del relay (token correcto/ausente/erróneo, ACCEPT, límite de fallos, compatibilidad sin token).

## Pruebas y herramientas de editor

Todo se ejecuta en batchmode (sin ventana):

```
Unity -batchmode -nographics -projectPath <proyecto> -executeMethod SmokeTest.RunEditMode  -logFile edit.log   # genera los 8 niveles x 3 semillas y valida
Unity -batchmode -nographics -projectPath <proyecto> -executeMethod SmokeTest.RunPlayMode  -logFile play.log   # entra en Play y recorre los 8 niveles
Unity -batchmode -nographics -quit -projectPath <proyecto> -executeMethod TexturePreviewExporter.Export          # PNG de texturas + hoja de contacto
Unity -batchmode -nographics -quit -projectPath <proyecto> -executeMethod BuildScript.BuildWindows               # build Windows x64
```

`SmokeTest.RunEditMode` comprueba: sin excepciones, materiales/shaders válidos (albedo ≥ 512 px + normal + máscara), puerta de salida
presente y sin cartel/TextMesh, BFS inicio → salida/pilas/monstruos, camino libre con `SphereCast`, NavMesh conectado, orientación de
triángulos. `RunPlayMode` comprueba además HUD sin pistas, luces reales ≤ N, recoger pila, derrota → reinicio, puerta → siguiente nivel,
victoria final y reinicio total, y además: menú principal / opciones / créditos / pausa (Esc) / inventario (I, usar pila) / sin
conflictos con derrota y victoria, modelos de monstruo reales (sin fallback) por nivel, animaciones, movimiento (aceleración,
head-bob on/off, correr, agacharse, linterna con retardo), VHS (material no nulo, ON/OFF con `V`, overlay REC/timecode,
glitch según cercanía del monstruo) jump-scare / screamer (fases, corte a negro, foto, grito, pantalla final con botones), cuerpo del jugador (Animator, capas, IK, estados Idle/Walk/Run/Duck, opción), agua (shader propio, vadeo, aguas profundas, bajo el agua, burbujas, gotas, cáusticas). `RunEditMode` valida además prefabs/clips/avatares/créditos de los monstruos,
el shader VHS (carga, compila, material, `VhsPostKeeper`) y la persistencia de opciones. Los informes se escriben en `/workspace/logs/smoke-*.txt`.
`Assets/Resources/ShaderVariantKeeper` contiene materiales que fuerzan a incluir en el build las variantes de Standard usadas en runtime
(`_NORMALMAP`, `_METALLICGLOSSMAP`, `_EMISSION`, transparente); el build también conserva las variantes de niebla.

## Notas y limitaciones conocidas

- Compila y pasa las pruebas de humo en Unity 2022.3.62f3 (batchmode, sin GPU). **No se ha podido revisar el aspecto final en pantalla**
  (iluminación, niebla, brillos, agua); si algo se ve raro, ajusta los valores en `LevelConfig.cs`.
- Los materiales usan el shader `Standard`. En una *build* de PC, si salen rosas, añade `Standard` a
  `Project Settings > Graphics > Always Included Shaders`.
- El audio es generado por código (sin música). Los modelos de monstruo son de baja/media resolución (CC0) y no se ha podido
  comprobar su aspecto con GPU real (solo renders de previsualización en `/workspace/monster-previews`).
- `Main.unity` está escrito a mano en YAML; si Unity se quejara, usa el montaje manual de arriba.

## Hoja de ruta

1. **Audio:** música dinámica según el estado del monstruo, gruñidos espaciales, sonido de la linterna y de la puerta.
2. **Modelos:** sustituir primitivas por modelos modulares (paredes, columnas, puertas); decals de manchas.
3. **Más niveles y variedad:** habitaciones especiales (llaves, interruptores, ascensores), varias salidas, más "niveles" de las Backrooms.
4. **Más entidades:** entidades que reaccionan a la luz o al sonido, "Smilers" en la oscuridad, patrullas coordinadas.
5. **Objetos y progresión:** llaves, botiquín (si se añade vida), checkpoints.
6. **Pulido:** menú principal/pausa, ajustes (sensibilidad, volumen, FOV), post-proceso real (grano, aberración; requiere URP/Post Processing),
   guardado de mejor tiempo y semilla diaria.
7. **Rendimiento:** combinar mallas estáticas, luces baked/Light Probes, oclusión por celdas.

## Cambios de la v6.3

- **Sonido «de aire» (respiración):** el culpable era la respiración en bucle continuo (ruido filtrado casi siempre audible, más fuerte al
  perder resistencia) apoyada por el siseo de cinta VHS y los drones de ruido de algunos niveles. Ahora la respiración son *one-shots*
  de inhalar/exhalar con envolvente suave, solo tras correr o con poca resistencia, con límite de ritmo y a ≤ -35 dBFS; hay una tabla de
  mezcla única (`AudioMix`) y nuevos deslizadores en Opciones: **Volumen general**, **Efectos**, **Ambiente** y el interruptor **Respiración**.
  El volumen general escala toda la mezcla (`AudioListener.volume`). Los drones del hotel y del complejo industrial se bajaron unos dB.
- **Niveles: ahora son 5.** Se eliminó el nivel «Poolrooms» basado en el modelo propio (`PoolHall.fbx`, junto con su código de sala grande/foso/escaleras
  en el generador) y el nivel de agua «Salas inundadas» pasa a llamarse **Poolrooms** (nivel 2, con «El Ahogado»; «El Reptador» ya no se usa y sus
  assets se han quitado del proyecto). Orden: 0 Oficinas · 1 Aparcamiento · 2 Poolrooms · 3 Hotel · 4 Industrial. Notas: 15 (3 por nivel).

## Cambios de la v6.4: entrada en ascensor en cada nivel

- **Cada nivel empieza dentro de un ascensor** (obligatorio, sin opción para desactivarlo, también en multijugador): la cabina baja a toda
  velocidad durante unos 8 s (sacudidas de cámara, retumbo, motor/cables y soplo de aire procedurales, contador de planta que cae
  rápido con flecha y etiqueta «BAJANDO», fluorescente parpadeante, frenazo con sacudida, «ding») y las puertas se abren dejando ver el nivel.
  El jugador está bloqueado durante el descenso y se libera cuando las puertas están a medio abrir (~7,4 s). La tarjeta «NIVEL N — …»
  aparece al abrirse las puertas. Los monstruos esperan quietos hasta ese momento. Al alejarse, las puertas se cierran y la cabina se
  queda atrás con colisión (no se puede volver a entrar ni atravesar).
- **Saltar:** solo al **reiniciar el nivel tras morir** se puede saltar la animación con `Espacio` / `E` (se muestra la indicación «Saltar»).
  En la primera entrada y al pasar de nivel siempre se reproduce completa.
- **Modelo:** la carcasa (suelo, paredes, pasamanos, botones, techo) y las dos hojas de puerta con su dintel son CC0 (3D Assets,
  «Liminal Spaces Kit»; ver `CREDITS.md` y los créditos del juego). Se unieron y convirtieron a FBX con Blender (hojas `Door_L`/`Door_R`
  separadas, texturas reducidas a 512 px, ~0,6 MB). El indicador de planta, las luces, los botones iluminados, el sonido y la animación
  de puertas son procedurales. Cada nivel tiñe el indicador y la luz (el complejo industrial usa luz roja de emergencia).
- **Semilla / multijugador:** la cabina se coloca en la celda de inicio, mirando a su salida abierta, **después** de generar el laberinto
  (no cambia el hash del mundo). El anfitrión envía `Descend` (protocolo `MP-2`) cuando todos han generado el mundo, y todos arrancan
  el descenso a la vez dentro de la misma cabina; los monstruos del anfitrión se liberan al abrirse sus puertas.
- **Pruebas:** `SmokeTest` (cabina en los 5 niveles, línea de tiempo, bloqueo/liberación, monstruos retenidos, saltar solo tras reinicio,
  colisión, salida alcanzable desde la puerta, audio dentro de la mezcla) y `mp-test.sh` (descenso sincronizado entre anfitrión y
  cliente, avatar remoto dentro de la cabina). Vistas previas: `/workspace/monster-previews/elevator_*.png`.

## Cambios de la v6.5: auditoría de audio («soplo de aire») + intro de arranque

### Auditoría de audio: qué sonaba a «aire»
Se enumeraron TODAS las fuentes de audio (`AudioCensus`: menú, 5 niveles, reinicios, vuelta al menú; volcado en `/workspace/logs/audio-census-*.txt`)
y se volcó cada clip a WAV (`AudioDump`) para analizarlo con numpy/scipy (RMS, planitud espectral, bandas, modulación de amplitud).
- **Culpable principal: el ruido grave del drone ambiental** (niveles 1-4; `droneNoise` 0,28-0,35): 20-25 % de la energía del drone era ruido
  filtrado (150-1500 Hz, ≈ -37 dBFS efectivo) con una modulación lenta de 0,25 Hz → un «soplo» continuo que sube y baja. Es el único bucle
  de ruido que suena SIEMPRE. Ahora el drone es solo tonal (parciales de ciclos enteros + parciales desafinados que laten despacio).
- **Siseo de cinta VHS** (ruido de banda media): en reposo era inaudible (-62 dBFS) pero con un monstruo cerca llegaba a -43 dBFS; ahora el
  reposo es un zumbido tonal de 50 Hz (-57 dBFS) y el siseo de ruido solo aparece con las interferencias y a ≤ -48 dBFS; se apaga con la
  nueva opción **«Siseo VHS / ruido»** (Opciones; activada por defecto).
- **Ascensor (v6.4):** el «aire» y el retumbo eran ruido filtrado (retumbo -24 dBFS, 43 % de su energía bajo 60 Hz con modulación a 3 Hz);
  ahora son tonales (planitud espectral 0,000).
- **Zumbidos de fluorescentes:** descartado como culpable (son tonales, 100 Hz + armónicos, planitud 0,000) pero había decenas de bucles 3D
  sonando a la vez (hasta 16 por nivel, todos fuera de alcance). `AudioBuzzLimiter` deja sonar solo los 3 más cercanos (a < 6 m) con fundido.
- **Sin acumulación:** el censo muestra 1 drone y 1 siseo/zumbido de cinta en menú, niveles y tras reiniciar; el número de fuentes no crece.
- Pruebas nuevas: planitud espectral de todos los bucles (un bucle con planitud > 0,02 no puede superar -45 dBFS efectivos), un solo
  drone / siseo, ≤ 3 zumbidos y ≤ 7 bucles sonando, número de fuentes estable tras reiniciar y en el menú, zumbido junto a una luz,
  interruptor «Siseo VHS / ruido».

### Intro de arranque
- Al abrir el juego, antes del menú, se reproduce una intro de ~9,4 s estilo found footage (todo procedural): estática VHS, inserción de
  cinta con glitch, «● REC» y código de tiempo, aviso («ESTE JUEGO CONTIENE LUCES PARPADEANTES, SUSTOS Y SONIDOS FUERTES»), «PIXEL
  presenta», título «BACKROOMS: ESCAPA» con parpadeo y zumbido grave, y fundido hacia el menú con el pasillo en vivo.
- Se puede saltar con cualquier tecla o clic pasados 2 s. Argumento `-nointro` para desactivarla.
- Fotosensibilidad: sin destellos de pantalla completa, estática suave, parpadeos ≤ 3 por segundo; con «Susto suave» se reducen aún más
  (sin desplazamiento de color, menos estática, menos volumen).
- **Pantalla de Unity:** el build pide `PlayerSettings.SplashScreen.show = false`. Con la licencia **Personal** de Unity 2022.3 el logo
  de Unity puede quedar forzado por la licencia (no se elude); ver el informe de la compilación.
- Vistas previas: `/workspace/monster-previews/intro_*.png`.

## Cambios de la v6.6: susto de muerte visible + texturas horneadas con Blender

### Susto (screamer): por qué no se veía la entidad
- La cámara se lanzaba hacia un punto fijo (`ai_eyeHeight`, ~0,95 de la altura de los ojos de la IA) del monstruo, con la linterna apagada
  durante el corte a negro y sin luz propia: la cara quedaba **a oscuras**, a veces **tapada por paredes/cabina o por la niebla**, el
  punto no coincidía con la cabeza real (modelos encorvados, animación de ataque que gira el torso) y encima el **glitch VHS extremo**
  (barra rodante invertida, bloques de estática, ruido pesado) tapaba lo poco que se veía.
- Ahora (`Screamer.cs`): tras la embestida y el corte a negro, la cámara principal pasa a dibujar **solo la capa 29** donde se mueve el
  monstruo (sin muros, cabina, niebla ni sombras), fondo oscuro rojizo, plano cercano 3 cm, **luz principal fría + luz de contorno roja**
  propias (las luces de la escena no iluminan esa capa) y la cabeza se localiza por los ojos/hueso/mallas (con ajuste por perfil:
  `screamUp`, `screamHeadTop`). El monstruo se gira hacia el jugador y mantiene una pose estable (Idle).
- Tres planos con cortes secos: **A)** cara y hombros a 0,9–1,4 m (según altura), **B)** primerísimo plano de ojos, **C)** plano abierto con
  embestida hacia la cámara; ~2,0 s de cara + 0,5 s de fundido, luego «HAS MUERTO». Pulso de escala, giro de cabeza, balanceo y sacudida.
- El glitch VHS del plano de la cara se reduce y los bloques/barra/ruido pesado crecen con S² en el shader, para que la cara se lea. «Susto
  suave» sigue reduciendo luces, destellos y glitch.
- Multijugador: el susto solo se lanza para todos cuando caen todos (`AllDown`); la cámara se coloca respecto al monstruo (no al jugador),
  así que todos ven al monstruo que atrapó al último.
- Vistas previas (`ScreamerPreview.Export`, 3 planos × 5 monstruos): imagen «Susto de muerte» de la web (`docs/assets/img/susto-monstruos.jpg`).
- Zombis (El Huésped y El Ahogado): su clip Idle está en pose T, así que el susto usa el clip Walk (`screamAnim`). `screamDist` y `screamLight` por monstruo ajustan distancia y luz (El Coloso: ×1,6 y luz ×3,2).

### Texturas realistas (Blender)
- `Tools/blender/`: `texlib.py` (mini-DSL de nodos: ruido 4D sobre un toro → **sin costuras**, Voronoi, rampas de color, máscaras),
  `styles.py` (14 materiales: papel amarillo con rayas/manchas/moho/despegado/rodapié, damasco de hotel con zócalo de madera, moquetas,
  placas de techo, hormigón de pared/suelo/techo, azulejos de piscina, chapa pintada oxidada con remaches, chapa lagrimada),
  `bake.py` (Cycles CPU, 1 muestra, emisión) y `pack_textures.py` (normal desde altura, máscara Standard, JPG/PNG).
  `blender -b -P Tools/blender/bake.py -- <carpeta>` y luego `python3 Tools/blender/pack_textures.py <carpeta> Assets/Resources/Textures`.
- Albedo 1024 px (2048 en pared y moqueta del nivel 0), normal y máscara a 1024 px. Mipmaps, anisotrópico x8, repetición, no legibles,
  compresión con crunch (`TextureImportRules`). UV siguen en metros. Si falta alguna textura (o la altura de pared no coincide) se usa
  la generación por CPU anterior. Son cosméticas: no tocan la semilla ni el hash del mundo (multijugador intacto).
- Vistas previas antes/después (`TextureV66Preview.Export`): `docs/assets/img/texturas-blender-antes-despues.jpg`
  (`TextureV66Preview.Export`).

## v7.7.0 — Nivel 114 «El Desierto Cálido»
- Nuevo **nivel 8 = Nivel 114 de la wiki** (de Sonicelmaspro2, ampliado por Omaigad12): desierto cálido sin fin (10-17 °C), seguro (sin entidades), con día y noche, clima cada «6 horas» (despejado, nublado, lluvia de agua de almendras, lluvia de «dolor líquido», tormenta de arena, nieve muy rara y el rarísimo «Clima Hermoso» con auroras y arena de colores), pájaros de sangre amarilla solo de día, aldeas en ruinas (casas de adobe y piedra, muebles rotos, pozos, una torre), cuevas con vetas de hierro, oro, plomo y diamante, arenas movedizas que te llevan a otra zona y, muy rara vez, el Vacío.
- La salida es una **puerta de piedra** en lugar de una puerta de madera en una casa de un pueblo. Objetos: agua de almendras, Raciones Reales (caducadas), agua de anacardo y la rarísima agua de neón.
- Código: `Assets/Scripts/Desert/` (DesertPlan, DesertHeight, DesertWorld, DesertMesh, DesertSky, DesertFx, DesertItems), shaders `Resources/Desert/Shaders`. Pruebas: `V770Checks`, `SeedReachCheck` (nivel 8). Multijugador: protocolo MP-10 (reloj del desierto del anfitrión).
