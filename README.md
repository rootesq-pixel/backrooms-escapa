# BACKROOMS: ESCAPA

Juego de terror en primera persona para PC (Windows 64 bits) hecho con **Unity**: escapa de las Backrooms a través de **6 niveles** encadenados. Cada nivel es un laberinto (o aparcamiento abierto) procedural con su propia estética, luces, dificultad y criaturas que patrullan y te persiguen. La salida es una puerta discreta que **solo se descubre explorando**: sin cartel, sin flecha, sin indicador de distancia.

- 🌐 **Web:** https://rootesq-pixel.github.io/backrooms-escapa/
- ⬇️ **Descarga directa v6 (Windows, ZIP ≈ 46 MB):** [BackroomsEscape_v6_Windows.zip](https://github.com/rootesq-pixel/backrooms-escapa/releases/download/v6.0.0/BackroomsEscape_v6_Windows.zip)
- 📦 **Release v6.0.0 — Multijugador:** https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v6.0.0
- 🕓 Versión anterior: [v5.0.0](https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v5.0.0) (solo un jugador)

> ⚠️ **Build temprana.** Puede haber errores. El **multijugador solo se ha probado en local (localhost)**: no se ha probado por Internet real ni con 3-4 personas reales. El ejecutable **no está firmado**, así que Windows SmartScreen puede avisar al abrirlo (*Más información → Ejecutar de todas formas*).

## Cómo jugar

1. Descarga `BackroomsEscape_v6_Windows.zip` desde la [release v6.0.0](https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v6.0.0).
2. Descomprime el ZIP (≈ 107 MB descomprimido).
3. Ejecuta `BackroomsEscape.exe`.

## Niveles

| Nivel | Nombre | Ambiente | Criatura |
|---|---|---|---|
| 0 | Oficinas amarillas | Papel pintado amarillento, moqueta húmeda y fluorescentes parpadeantes | El Alto |
| 1 | Aparcamiento | Hormigón, pilares, techo alto y muy oscuro | El Coloso |
| 2 | Salas inundadas | Azulejos de piscina y agua realista (olas, reflejos, espuma, cáusticas) | El Reptador |
| 3 | Poolrooms | Laberinto de azulejos cian-blanco con un gran salón central y un foso donde se nada y se bucea | El Ahogado |
| 4 | Hotel abandonado | Pasillos estrechos con moqueta roja, lámparas casi muertas y niebla densa | 2 × El Huésped |
| 5 | Complejo industrial | Chapa oxidada, tuberías y luces de emergencia rojas | 2 × El Hollín (más rápidos) |

Al llegar a la puerta de salida se carga el siguiente nivel; tras el nivel 5 aparece la pantalla de victoria (tiempo total e intentos fallidos). Si te atrapan, el nivel se reinicia.

## Multijugador (v6, prueba temprana)

Modo **cooperativo de 2 a 4 jugadores**. El modo de un jugador no cambia: la red solo se activa desde el menú **Multijugador**.

> ⚠️ Solo se ha probado **en local (localhost)**, con dos instancias del juego; los jugadores 3 y 4 se probaron con clientes simulados en la sala. **No se ha probado por Internet real, con latencia/pérdida reales ni con 3-4 personas reales.**

### Cómo jugar con amigos

1. **Todos** necesitáis la **misma versión** (la v6): el juego comprueba el protocolo y rechaza versiones distintas.
2. **Anfitrión:** menú principal → **Multijugador** → escribe tu nombre → **Alojar partida** → deja el puerto **TCP 7777** (o cambia el número) → **Crear sala**. La pantalla muestra tus IP locales (LAN / VPN).
3. **Cortafuegos:** la primera vez, el **Firewall de Windows** pedirá permitir el juego: acepta (o abre el puerto TCP 7777 de entrada).
4. Pasa a tus amigos tu **IP y el puerto**:
   - **Misma red local (mismo wifi):** la IP de tu red local (suele empezar por `192.168.` o `10.`).
   - **Por Internet:** redirige el puerto **TCP 7777** de tu router hacia tu PC (reenvío de puertos) y comparte tu IP pública.
   - **Más fácil, sin tocar el router: una VPN** como **Radmin VPN**, **ZeroTier** o **Hamachi**. Todos entráis en la misma red virtual y el anfitrión comparte la IP que le da la VPN.
5. **Clientes:** Multijugador → nombre → **Unirse** → IP y puerto del anfitrión → **Conectar**. Entraréis en la sala de espera.
6. El anfitrión elige el nivel con ◄ ► y pulsa **Empezar**. No se puede entrar en una partida ya empezada.

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
| Nadar / bucear en agua profunda | `W` + `Ctrl` |
| Menú de pausa (en multijugador la partida **no** se detiene) | `Esc` |
| Chat de texto (solo multijugador; `Enter` envía, `Esc` cancela) | `Enter` |
| Reiniciar el nivel actual | `R` |
| Reanimar a un compañero derribado (multijugador): quédate a su lado ~4 s | (automático) |

## Características

- 6 niveles procedurales con texturas PBR, niebla, luces y audio generados por código.
- Criaturas con vista y oído (correr hace más ruido; apagar la linterna te oculta).
- Cámara VHS opcional, inventario en rejilla, notas y pilas para la linterna.
- Agua realista y buceo en los niveles 2 y 3.
- Cuerpo en primera persona con animaciones, menús y opciones en español (incluido «Susto suave»).
- **Multijugador cooperativo de 2-4 jugadores** (TCP, por IP y puerto; prueba temprana).

## Estructura del repositorio

- `docs/` – sitio web (GitHub Pages).
- `unity-project/` – código fuente del proyecto Unity (Unity 2022.3 LTS, pipeline Built-in). Consulta la documentación técnica dentro del proyecto para abrirlo con Unity Hub.
- `CREDITS.md` – créditos y licencias de los modelos y recursos externos (CC0).
- Las builds están en [Releases](https://github.com/rootesq-pixel/backrooms-escapa/releases) (no se guardan en git).

## Créditos

Ver [CREDITS.md](CREDITS.md). Las Backrooms son una leyenda colaborativa de Internet; este proyecto es una obra independiente.
