# BACKROOMS: ESCAPA

Juego de terror en primera persona para PC (Windows 64 bits) hecho con **Unity**: escapa de las Backrooms a través de **5 niveles** encadenados. Cada nivel es un laberinto (o aparcamiento abierto) procedural con su propia estética, luces, dificultad y criaturas que patrullan y te persiguen. La salida es una puerta discreta que **solo se descubre explorando**: sin cartel, sin flecha, sin indicador de distancia.

- 🌐 **Web:** https://rootesq-pixel.github.io/backrooms-escapa/
- ⬇️ **Descarga directa v6.7 (Windows, ZIP ≈ 60 MB):** [BackroomsEscape_v6_7_Windows.zip](https://github.com/rootesq-pixel/backrooms-escapa/releases/download/v6.7.0/BackroomsEscape_v6_7_Windows.zip)
- 📦 **Release v6.7.0:** https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v6.7.0
- 🕓 Versiones anteriores: [v6.5.0](https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v6.5.0) (audio e intro) · [v6.4.0](https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v6.4.0) (ascensor) · [v6.3.0](https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v6.3.0) (5 niveles, sin ascensor) · [v6.2.0](https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v6.2.0) (multijugador por Internet, 6 niveles) · [v6.0.0](https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v6.0.0) (multijugador por IP y puerto) · [v5.0.0](https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v5.0.0) (un jugador)

> ⚠️ **Build temprana.** Puede haber errores. El **multijugador** se ha probado en local y con un servidor intermedio real; **las pruebas con amigos por Internet están pendientes**. El ejecutable **no está firmado**, así que Windows SmartScreen puede avisar al abrirlo (*Más información → Ejecutar de todas formas*).

## Cómo jugar

1. Descarga `BackroomsEscape_v6_7_Windows.zip` desde la [release v6.7.0](https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v6.7.0).
2. Descomprime el ZIP (≈ 113 MB descomprimido).
3. Ejecuta `BackroomsEscape.exe`.

## Niveles

| Nivel | Nombre | Ambiente | Criatura |
|---|---|---|---|
| 0 | Oficinas amarillas | Papel pintado amarillento, moqueta húmeda y fluorescentes parpadeantes | El Alto |
| 1 | Aparcamiento | Hormigón, pilares, techo alto y muy oscuro | El Coloso |
| 2 | Poolrooms | Azulejos blancos de piscina con franja azul y agua realista (olas, reflejos, espuma, cáusticas, vadeo, restos flotantes); luz azulada | El Ahogado |
| 3 | Hotel abandonado | Pasillos estrechos con moqueta roja, lámparas casi muertas y niebla densa | 2 × El Huésped |
| 4 | Complejo industrial | Chapa oxidada, tuberías y luces de emergencia rojas | 2 × El Hollín (más rápidos) |

Al llegar a la puerta de salida se carga el siguiente nivel; tras el nivel 4 aparece la pantalla de victoria (tiempo total e intentos fallidos). Si te atrapan, el nivel se reinicia.

## Novedades de la v6.7

- **Inventario realista:** maletín metálico con espuma, iconos 3D de cada objeto, peso y estado, y arrastrar y soltar entre huecos.
- **Textos y fuentes con tildes:** fuentes incluidas con todos los acentos y símbolos; textos revisados (notas, sala multijugador, descripciones).
- **Objetivo dentro del recuadro:** el objetivo ya no se sale de su caja en ninguna resolución (ultrapanorámica y 5:4 incluidas).
- **Pasos −8 dB:** pasos (también de los demás jugadores), respiración y zumbido más suaves.
- **Luz del Nivel 0:** luz más cálida y suave, panel del techo menos quemado y caída de luz más inquietante.
- **Agua realista en Poolrooms:** refracción del fondo, absorción azul verdosa por profundidad, reflejos con Fresnel, brillos y cáusticas animadas en el fondo y las paredes.

## Novedades de la v6.6: susto de muerte visible y texturas realistas

- **El susto de muerte ahora se ve:** antes la entidad quedaba a oscuras, tapada por muros/niebla o mal encuadrada y el glitch VHS la ocultaba. Ahora, al atraparte, la cámara dibuja solo al monstruo (capa propia, fondo rojo oscuro, sin niebla ni muros), con **luz fría de frente y luz roja de contorno**, y encuadra la cara con **tres planos de corte seco** (rostro y hombros, primerísimo plano de los ojos y embestida), unos 2 s + fundido, antes de «HAS MUERTO». Funciona con las 5 entidades; «Susto suave» sigue reduciendo luces, destellos, sacudida y glitch. En multijugador el susto completo solo salta cuando caen todos.
- **Texturas horneadas con Blender:** paredes, suelos y techos de los 5 niveles usan texturas tileables (albedo 1024 px, 2048 px en pared y moqueta del nivel 0, normal y máscara PBR) creadas con materiales procedurales de Blender (papel con rayas y manchas, moqueta, techo acústico, hormigón, azulejos de piscina, damasco de hotel, chapa oxidada y lagrimada). Si faltan, el juego vuelve a las texturas generadas por código. El ZIP pasa de ≈ 46 a ≈ 57 MB.
- Los zombis usan el clip *Walk* en el susto (su *Idle* era una pose en T).
- Multijugador sin cambios de protocolo (MP-2).

## Novedades de la v6.5: audio más suave e intro

- **Audio ambiente más suave (el «soplo de aire»):** se analizaron todas las fuentes de sonido del juego y el culpable principal era el ruido grave del drone ambiental de los niveles 1-4. Ahora el drone es **solo tonal**, el zumbido de la cinta VHS en reposo es un tono grave y el siseo de ruido solo aparece con interferencias y mucho más bajo. Los sonidos del ascensor (aire y retumbo) también pasan a ser tonales. Se arregló lo mejor que se pudo medir con análisis del audio; no se ha comprobado con oídos ajenos, así que puede quedar algún matiz.
- **Solo zumban las 3 luces de techo más cercanas** (antes sonaban decenas de zumbidos 3D a la vez).
- **Nueva opción «Siseo VHS / ruido»** en Opciones (activada por defecto) para quitar el siseo de cinta.
- **Intro de arranque** (~9 s, estilo found footage: estática VHS, inserción de cinta, aviso de contenido, título y fundido al menú). Se salta con cualquier tecla o clic pasados 2 s. El menú principal ahora habla de cinco niveles.
- **Pantalla de Unity:** el logo de Unity puede seguir apareciendo al arrancar por la licencia Personal de Unity (no se puede quitar con esa licencia).

## Novedades de la v6.4: el ascensor

- **Cada nivel empieza con un descenso en ascensor** (obligatorio, también en multijugador): la cabina cae a toda velocidad durante unos 8 s, con sacudidas de cámara, contador de planta que baja rápido, frenazo con sacudida, «ding» y apertura de puertas al nivel. Mientras dura, el jugador no puede moverse y los monstruos esperan quietos. La animación **solo se puede saltar tras morir y reiniciar el nivel** (`Espacio` / `E`).
- **Multijugador:** todos descienden a la vez en la misma cabina. El protocolo de red pasa a **MP-2**, así que **todos los jugadores necesitan la v6.4** (la v6.3 y anteriores no pueden unirse).
- **Créditos del modelo:** la cabina y las puertas son CC0 («Elevator car interior» y «Elevator door pair», del *Backrooms and Liminal Spaces Kit* de 3D Assets); el resto (indicador de planta, luces, sonido, animación de puertas) es procedural. Ver [CREDITS.md](CREDITS.md).

## Novedades de la v6.3

- **Sonido «de aire» / respiración más suave:** la respiración ya no suena en bucle continuo. Ahora son sonidos puntuales de inhalar/exhalar, solo tras correr o con poca resistencia, mucho más bajos; también se bajaron unos dB los drones de ambiente del hotel y del complejo industrial.
- **Nuevos controles de volumen en Opciones:** **Volumen general**, **Efectos** y **Ambiente**, más un interruptor para activar o desactivar la **Respiración**.
- **Ahora son 5 niveles:** se eliminó el nivel «Poolrooms» del salón grande (el basado en el modelo propio) y el nivel de agua «Salas inundadas» pasa a llamarse **Poolrooms** (nivel 2). Orden: 0 Oficinas amarillas · 1 Aparcamiento · 2 Poolrooms · 3 Hotel abandonado · 4 Complejo industrial.
- **Se eliminó el monstruo «El Reptador»**. Quedan 5 criaturas: El Alto, El Coloso, El Ahogado, El Huésped y El Hollín.
- Las notas por nivel pasan a ser 15 en total (3 por nivel).

## Multijugador (v6.6, prueba temprana)

Modo **cooperativo de 2 a 4 jugadores**. El modo de un jugador no cambia: la red solo se activa desde el menú **Multijugador**.

> ⚠️ **Prueba temprana.** Se ha probado en local y con un servidor intermedio real (el servidor oficial integrado, ver abajo). **Las pruebas con amigos por Internet están pendientes.** Los jugadores 3 y 4 se probaron con clientes simulados, y UPnP/NAT-PMP solo contra routers simulados (no hay pruebas con routers reales).

### Novedades de la v6.2 (juego por Internet)

- **Alojar con código / Unirse con código:** el anfitrión pulsa **Alojar con código**, la sala muestra un **código de 6 caracteres** y los amigos entran con **Unirse con código**. **No hace falta abrir puertos**, ni UPnP, ni VPN, y funciona incluso detrás de CGNAT. Usa un servidor intermedio (relay) que empareja a los jugadores por el código y reenvía los bytes.
- **Servidor oficial integrado:** la v6.2 lleva incorporado el servidor oficial, así que esos botones funcionan sin configurar nada. Su dirección va **cifrada y ofuscada dentro del juego**; no aparece en la interfaz, en los logs ni en este repositorio. Ojo: es **ofuscación, no un secreto real** (un cliente necesita conocer la dirección para conectarse); la protección real es un token en el servidor. El servidor oficial puede no estar disponible o cambiar en futuras versiones.
- **Apertura automática de puerto (UPnP / NAT-PMP):** al **Crear sala** (modo IP y puerto) el juego intenta abrir el puerto TCP elegido en tu router y lo cierra al salir. Muestra el estado en español.
- **IP pública visible:** la pantalla del anfitrión muestra tus IP locales, tu **IP pública** (consultada a servicios como `api.ipify.org`) y un botón **Copiar dirección**. El campo de IP del cliente acepta `ip:puerto` pegado.
- **Servidor propio:** la carpeta [`relay-server/`](relay-server/) contiene el relay (Node.js, sin dependencias, con Dockerfile y `fly.toml`) para alojar el tuyo. Puedes indicarlo en el campo «Servidor relay» del menú, en un `relay.txt` junto a `BackroomsEscape.exe` (`mi-servidor.ejemplo.com:7780`, puerto por defecto **7780**) o con la variable de entorno `BACKROOMS_RELAY`. Si compilas el proyecto desde este repositorio, **no incluye el servidor oficial** (su carga cifrada no se publica): usará solo el que configures.

### Cómo jugar con amigos

**Opción A: con código (recomendada, sin abrir puertos)**

1. Todos necesitáis la **misma versión** (v6.7). El protocolo sigue siendo **MP-2** (el de la v6.4): la v6.3 y anteriores **no pueden unirse**; v6.4, v6.5 y v6.6 comparten protocolo, pero lo más seguro es que todos uséis la misma versión.
2. **Anfitrión:** menú principal → **Multijugador** → tu nombre → **Alojar con código**. Pasa a tus amigos el código de la sala.
3. **Clientes:** Multijugador → tu nombre → **Unirse con código** → escribe el código → entras en la sala de espera.
4. El anfitrión elige el nivel con ◄ ► y pulsa **Empezar**. No se puede entrar en una partida ya empezada.

**Opción B: IP y puerto (TCP 7777)**

1. **Anfitrión:** Multijugador → **Alojar partida** → puerto **TCP 7777** (o el que quieras) → **Crear sala**. El juego intenta abrir el puerto con UPnP; si no lo consigue, abre TCP 7777 en tu router (reenvío de puertos).
2. **Cortafuegos:** la primera vez, el Firewall de Windows pedirá permitir el juego: acepta.
3. Pasa a tus amigos tu **IP y puerto**: en la misma red local, la IP local (suele empezar por `192.168.` o `10.`); por Internet, tu IP pública.
4. **Clientes:** Multijugador → **Unirse** → IP y puerto → **Conectar**.

**Alternativa: una VPN** como **Radmin VPN**, **ZeroTier** o **Hamachi**: todos entráis en la misma red virtual y el anfitrión comparte la IP que le da la VPN.

### Qué incluye

- Sala de espera con lista de jugadores (2-4) y selección de nivel por el anfitrión.
- Avatares remotos con traje hazmat, linterna y nombre; monstruos y objetos **sincronizados** (el primero en llegar se lleva la pila; lo que sueltas lo ven todos).
- **Reanimación:** si te derriban, un compañero que se quede a tu lado unos 4 s te levanta; si caen todos, se reinicia el nivel.
- **Chat** de texto con `Enter`. En multijugador, la pausa y el inventario no detienen la partida.
- El nivel termina cuando todos los jugadores vivos llegan a la salida. Con 3-4 jugadores aparece **un monstruo extra** a partir del nivel 3.

### Limitaciones conocidas

Sin reconexión a mitad de partida, sin voz, sin migración de anfitrión (si el anfitrión cae, se acaba la partida) y sin anti-trampas. Los clientes ven a los monstruos con ~0,1 s de retraso. Solo el anfitrión puede reiniciar o cambiar de nivel. Más detalles técnicos en [`unity-project/README.md`](unity-project/README.md).

## Controles

| Acción | Tecla |
|---|---|
| Moverse | `W` `A` `S` `D` |
| Mirar | Ratón |
| Correr (stamina limitada) | `Shift` |
| Agacharse (mantener / alternar) | `Ctrl` / `C` |
| Asomarse a izquierda / derecha | `Q` / `E` |
| Linterna on/off | `F` |
| Inventario y notas (pausa el juego) | `I` |
| Barra rápida (casillas 1-5; 1 = linterna) | `1`-`5` |
| Usar una pila (recarga la linterna) | `B` |
| Modo cámara VHS on/off | `V` |
| Saltar el ascensor (solo tras morir y reiniciar el nivel) | `Espacio` / `E` |
| Menú de pausa (en multijugador la partida **no** se detiene) | `Esc` |
| Chat de texto (solo multijugador; `Enter` envía, `Esc` cancela) | `Enter` |
| Reiniciar el nivel actual | `R` |
| Reanimar a un compañero derribado (multijugador): quédate a su lado ~4 s | (automático) |

## Características

- Cada nivel empieza con un descenso en ascensor (v6.4) y el juego abre con una intro found footage (v6.5).
- 5 niveles procedurales con texturas PBR, niebla, luces y audio generados por código.
- Criaturas con vista y oído (correr hace más ruido; apagar la linterna te oculta).
- Cámara VHS opcional, inventario en rejilla, notas y pilas para la linterna.
- Agua realista (olas, reflejos, espuma, cáusticas, vadeo) en el nivel 2, Poolrooms.
- Cuerpo en primera persona con animaciones, menús y opciones en español (incluido «Susto suave»).
- **Multijugador cooperativo de 2-4 jugadores** por código de sala (servidor oficial integrado) o por IP y puerto, con UPnP automático (prueba temprana).

## Estructura del repositorio

- `docs/` – sitio web (GitHub Pages).
- `unity-project/` – código fuente del proyecto Unity (Unity 2022.3 LTS, pipeline Built-in). Consulta la documentación técnica dentro del proyecto para abrirlo con Unity Hub.
- `relay-server/` – servidor intermedio (relay) para el modo «con código», por si quieres alojar el tuyo.
- `CREDITS.md` – créditos y licencias de los modelos y recursos externos (CC0).
- Las builds están en [Releases](https://github.com/rootesq-pixel/backrooms-escapa/releases) (no se guardan en git).

## Créditos

Ver [CREDITS.md](CREDITS.md). Las Backrooms son una leyenda colaborativa de Internet; este proyecto es una obra independiente.
