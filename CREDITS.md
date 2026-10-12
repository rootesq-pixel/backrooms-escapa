# Créditos

BACKROOMS: ESCAPA — juego en Unity (pipeline integrado). Mallas del nivel, texturas, audio, shader VHS e interfaz son procedurales / código del proyecto (sin assets externos; las texturas de superficie se hornean desde materiales procedurales de Blender, ver «Texturas»).

## Modelos de monstruos (todos CC0 / dominio público)

Cada nivel usa una criatura distinta. Los modelos originales se convirtieron a FBX con Blender (decimado / renombrado de animaciones donde se indica) y se retexturizaron o tintaron en Unity. Los ojos brillantes, sonidos (gruñidos, pasos), temblor y efectos de terror son procedurales.

| Nivel | Id (Resources/Monsters) | Nombre | Autor | Licencia | Fuente |
|---|---|---|---|---|---|
| 0 Oficinas | HorrorStalker | El Alto | HorrorGameMaker.com (City Building Game Art) | CC0 (dominio público) | https://opengameart.org/content/3d-horror-game-monster |
| 1 Aparcamiento | GiantMutant | El Coloso | Eldritch Grim | CC0 (dominio público) | https://opengameart.org/content/giant-mutant |
| 2 Poolrooms | DrownedZombie | El Ahogado | Rikindle3D (modelo del Huésped, reteñido) | CC0 (dominio público) | https://opengameart.org/content/male-city-zombie-ready-for-use-in-game-engines |
| 3 Hotel | CityZombie | El Huésped | Rikindle3D | CC0 (dominio público) | https://opengameart.org/content/male-city-zombie-ready-for-use-in-game-engines |
| 4 Industrial | SootHorror | El Hollín | EmoPreben | CC0 (dominio público) | https://opengameart.org/content/horror-monster-0 |

### Modificaciones

- **HorrorStalker** (El Alto): Retextura pálida/ceniza, estirado en altura; ojos y sonidos procedurales.
- **GiantMutant** (El Coloso): Malla reducida (decimado ~30 %), oscurecida; ojos y sonidos procedurales.
- **CityZombie** (El Huésped): Texturas desaturadas y reteñidas (gris verdoso) con Pillow; animaciones reasignadas al esqueleto con Blender; ojos y sonidos procedurales.
- **DrownedZombie** (El Ahogado): mismo modelo de Rikindle3D que el Huésped, con textura reteñida azul verdosa y empapada, ojos fríos y lamento de ahogado procedural.
- **SootHorror** (El Hollín): Malla decimada (~45 %) y estirada/encorvada; textura reteñida a negro hollín grasiento; animaciones de zombi (Mixamo) reasignadas con Blender; ojos pálidos, temblor y sonidos procedurales. Licencia CC0 comprobada en la página del autor el 11-oct-2026.

## Cuerpo del jugador (traje hazmat)

| Id (Resources/Player) | Nombre | Autor | Licencia | Fuente |
|---|---|---|---|---|
| PlayerHazmat | «Character Hazmat» | Quaternius | CC0 1.0 (dominio público) | https://poly.pizza/m/z3TSQYx1Kn (pack de personajes de Quaternius: https://quaternius.com) |

Licencia comprobada el 11-oct-2026 en la página de Poly Pizza (campo «Licence: CC0 1.0», «Public Domain (CC0)»). Cambios: GLB convertido a FBX con Blender; eliminadas las armas y el resto de objetos; conservadas las animaciones Idle, Walk, Run y Duck; color del traje cambiado a amarillo hazmat con guantes/botas negros (materiales de Unity). La linterna de mano, el IK del brazo, las sombras y el efecto de empapado son procedurales.

## Ascensor de entrada (todos los niveles)

Cada nivel empieza dentro de una cabina de ascensor (v6.4). La carcasa y las puertas usan dos modelos estáticos del mismo kit, descargados sin cuenta ni compra desde 3D Assets:

| Archivo en el proyecto | Nombre | Autor | Licencia | Fuente |
|---|---|---|---|---|
| `Assets/Resources/Elevator/Elevator.fbx` (cabina: suelo, paredes, pasamanos, panel de botones, techo) | «Elevator car interior (Backrooms and Liminal Spaces Kit)» | 3D Assets (@3dassets) | CC0 1.0 (dominio público; no exige atribución, se cita igualmente) | https://3dassets.dev/assets/liminal-spaces-kit-elevator-car-22251ff7 (GLB: https://cdn.3dassets.dev/assets/25322/v1/model.glb) |
| `Assets/Resources/Elevator/Elevator.fbx` (dos hojas de puerta + dintel) | «Elevator door pair (Backrooms and Liminal Spaces Kit)» | 3D Assets (@3dassets) | CC0 1.0 (dominio público) | https://3dassets.dev/assets/liminal-spaces-kit-elevator-doors-b1f54075 (GLB: https://cdn.3dassets.dev/assets/25321/v1/model.glb) |

