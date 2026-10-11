# BACKROOMS: ESCAPA

Juego de terror en primera persona para PC (Windows 64 bits) hecho con **Unity**: escapa de las Backrooms a través de **6 niveles** encadenados. Cada nivel es un laberinto (o aparcamiento abierto) procedural con su propia estética, luces, dificultad y criaturas que patrullan y te persiguen. La salida es una puerta discreta que **solo se descubre explorando**: sin cartel, sin flecha, sin indicador de distancia.

- 🌐 **Web:** https://rootesq-pixel.github.io/backrooms-escapa/
- ⬇️ **Descarga directa (Windows, ZIP ≈ 46 MB):** [BackroomsEscape_v5_Windows.zip](https://github.com/rootesq-pixel/backrooms-escapa/releases/download/v5.0.0/BackroomsEscape_v5_Windows.zip)
- 📦 **Release v5.0.0:** https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v5.0.0

> ⚠️ **Build temprana.** Puede haber errores. El ejecutable **no está firmado**, así que Windows SmartScreen puede avisar al abrirlo (*Más información → Ejecutar de todas formas*).

## Cómo jugar

1. Descarga `BackroomsEscape_v5_Windows.zip` desde la [release](https://github.com/rootesq-pixel/backrooms-escapa/releases/tag/v5.0.0).
2. Descomprime el ZIP (≈ 103 MB descomprimido).
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
| Menú de pausa | `Esc` |
| Reiniciar el nivel actual | `R` |

## Características

- 6 niveles procedurales con texturas PBR, niebla, luces y audio generados por código.
- Criaturas con vista y oído (correr hace más ruido; apagar la linterna te oculta).
- Cámara VHS opcional, inventario en rejilla, notas y pilas para la linterna.
- Agua realista y buceo en los niveles 2 y 3.
- Cuerpo en primera persona con animaciones, menús y opciones en español (incluido «Susto suave»).

## Estructura del repositorio

- `docs/` – sitio web (GitHub Pages).
- `unity-project/` – código fuente del proyecto Unity (Unity 2022.3 LTS, pipeline Built-in). Consulta la documentación técnica dentro del proyecto para abrirlo con Unity Hub.
- `CREDITS.md` – créditos y licencias de los modelos y recursos externos (CC0).
- Las builds están en [Releases](https://github.com/rootesq-pixel/backrooms-escapa/releases) (no se guardan en git).

## Créditos

Ver [CREDITS.md](CREDITS.md). Las Backrooms son una leyenda colaborativa de Internet; este proyecto es una obra independiente.
