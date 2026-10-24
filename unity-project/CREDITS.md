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
| 6 Parque de bolas | PitLurker | El Sumergido | EmoPreben (modelo del Hollín, reteñido) | CC0 (dominio público) | https://opengameart.org/content/horror-monster-0 |

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

Licencia comprobada el 11-oct-2026 en la página de Poly Pizza (campo «Licence: CC0 1.0», «Public Domain (CC0)»). Cambios: GLB convertido a FBX con Blender; eliminadas las armas y el resto de objetos; conservadas las animaciones Idle, Walk, Run y Duck; color del traje cambiado a amarillo hazmat con guantes/botas negros (materiales de Unity). La linterna de mano, el IK del brazo, las sombras y el efecto de empapado son procedurales. **v7.3.0:** malla subdividida y desplegada (UV) en Blender 4.2 (`Tools/blender/suit_bake.py`, exportada como `HazmatHD.fbx`); texturas del traje propias, horneadas con Cycles a partir de materiales procedurales (pliegues, costuras, cremallera, cinturón, guantes de goma, botas con suela, suciedad y zona mojada abajo; albedo, normales y máscara metal/AO/suavidad). Las texturas son de dominio público (CC0); la malla sigue siendo CC0 de Quaternius. Guantes a escala realista: la malla de las manos y los huesos de los dedos se reducen hacia la muñeca (factor 0,66) en el mismo script, sin cambiar UV ni texturas.

## Ascensor de entrada (todos los niveles)

Cada nivel empieza dentro de una cabina de ascensor (v6.4). La carcasa y las puertas usan dos modelos estáticos del mismo kit, descargados sin cuenta ni compra desde 3D Assets:

| Archivo en el proyecto | Nombre | Autor | Licencia | Fuente |
|---|---|---|---|---|
| `Assets/Resources/Elevator/Elevator.fbx` (cabina: suelo, paredes, pasamanos, panel de botones, techo) | «Elevator car interior (Backrooms and Liminal Spaces Kit)» | 3D Assets (@3dassets) | CC0 1.0 (dominio público; no exige atribución, se cita igualmente) | https://3dassets.dev/assets/liminal-spaces-kit-elevator-car-22251ff7 (GLB: https://cdn.3dassets.dev/assets/25322/v1/model.glb) |
| `Assets/Resources/Elevator/Elevator.fbx` (dos hojas de puerta + dintel) | «Elevator door pair (Backrooms and Liminal Spaces Kit)» | 3D Assets (@3dassets) | CC0 1.0 (dominio público) | https://3dassets.dev/assets/liminal-spaces-kit-elevator-doors-b1f54075 (GLB: https://cdn.3dassets.dev/assets/25321/v1/model.glb) |

Licencia comprobada el 11-oct-2026 en las propias páginas del recurso (campo «Licence: CC0 1.0 Universal … no attribution required»; la API JSON de cada recurso devuelve `license.slug = cc0-1.0`, `attributionRequired = false`). Nota de transparencia: la página indica que el colaborador declara haber usado IA para generar estos modelos («AI used: yes»); el recurso se publica igualmente bajo CC0.

Cambios: los dos GLB se importaron en Blender, se unieron en un solo FBX (cabina + hojas de puerta, con las hojas como objetos separados `Door_L` / `Door_R`), se eliminó la jerarquía vacía y las tres texturas (256×256 px, `Elev_*.png`) se guardaron aparte. En el juego la cabina se ensancha un 30 % en X/Z para que quepan cuatro jugadores, las hojas se animan por código (se retraen hacia su lado), y el resto es procedural: visera con indicador de planta, panel de luz del techo, luces de los botones, colisiones, luz parpadeante, tintes por nivel y todos los sonidos (retumbo, motor/cables, frenazo, «ding», puertas).

### Cabina realista (v7.4.0)

Desde la v7.4.0 la cabina es un modelo propio (`Assets/Resources/Elevator/Elevator740.fbx`), modelado por script en Blender 4.2 (`Tools/v740/build_elev.py`): paneles de madera y de acero cepillado con juntas, pasamanos con soportes, botonera con columna de botones iluminados, indicador de planta sobre el dintel, placa de inspección, rejilla del altavoz, plafón con lamas, puertas correderas con marco y umbral, y suciedad acumulada en las esquinas del suelo. Es de dominio público (CC0). El modelo anterior del kit de 3D Assets sigue en el proyecto solo como reserva, si no se encuentra el nuevo.