Licencia comprobada el 11-oct-2026 en las propias páginas del recurso (campo «Licence: CC0 1.0 Universal … no attribution required»; la API JSON de cada recurso devuelve `license.slug = cc0-1.0`, `attributionRequired = false`). Nota de transparencia: la página indica que el colaborador declara haber usado IA para generar estos modelos («AI used: yes»); el recurso se publica igualmente bajo CC0.

Cambios: los dos GLB se importaron en Blender, se unieron en un solo FBX (cabina + hojas de puerta, con las hojas como objetos separados `Door_L` / `Door_R`), se eliminó la jerarquía vacía y las tres texturas (256×256 px, `Elev_*.png`) se guardaron aparte. En el juego la cabina se ensancha un 30 % en X/Z para que quepan cuatro jugadores, las hojas se animan por código (se retraen hacia su lado), y el resto es procedural: visera con indicador de planta, panel de luz del techo, luces de los botones, colisiones, luz parpadeante, tintes por nivel y todos los sonidos (retumbo, motor/cables, frenazo, «ding», puertas).

## Poolrooms (nivel 2)

El nivel de agua (antes «Salas inundadas») se llama ahora Poolrooms. Es totalmente procedural (sin modelos externos). En la v6.3 se eliminó la sala del modelo propio del autor (`PoolHall.fbx`) y el monstruo «El Reptador» (AlienBug) ya no se usa ni se incluye.

## CGTrader

Se intentó consultar https://www.cgtrader.com/3d-models/backrooms: casi todo es de pago y los modelos gratuitos exigen cuenta para descargar, por lo que **no se ha usado ningún modelo de CGTrader**.

Las licencias CC0 no exigen atribución; se incluye por cortesía y trazabilidad. Los archivos fuente originales no se incluyen en el repositorio (solo los FBX/texturas convertidos en `Assets/Monsters`).

## Texturas de superficie (v6.6)

Todas las texturas de paredes, suelos y techos de los 5 niveles son **trabajo propio y 100 % procedurales**: materiales por nodos de Blender (ruido 4D, Voronoi, rampas de color, máscaras de manchas/humedad/óxido/desgaste) escritos como scripts de Python en `Tools/blender/` (`texlib.py`, `styles.py`, `bake.py`, `pack_textures.py`) y horneados a albedo / normal / máscara Standard (metálico, oclusión, suavidad) en `Assets/Resources/Textures/`. No se usa ninguna foto ni textura de terceros. El ruido se evalúa sobre un toro 4D, por lo que las texturas se repiten sin costuras. La generación por CPU anterior (`ProceduralTextures`) se conserva como alternativa si falta alguna textura.

## Herramientas

- Unity 2022.3 LTS, Blender 4.2 (conversión de modelos y horneado de texturas procedurales), Python/Pillow/NumPy/OpenCV (retexturizado de los zombis y del Hollín; empaquetado de texturas).

## Inventario, objetos y linterna (v5)

Los iconos del inventario, los modelos de los objetos recogibles (pila, agua de almendras, barra luminosa, cinta VHS, nota), la linterna de mano, las 18 notas de lore y todo el audio se generan por código; no usan activos de terceros.

## Multijugador (v6)

La capa de red (`Assets/Scripts/Net/`) está escrita sobre `System.Net.Sockets` (TCP), incluido en .NET/Mono: **no se ha añadido ningún paquete ni activo de terceros**
(no se usa Netcode for GameObjects ni Unity Transport). El avatar remoto reutiliza el cuerpo hazmat de Quaternius (CC0) citado arriba.


## v6.1 (multijugador por Internet)
UPnP/NAT-PMP, detección de IP pública y el cliente del relay son código propio con `System.Net`. El servidor relay (`backrooms-relay/`) es código propio en Node.js sin dependencias. No se han añadido paquetes ni recursos de terceros.

## v6.2
Relay oficial integrado cifrado (AES/HMAC con System.Security.Cryptography) y token compartido: código propio, sin paquetes de terceros nuevos.

## v6.7
- **Fuentes** (`Assets/Resources/Fonts/`): «Escapa Mono» y «Escapa Sans Condensed» son versiones renombradas de **IBM Plex Mono / IBM Plex Sans Condensed** (© IBM Corp., SIL Open Font License 1.1) con glifos de **DejaVu** (licencia libre de Bitstream Vera/DejaVu) fusionados para símbolos; **Courier Prime** (© Quote-Unquote Apps, SIL OFL 1.1) para el texto en papel.
- **Iconos 3D del inventario** y **cáusticas del agua** (flipbook de 64 fotogramas) renderizados con Blender a partir de escenas propias; sin activos de terceros.

## Técnicas de agua (v6.7.2)

Implementación propia inspirada en proyectos con licencia MIT (no se ha copiado código):
- Optically-Realistic-Water (port de Unity del shader de Martins Upitis), MIT — reflexión planar con plano de recorte oblicuo, refracción enmascarada por profundidad, atenuación de normales con la distancia.
- WebGL Water de Evan Wallace (2011), MIT — cáusticas proyectadas y reflexión planar en piscinas.
- water-shader-unity (leonjovanovic) — separación cromática de cáusticas.
