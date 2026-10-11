# Créditos

BACKROOMS: ESCAPA — juego en Unity (pipeline integrado). Mallas del nivel, texturas, audio, shader VHS e interfaz son procedurales / código del proyecto (sin assets externos).

## Modelos de monstruos (todos CC0 / dominio público)

Cada nivel usa una criatura distinta. Los modelos originales se convirtieron a FBX con Blender (decimado / renombrado de animaciones donde se indica) y se retexturizaron o tintaron en Unity. Los ojos brillantes, sonidos (gruñidos, pasos), temblor y efectos de terror son procedurales.

| Nivel | Id (Resources/Monsters) | Nombre | Autor | Licencia | Fuente |
|---|---|---|---|---|---|
| 0 Oficinas | HorrorStalker | El Alto | HorrorGameMaker.com (City Building Game Art) | CC0 (dominio público) | https://opengameart.org/content/3d-horror-game-monster |
| 1 Aparcamiento | GiantMutant | El Coloso | Eldritch Grim | CC0 (dominio público) | https://opengameart.org/content/giant-mutant |
| 2 Salas inundadas | AlienBug | El Reptador | CDmir | CC0 (dominio público) | https://opengameart.org/content/alien-bug-animated |
| 3 Poolrooms | DrownedZombie | El Ahogado | Rikindle3D (modelo del Huésped, reteñido) | CC0 (dominio público) | https://opengameart.org/content/male-city-zombie-ready-for-use-in-game-engines |
| 4 Hotel | CityZombie | El Huésped | Rikindle3D | CC0 (dominio público) | https://opengameart.org/content/male-city-zombie-ready-for-use-in-game-engines |
| 5 Industrial | SootHorror | El Hollín | EmoPreben | CC0 (dominio público) | https://opengameart.org/content/horror-monster-0 |

### Modificaciones

- **HorrorStalker** (El Alto): Retextura pálida/ceniza, estirado en altura; ojos y sonidos procedurales.
- **GiantMutant** (El Coloso): Malla reducida (decimado ~30 %), oscurecida; ojos y sonidos procedurales.
- **AlienBug** (El Reptador): Convertido de GLB a FBX con Blender; escalado x2.5 y húmedo/brillante; sonido de chapoteo procedural.
- **CityZombie** (El Huésped): Texturas desaturadas y reteñidas (gris verdoso) con Pillow; animaciones reasignadas al esqueleto con Blender; ojos y sonidos procedurales.
- **DrownedZombie** (El Ahogado): mismo modelo de Rikindle3D que el Huésped, con textura reteñida azul verdosa y empapada, ojos fríos y lamento de ahogado procedural.
- **SootHorror** (El Hollín): Malla decimada (~45 %) y estirada/encorvada; textura reteñida a negro hollín grasiento; animaciones de zombi (Mixamo) reasignadas con Blender; ojos pálidos, temblor y sonidos procedurales. Licencia CC0 comprobada en la página del autor el 11-oct-2026.

## Cuerpo del jugador (traje hazmat)

| Id (Resources/Player) | Nombre | Autor | Licencia | Fuente |
|---|---|---|---|---|
| PlayerHazmat | «Character Hazmat» | Quaternius | CC0 1.0 (dominio público) | https://poly.pizza/m/z3TSQYx1Kn (pack de personajes de Quaternius: https://quaternius.com) |

Licencia comprobada el 11-oct-2026 en la página de Poly Pizza (campo «Licence: CC0 1.0», «Public Domain (CC0)»). Cambios: GLB convertido a FBX con Blender; eliminadas las armas y el resto de objetos; conservadas las animaciones Idle, Walk, Run y Duck; color del traje cambiado a amarillo hazmat con guantes/botas negros (materiales de Unity). La linterna de mano, el IK del brazo, las sombras y el efecto de empapado son procedurales.

## Nivel 3 «Poolrooms»

La sala del Poolrooms procede del archivo `poolrooms_user.blend` aportado por el autor del juego (modelo propio; no requiere atribución). Se exportó con Blender a `Assets/Resources/Poolrooms/PoolHall.fbx` (sin cámara/luz/agua; texturas por código).

## CGTrader

Se intentó consultar https://www.cgtrader.com/3d-models/backrooms: casi todo es de pago y los modelos gratuitos exigen cuenta para descargar, por lo que **no se ha usado ningún modelo de CGTrader**.

Las licencias CC0 no exigen atribución; se incluye por cortesía y trazabilidad. Los archivos fuente originales no se incluyen en el repositorio (solo los FBX/texturas convertidos en `Assets/Monsters`).

## Herramientas

- Unity 2022.3 LTS, Blender (conversión de modelos), Python/Pillow (retexturizado de los zombis y del Hollín).

## Inventario, objetos y linterna (v5)

Los iconos del inventario, los modelos de los objetos recogibles (pila, agua de almendras, barra luminosa, cinta VHS, nota), la linterna de mano, las 18 notas de lore y todo el audio se generan por código; no usan activos de terceros.