Texturas PBR de [ambientCG](https://ambientcg.com) (CC0 1.0, dominio público; no exigen atribución, se cita igualmente). Se descargaron a 1K/2K y se reempaquetaron a 512–2048 px como albedo, normales y máscara Standard (metal/AO/suavidad) con `Tools/v740/packtex.py`, `Tools/v740/compose_l1.py` y `Tools/v750/compose_l3.py`:

| Uso | Material de ambientCG | Fuente |
|---|---|---|
| Acero cepillado y pintado de la cabina (`ElevSteel`, `ElevPaint`) | Metal009 | https://ambientcg.com/view?id=Metal009 |
| Acero oscuro (zócalos, marco) (`ElevSteelDark`) | Metal038 | https://ambientcg.com/view?id=Metal038 |
| Paneles de madera (`ElevWood`) | Wood051 | https://ambientcg.com/view?id=Wood051 |
| Suelo de goma (`ElevFloor`) | Rubber001 | https://ambientcg.com/view?id=Rubber001 |
| Nivel 1, suelo del aparcamiento (`ParkingFloor`) | Concrete042A | https://ambientcg.com/view?id=Concrete042A |
| Nivel 1, muros y pilares (`ParkingWall`) | Concrete031 | https://ambientcg.com/view?id=Concrete031 |
| Nivel 1, techo (`ParkingCeiling`) | Concrete012 | https://ambientcg.com/view?id=Concrete012 |
| Nivel 3, madera del zócalo, puertas y marcos (`HotelWall`, `HotelDoor`, `HotelWood`) | Wood066 | https://ambientcg.com/view?id=Wood066 |
| Nivel 3, pelo de la moqueta (`HotelCarpet`; dibujo propio) | Carpet012 | https://ambientcg.com/view?id=Carpet012 |
| Nivel 3, soporte del papel pintado y tapicería/ropa de cama (`HotelWall`, `HotelFabric`; damasco propio) | Fabric045 | https://ambientcg.com/view?id=Fabric045 |
| Nivel 3, yeso del techo, zonas despegadas y pintura de servicio (`HotelCeiling`, `HotelWall`, `HotelService`) | Plaster001 | https://ambientcg.com/view?id=Plaster001 |

Nivel 3 (v7.5.0), trabajo propio (CC0): dibujo del damasco (`Tools/v750/damask.py`), rombos de la moqueta, cuadros (paisajes, retratos sin cara, marinas y bodegones dibujados por script), linóleo a cuadros de los pasillos de servicio, y todos los muebles del hotel (camas, armarios, mesillas, lámparas, recepción, mesas, sillas, piano, arañas, ventanales, puertas y ascensores), modelados por código en `HotelDecor.cs`. Números de latón: fuente DejaVu Serif (licencia libre Bitstream Vera).

Añadidos propios sobre esas texturas (numpy/Pillow, CC0): líneas amarillas gastadas, rodadas de neumáticos, suciedad, agujeros de encofrado con regueros de óxido, manchas de humedad y eflorescencias, anillos de agua en el techo, manchas de aceite y charcos (calcomanías del suelo del Nivel 1). Además: placa de inspección, rejilla y suciedad de la cabina.

## Poolrooms (nivel 2)

El nivel de agua (antes «Salas inundadas») se llama ahora Poolrooms. Es totalmente procedural (sin modelos externos). En la v6.3 se eliminó la sala del modelo propio del autor (`PoolHall.fbx`) y el monstruo «El Reptador» (AlienBug) ya no se usa ni se incluye.

## CGTrader

Se intentó consultar https://www.cgtrader.com/3d-models/backrooms: casi todo es de pago y los modelos gratuitos exigen cuenta para descargar, por lo que **no se ha usado ningún modelo de CGTrader**.

Las licencias CC0 no exigen atribución; se incluye por cortesía y trazabilidad. Los archivos fuente originales no se incluyen en el repositorio (solo los FBX/texturas convertidos en `Assets/Monsters`).

## Texturas de superficie (v6.6)

Todas las texturas de paredes, suelos y techos de los cinco primeros niveles (0–4) son **trabajo propio y 100 % procedurales**: materiales por nodos de Blender (ruido 4D, Voronoi, rampas de color, máscaras de manchas/humedad/óxido/desgaste) escritos como scripts de Python en `Tools/blender/` (`texlib.py`, `styles.py`, `bake.py`, `pack_textures.py`) y horneados a albedo / normal / máscara Standard (metálico, oclusión, suavidad) en `Assets/Resources/Textures/`. No se usa ninguna foto ni textura de terceros. El ruido se evalúa sobre un toro 4D, por lo que las texturas se repiten sin costuras. La generación por CPU anterior (`ProceduralTextures`) se conserva como alternativa si falta alguna textura.

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

## Tipografías (Assets/Resources/Fonts)

| Archivo | Familia | Autor | Licencia |
|---|---|---|---|
| EscapaMono-Regular/Bold.ttf | versión modificada de IBM Plex Mono | IBM Corp. (original) | SIL Open Font License 1.1 |
| EscapaSansCondensed-Regular/SemiBold.ttf | versión modificada de IBM Plex Sans Condensed | IBM Corp. (original) | SIL Open Font License 1.1 |
| CourierPrime-Regular/Bold.ttf | Courier Prime (sin modificar) | Alan Dague-Greene | SIL Open Font License 1.1 |

Las fuentes «Escapa» son IBM Plex (Copyright © 2017 IBM Corp., Reserved Font Name «Plex») con los símbolos ► ◄ ● ⚠ ✖ ✔ ▲ ▼ ■ □ de DejaVu Sans (licencia Bitstream Vera) añadidos con fontTools, para que la interfaz no dependa de las fuentes del sistema. Al ser versiones modificadas se han renombrado, como exige la OFL para nombres reservados; se distribuyen bajo la misma OFL 1.1.

## Iconos del inventario

`Assets/Resources/Icons/*.png`: renderizados en Blender (Cycles) a partir de modelos 3D creados con `Tools/blender/item_icons.py`. Texturas del maletín, espuma, papel y cinta (`Assets/Resources/UI`): generadas con `Tools/inventory_textures.py`. Obra propia del proyecto.

## v7.0 — Campo infinito (Assets/Resources/Field)

| Recurso | Uso | Autor / fuente | Licencia |
|---|---|---|---|
| grass_medium_01, grass_medium_02, grass_bermuda_01, dandelion_01 (modelos y texturas escaneados) | matas de hierba instanciadas (decimadas con Blender a ≤700 triángulos, alfa dilatado) | Poly Haven — https://polyhaven.com/models | CC0 |
| sparse_grass (texturas PBR) | suelo del campo | Poly Haven — https://polyhaven.com/a/sparse_grass | CC0 |
| damaged_plaster, grey_roof_01, oak_wood_planks (texturas PBR 1k) | paredes, tejados y suelos de las casas | Poly Haven | CC0 |
| kloofendal_overcast_puresky (HDRI 2k) | cielo nublado | Poly Haven (Greg Zaal) | CC0 |
| footstep_grass_000–004 («Impact Sounds») | pisadas en hierba (normalizadas a pico 0,6) | Kenney — https://kenney.nl/assets/impact-sounds | CC0 |

El roce de la hierba, el viento, el zumbido de radio de la salida, el terreno, las casas, los postes y las puertas sueltas se generan por código.
«El Vigía (Nivel 5)» (FieldWatcher) reutiliza el modelo CC0 de El Alto (HorrorGameMaker.com) con una textura reteñida con Pillow.

## Parque de bolas (v7.1.0)
- **Sala de juegos** (piscinas de bolas, estructura acolchada con redes, toboganes, columnas, bloques de espuma, sala de fiestas, mostrador de meriendas, fluorescentes): trabajo propio, modelada por script en Blender 4.2 (`Tools/ballpit/build_hall.py`) con iluminación global horneada en Cycles (lightmap HDR + sondas) y eliminación de ruido con Intel Open Image Denoise. Dominio público (CC0).
- **Texturas** (goma EVA, vinilo acolchado, pared con mural de globos, plástico, red, bolas) y **atlas de carteles** (menú, normas, póster, banderines, dibujo infantil): generadas por código con NumPy/Pillow (`Tools/ballpit/textures.py`, `atlas.py`; tipografía DejaVu, licencia libre de Bitstream Vera/DejaVu). Dominio público (CC0).
- **Bolas**: miles de instancias en GPU con un shader propio (`BallInstanced.shader`); se apartan alrededor de las piernas y se levantan cuando algo se arrastra por debajo.
- **Audio**: roce de bolas, caja de música (melodía de la «Canción de cuna» de J. Brahms, 1868, dominio público) y risas infantiles lejanas sintetizadas por formantes; todo generado por código (`ProceduralAudio.cs`), sin grabaciones de terceros.
- **«El Sumergido (Nivel 6)»** (PitLurker) reutiliza el modelo CC0 de El Hollín (EmoPreben) con una textura reteñida con NumPy/Pillow (piel cerosa gris pálida con manchas desteñidas de bolas de colores).

## Objetos recogibles y silbido (v7.0.2)
- **Modelos de los objetos** (pila, agua de almendras, barra luminosa, cinta VHS, nota, linterna): trabajo propio, modelados por script en Blender 4.2 (`Tools/blender/item_models.py`) y horneados con Cycles (alto → bajo detalle) a texturas PBR de 512 px (albedo, metal/suavidad, normal, emisión). Los iconos del inventario se renderizan con los mismos modelos. Dominio público (CC0).
- **Silbido** (`G`, tres variantes): sintetizado por código en el propio juego (`Whistle.cs`, osciladores con vibrato, respiración y envolvente). Sin grabaciones de terceros. Dominio público (CC0).
- **Muebles de las casas del Campo infinito** (mesa, estantería, caja): primitivas con la textura de madera procedural del proyecto.

## Cascos de buzo del nivel 7 (v7.3.0)
- **Malla y texturas de los cascos** (latón envejecido: albedo, normales con remaches, pernos y costuras, máscara metal/oclusión/suavidad con cardenillo y aristas pulidas): trabajo propio; malla generada por código (`OceanHelmetRack.cs`) y texturas procedurales generadas con `Tools/textures/gen_helmet_tex.py` (numpy, semilla 7301). Dominio público (CC0).

## El Farolero (v7.3.0)
- **Modelo, dientes, aletas, ilicio y esca** del rape abisal El Farolero: diseño y trabajo propios, modelado por código en Blender 4.2 (`Tools/blender/angler_build.py`: metabolas, remallado de vóxeles y booleanas; agujas, aletas con radios y bordes desgarrados, ilicio carnoso). Texturas de la piel propias (casi negra y mojada, con tubérculos, cicatrices, venas, encías y fotóforos tenues; albedo, normales, máscara AO/suavidad y emisión), horneadas con Cycles a partir de materiales procedurales. Animación por código (`OceanCreatures.cs`, `OceanFinWave.cs`). Dominio público (CC0).

## Muelle del nivel 7 (v7.3.0)
- **Tablas, pilotes y cuerdas del muelle** (madera envejecida y mojada con veta, grietas, nudos, juntas y clavos oxidados; pilotes con algas, musgo y percebes en la línea de flotación; cuerda trenzada; abrazaderas y zapatas de metal): trabajo propio; geometría por código (`OceanWorld.cs`, `OceanGeo.cs`) y texturas procedurales con `Tools/textures/gen_pier_tex.py` (numpy, semilla 7302). Dominio público (CC0).

## Sofá, barca, muebles y esculturas del nivel 7 (v7.3.0)
- **Sofá empapado** de la casa inundada (cojines, brazos y respaldo hundidos; tela gastada con trama, ribetes, costuras, pliegues, manchas, línea de agua, algas y moho), **barca de tingladillo** (tablas pintadas y desconchadas con veta, remaches de cobre, cuadernas, bancadas, regala, toletes de hierro, remos, poste con farol y motor fueraborda; algas e incrustaciones en la línea de flotación), **muebles de las casas** (mesa con patas, faldón, vetas y anillos de agua; sillas de listones; cama de hierro oxidado con colchón de cutí, sábanas húmedas arrugadas, manta y almohada) y **esculturas colosales del fondo** (cabeza con los ojos cerrados medio enterrada, mano gigante y rostro tumbado; piedra erosionada con grietas, algas y percebes): diseño y trabajo propios, modelado por código en Blender 4.2 (`Tools/blender/props_common.py`, `sofa_build.py`, `boat_build.py`, `furniture_build.py`, `sculpt_build.py`) con texturas procedurales horneadas con Cycles (albedo, normales, máscara metal/AO/suavidad y emisión del farol). No se copian diseños de otros juegos. Dominio público (CC0).
- **Máscara de agua de la barca** (`Hidden/BoatWaterMask`, stencil): técnica estándar, código propio. CC0.
