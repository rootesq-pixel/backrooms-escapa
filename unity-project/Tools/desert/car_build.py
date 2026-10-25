# v7.8.0: todoterreno 4x4 del Nivel 114 hecho a mano en Blender (sin modelos de terceros), estilo «Defender» de caja.
# Pase de realismo: líneas de panel, bisagras y manillas, aletas en arco, chapa estriada en las aletas, rejilla con lamas,
# faros con aro cromado y lente abombada, intermitentes, matrículas, parachoques con ganchos y bola de remolque,
# snorkel con toma de aire, espejos con brazo, baca con bidones, planchas de arena, petate y pala; neumático de tacos en
# tres filas con letras en el flanco, llanta de acero con tuercas; interior con asientos con costuras, cuadro con relojes,
# consola, palancas, pedales, guarnecidos de puerta, retrovisor y parasoles.
# Desgaste (polvo abajo y en lo horizontal, chorretones, óxido en cantos, desconchones) horneado con Cycles en tres atlas:
# DCar_paint.png (carrocería, 2048), DCar_wheel.png (rueda, 1024), DCar_interior.png (interior, 1024).
# Ejes: delante = +Y de Blender, arriba = +Z. Medidas reales: ~5,1 m con rueda de repuesto y parachoques, 1,79 m de ancho
# (2,17 m con aletas y espejos), 2,2 m de alto con la baca, batalla 2,80 m.
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix
OUT = sys.argv[sys.argv.index("--") + 1]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

# colores en espacio lineal (Blender): pintura verde oliva apagada, plásticos casi negros
def mat(name, col, rough=0.6, metal=0.0, emit=None, alpha=1.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*col, 1); b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    if emit: b.inputs["Emission Color"].default_value = (*emit, 1); b.inputs["Emission Strength"].default_value = 1.0
    if alpha < 1: b.inputs["Alpha"].default_value = alpha
    m["base"] = col
    return m
M = {
 "Paint": mat("Paint", (0.17, 0.19, 0.125), 0.55, 0.1),   # oliva descolorido por el sol
 "Black": mat("Black", (0.032, 0.031, 0.029), 0.75),
 "Metal": mat("Metal", (0.30, 0.30, 0.29), 0.45, 0.9),
 "Chrome": mat("Chrome", (0.62, 0.62, 0.60), 0.15, 1.0),
 "Glass": mat("Glass", (0.25, 0.3, 0.3), 0.05, 0.0, None, 0.3),
 "Cloth": mat("Cloth", (0.035, 0.033, 0.030), 0.95),
 "Lamp": mat("Lamp", (0.9, 0.9, 0.85), 0.1, 0.0, (1, 0.95, 0.8)),
 "Tail": mat("Tail", (0.6, 0.05, 0.03), 0.2, 0.0, (0.8, 0.05, 0.02)),
 "Amber": mat("Amber", (0.75, 0.30, 0.02), 0.2),
 "Rubber": mat("Rubber", (0.030, 0.029, 0.027), 0.92),
 "Rim": mat("Rim", (0.40, 0.40, 0.38), 0.45, 0.6),
 "Orange": mat("Orange", (0.62, 0.16, 0.012), 0.5),
 "Canvas": mat("Canvas", (0.22, 0.17, 0.10), 0.95),
 "Jerry": mat("Jerry", (0.055, 0.075, 0.035), 0.5),
 "Plate": mat("Plate", (0.80, 0.78, 0.70), 0.4),
}
parts = {}   # nombre de objeto -> lista de objetos a unir

def box(group, mname, c, s, bev=0.02, rot=(0, 0, 0), seg=2):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * s[0], v.co.y * s[1], v.co.z * s[2]))
    if bev > 0: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bev, min(s) * 0.45), segments=seg, affect='EDGES', profile=0.5)
    me = bpy.data.meshes.new(group); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(group, me); sc.collection.objects.link(o)
    o.location = c; o.rotation_euler = rot; o.data.materials.append(M[mname])
    parts.setdefault(group, []).append(o); return o

def cyl(group, mname, c, r, depth, axis='X', verts=24, bev=0.0, r2=None, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r2 if r2 is not None else r, depth=depth)
    if bev > 0: bmesh.ops.bevel(bm, geom=[e for e in bm.edges if not e.is_wire], offset=bev, segments=2, affect='EDGES')
    me = bpy.data.meshes.new(group); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(group, me); sc.collection.objects.link(o); o.location = c
    o.rotation_euler = rot if rot is not None else (0, math.pi / 2, 0) if axis == 'X' else (math.pi / 2, 0, 0) if axis == 'Y' else (0, 0, 0)
    o.data.materials.append(M[mname]); parts.setdefault(group, []).append(o); return o

def torus(group, mname, c, R, r, rot=(0, 0, 0), seg=32, mseg=8):
    bm = bmesh.new()
    for i in range(seg):
        a = 2 * math.pi * i / seg
        for j in range(mseg):
            b = 2 * math.pi * j / mseg
            bm.verts.new(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a), r * math.sin(b)))
    bm.verts.ensure_lookup_table(); V = bm.verts
    for i in range(seg):
        for j in range(mseg):
            a, b2 = i * mseg + j, i * mseg + (j + 1) % mseg; c2, d = ((i + 1) % seg) * mseg + j, ((i + 1) % seg) * mseg + (j + 1) % mseg
            bm.faces.new((V[a], V[c2], V[d], V[b2]))
    me = bpy.data.meshes.new(group); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(group, me); sc.collection.objects.link(o); o.location = c; o.rotation_euler = rot
    o.data.materials.append(M[mname]); parts.setdefault(group, []).append(o); return o

def sphere(group, mname, c, r, seg=12):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=seg // 2 + 1, radius=r)
    me = bpy.data.meshes.new(group); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(group, me); sc.collection.objects.link(o); o.location = c
    o.data.materials.append(M[mname]); parts.setdefault(group, []).append(o); return o

W = 1.79 / 2; WB_F = 1.40; WB_R = -1.40; WR = 0.40; SX = 0.89   # cara exterior de los laterales
B = "Body"
# ---------------------------------------------------------------- carrocería (bañera hueca)
box(B, "Paint", (0, 1.73, 0.95), (1.70, 1.15, 0.62), 0.03)           # vano motor
box(B, "Paint", (0, 1.73, 1.275), (1.66, 1.10, 0.04), 0.02)          # capó
box(B, "Paint", (0, 1.70, 1.302), (1.10, 0.95, 0.018), 0.008)        # nervio central del capó
box(B, "Paint", (0, -0.55, 0.42), (1.70, 3.30, 0.10), 0.02)          # suelo
for sx in (-1, 1):
    box(B, "Paint", (sx * 0.86, -0.55, 0.86), (0.06, 3.30, 0.80), 0.02)   # laterales hasta la cintura
    box(B, "Paint", (sx * 0.96, WB_F, 0.86), (0.22, 1.10, 0.10), 0.03)    # aleta delantera plana
    box(B, "Metal", (sx * 0.96, WB_F + 0.05, 0.918), (0.20, 0.60, 0.012), 0.004)   # chapa estriada sobre la aleta
    for k in range(9): box(B, "Metal", (sx * 0.96, WB_F - 0.22 + k * 0.066, 0.928), (0.18, 0.012, 0.006), 0.0, (0, 0, 0.6 if k % 2 else -0.6), 1)
    for wy in (WB_F, WB_R):   # paso de rueda: aleta en arco alrededor de la parte de arriba del neumático
        n = 12
        for i in range(n):
            a = math.radians(8 + (164 * (i + 0.5) / n)); rr = 0.53
            box(B, "Black", (sx * 0.97, wy + math.cos(a) * rr, WR + math.sin(a) * rr), (0.24, 2 * rr * math.sin(math.radians(164 / n / 2)) + 0.012, 0.075), 0.008, (a + math.pi / 2, 0, 0))
            if i % 3 == 1: cyl(B, "Metal", (sx * 1.088, wy + math.cos(a) * rr, WR + math.sin(a) * rr), 0.011, 0.01, 'X', 8)   # remaches de la aleta
    box(B, "Black", (sx * 0.90, 0.0, 0.40), (0.10, 1.60, 0.06), 0.01)     # estribo
    for y in (-0.6, 0.6): box(B, "Black", (sx * 0.86, y, 0.37), (0.05, 0.06, 0.08), 0.0)   # soportes del estribo
    # líneas de panel (juntas finas un poco hundidas en oscuro) y bisagras
    for y in (0.985, 0.08, -0.02, -1.09):
        box(B, "Black", (sx * (SX + 0.001), y, 0.88), (0.004, 0.006, 0.80), 0.0)
    box(B, "Black", (sx * (SX + 0.001), -0.05, 0.485), (0.004, 2.07, 0.006), 0.0)
    for y in (0.95, -0.05):
        for z in (0.66, 1.08):
            box(B, "Metal", (sx * (SX + 0.012), y, z), (0.02, 0.05, 0.09), 0.006)
            cyl(B, "Metal", (sx * (SX + 0.025), y + 0.02, z), 0.012, 0.09, 'Z', 8)
    for y in (0.55, -0.50):   # manillas de puerta (asa y placa)
        box(B, "Black", (sx * (SX + 0.004), y, 1.17), (0.006, 0.16, 0.05), 0.004)
        box(B, "Chrome", (sx * (SX + 0.018), y, 1.17), (0.016, 0.12, 0.022), 0.006)
    box(B, "Amber", (sx * (SX + 0.07), 1.62, 0.80), (0.012, 0.06, 0.03), 0.004)   # repetidor lateral del intermitente
    box(B, "Rubber", (sx * 0.88, WB_R - 0.62, 0.30), (0.16, 0.012, 0.26), 0.004)   # faldilla
cyl(B, "Black", (SX + 0.005, -1.62, 1.05), 0.055, 0.012, 'X', 20)      # tapón de combustible
cyl(B, "Chrome", (SX + 0.012, -1.62, 1.05), 0.042, 0.006, 'X', 20)
box(B, "Paint", (0, -2.18, 0.86), (1.70, 0.06, 0.80), 0.02)          # puerta trasera (parte baja)
box(B, "Paint", (0, 1.13, 0.98), (1.70, 0.06, 0.62), 0.02)           # mamparo
for x in (-0.80, 0.80):   # juntas del capó
    box(B, "Black", (x, 1.73, 1.296), (0.006, 1.06, 0.004), 0.0)
box(B, "Black", (0, 2.275, 1.296), (1.60, 0.006, 0.004), 0.0)
for z in (0.62, 1.12): box(B, "Metal", (-0.62, -2.225, z), (0.10, 0.03, 0.06), 0.006)   # bisagras del portón
box(B, "Chrome", (0.62, -2.222, 1.18), (0.10, 0.02, 0.025), 0.006)                     # manilla del portón
box(B, "Black", (0, -2.212, 0.88), (1.66, 0.004, 0.006), 0.0)
# ---------------------------------------------------------------- cabina
for sx in (-1, 1):
    box(B, "Paint", (sx * 0.83, 1.05, 1.62), (0.07, 0.07, 0.66), 0.015, (0.12, 0, 0))
    box(B, "Paint", (sx * 0.84, 0.05, 1.62), (0.07, 0.10, 0.66), 0.015)
    box(B, "Paint", (sx * 0.84, -1.15, 1.62), (0.07, 0.10, 0.66), 0.015)
    box(B, "Paint", (sx * 0.84, -2.14, 1.62), (0.07, 0.10, 0.66), 0.015)
    box(B, "Paint", (sx * 0.84, -0.55, 1.30), (0.07, 3.30, 0.06), 0.01)
    box(B, "Paint", (sx * 0.875, -0.55, 2.0), (0.025, 3.32, 0.03), 0.008)   # canalón del techo
    for y in (0.55, -0.50, -1.62):   # gomas de las ventanillas
        box(B, "Black", (sx * 0.852, y, 1.95), (0.016, 0.82 if y > -1 else 0.80, 0.02), 0.0)
box(B, "Paint", (0, -0.55, 1.97), (1.72, 3.30, 0.07), 0.025)
for y in (-1.6, -0.8, 0.0, 0.7): box(B, "Paint", (0, y, 2.008), (1.60, 0.05, 0.012), 0.005)   # nervios del techo
box(B, "Paint", (0, 1.06, 1.94), (1.70, 0.10, 0.08), 0.015)
box(B, "Paint", (0, 1.11, 1.33), (1.70, 0.10, 0.08), 0.015)
box(B, "Paint", (0, -2.18, 1.94), (1.70, 0.08, 0.08), 0.015)
box(B, "Paint", (0, -2.18, 1.33), (1.70, 0.08, 0.08), 0.015)
for sx in (-0.35, 0.35):   # limpiaparabrisas
    box(B, "Black", (sx, 1.128, 1.395), (0.46, 0.014, 0.016), 0.0, (0.12, -0.18 if sx > 0 else 0.18, 0))
    box(B, "Black", (sx + (0.24 if sx > 0 else -0.24), 1.14, 1.37), (0.03, 0.03, 0.03), 0.0)   # eje del limpiaparabrisas
# ---------------------------------------------------------------- delante: rejilla, faros, parachoques
box(B, "Black", (0, 2.31, 0.98), (1.10, 0.04, 0.42), 0.01)            # marco de la rejilla
for i in range(9): box(B, "Black", (0, 2.333, 0.79 + i * 0.048), (0.86, 0.02, 0.016), 0.003)   # lamas
for x in (-0.22, 0.22): box(B, "Black", (x, 2.335, 0.98), (0.016, 0.022, 0.40), 0.0)
box(B, "Chrome", (0, 2.338, 1.16), (0.20, 0.008, 0.045), 0.006)       # placa del emblema
for sx in (-1, 1):
    box(B, "Black", (sx * 0.68, 2.31, 0.98), (0.30, 0.05, 0.30), 0.02)    # marco del faro
    torus(B, "Chrome", (sx * 0.68, 2.342, 0.98), 0.112, 0.012, (math.pi / 2, 0, 0), 32, 6)   # aro cromado
    cyl("Lamps", "Lamp", (sx * 0.68, 2.345, 0.98), 0.103, 0.035, 'Y', 32, 0.0, 0.085)        # lente abombada
    box(B, "Amber", (sx * 0.80, 2.335, 0.835), (0.09, 0.02, 0.055), 0.006)   # intermitente
    box(B, "Plate", (sx * 0.60, 2.335, 0.835), (0.07, 0.02, 0.05), 0.006)    # luz de posición
    # detrás: grupo de pilotos (rojo de freno, ámbar, marcha atrás)
    box("Tails", "Tail", (sx * 0.80, -2.23, 0.98), (0.08, 0.03, 0.12), 0.008)
    box(B, "Amber", (sx * 0.80, -2.228, 0.86), (0.08, 0.03, 0.07), 0.008)
    box(B, "Plate", (sx * 0.80, -2.228, 0.77), (0.08, 0.03, 0.06), 0.008)
    box(B, "Black", (sx * 0.80, -2.215, 0.88), (0.10, 0.012, 0.30), 0.006)
box(B, "Black", (0, 2.42, 0.62), (1.85, 0.18, 0.16), 0.03)            # parachoques delantero
for sx in (-1, 1): box(B, "Black", (sx * 0.98, 2.36, 0.62), (0.16, 0.20, 0.15), 0.03, (0, 0, -sx * 0.5))   # puntas en ángulo
box(B, "Plate", (0, 2.513, 0.62), (0.52, 0.008, 0.115), 0.004)        # matrícula delantera
box(B, "Black", (0, 2.515, 0.62), (0.47, 0.006, 0.08), 0.0)
for sx in (-1, 1):
    box(B, "Metal", (sx * 0.62, 2.53, 0.56), (0.06, 0.06, 0.04), 0.008)                     # ganchos de remolque
    torus(B, "Metal", (sx * 0.62, 2.56, 0.53), 0.035, 0.010, (0, math.pi / 2, 0), 16, 6)
box(B, "Black", (0, -2.30, 0.60), (1.75, 0.14, 0.14), 0.03)           # parachoques trasero
box(B, "Black", (0, -2.40, 0.56), (0.30, 0.14, 0.05), 0.01)           # peldaño
box(B, "Metal", (0, -2.42, 0.61), (0.06, 0.08, 0.06), 0.006); sphere(B, "Chrome", (0, -2.45, 0.67), 0.025, 12)   # bola
box(B, "Plate", (0, -2.218, 0.72), (0.52, 0.008, 0.115), 0.004)       # matrícula trasera
box(B, "Black", (0, -2.221, 0.72), (0.47, 0.006, 0.08), 0.0)
# bastidor (se ve desde abajo / al volcar)
for x in (-0.45, 0.45): box(B, "Black", (x, 0.0, 0.33), (0.08, 4.4, 0.16), 0.01)
cyl(B, "Metal", (0.55, -1.0, 0.30), 0.03, 2.4, 'Y', 10)               # escape
# ---------------------------------------------------------------- espejos, snorkel, antena
for sx in (-1, 1):
    box(B, "Black", (sx * 0.98, 1.00, 1.42), (0.22, 0.025, 0.025), 0.006, (0, 0, 0), 1)     # brazo
    box(B, "Black", (sx * 1.07, 1.00, 1.50), (0.025, 0.025, 0.16), 0.006, (0, 0, 0), 1)
    box(B, "Black", (sx * 1.10, 0.98, 1.60), (0.07, 0.07, 0.20), 0.02)                      # carcasa
    box(B, "Chrome", (sx * 1.10, 0.944, 1.60), (0.055, 0.004, 0.18), 0.004)                 # luna (hacia atrás)
cyl(B, "Black", (0.93, 1.22, 1.62), 0.055, 1.00, 'Z', 16)              # snorkel: tubo por el pilar
cyl(B, "Black", (0.93, 1.30, 1.10), 0.058, 0.22, 'Y', 16)              # codo hacia la aleta
cyl(B, "Black", (0.93, 1.27, 2.15), 0.07, 0.14, 'Z', 16)               # toma de aire
box(B, "Black", (0.93, 1.36, 2.17), (0.15, 0.10, 0.12), 0.03)
for z in (1.45, 1.85): box(B, "Metal", (0.90, 1.18, z), (0.10, 0.04, 0.03), 0.006)      # abrazaderas
cyl(B, "Black", (-0.95, 1.95, 1.50), 0.006, 1.2, 'Z', 6)               # antena
# ---------------------------------------------------------------- baca con equipaje
for x in (-0.80, 0.80):
    box(B, "Black", (x, -0.55, 2.07), (0.04, 3.10, 0.04), 0.008)
    box(B, "Black", (x, -0.55, 2.17), (0.03, 3.10, 0.03), 0.006)       # baranda
    for y in (-1.9, -1.2, -0.5, 0.2, 0.8): box(B, "Black", (x, y, 2.12), (0.025, 0.025, 0.09), 0.0, (0, 0, 0), 1)
for y in (-1.9, -1.55, -1.2, -0.85, -0.5, -0.15, 0.2, 0.5, 0.8):
    box(B, "Black", (0, y, 2.07), (1.64, 0.035, 0.035), 0.006)
for y in (-1.9, -1.2, -0.5, 0.2, 0.8):
    for x in (-0.80, 0.80): box(B, "Black", (x, y, 2.02), (0.04, 0.04, 0.08), 0.0)
for k, x in enumerate((-0.55, -0.22)):   # dos bidones de 20 l tumbados
    box(B, "Jerry", (x, -1.62, 2.175), (0.31, 0.47, 0.16), 0.02)
    for d in (-1, 1): box(B, "Jerry", (x, -1.62, 2.257), (0.025, 0.50, 0.008), 0.0, (0, 0, d * 0.58), 1)   # «X» estampada
    cyl(B, "Metal", (x - 0.09, -1.37, 2.20), 0.025, 0.05, 'Y', 12)     # boca
    box(B, "Metal", (x, -1.865, 2.22), (0.14, 0.03, 0.03), 0.006)      # asas
for x in (0.20, 0.58):   # planchas de arena naranjas
    box(B, "Orange", (x, -1.1, 2.115), (0.32, 1.10, 0.035), 0.008)
    for k in range(9): box(B, "Orange", (x, -1.55 + k * 0.11, 2.137), (0.28, 0.025, 0.012), 0.0, (0, 0, 0), 1)
cyl(B, "Canvas", (0, -0.10, 2.20), 0.14, 1.40, 'X', 18)                # petate enrollado
for x in (-0.45, 0.45): torus(B, "Black", (x, -0.10, 2.20), 0.145, 0.010, (0, math.pi / 2, 0), 18, 4)   # correas
box(B, "Metal", (0.30, 0.55, 2.11), (0.22, 0.28, 0.012), 0.004)        # pala
box(B, "Canvas", (0.30, 0.95, 2.11), (0.035, 0.55, 0.03), 0.008)
# ---------------------------------------------------------------- cristales
G = "Glass"
box(G, "Glass", (0, 1.09, 1.63), (1.56, 0.012, 0.54), 0.0, (0.12, 0, 0))
for sx in (-1, 1):
    for (y0, y1) in ((0.12, 0.98), (-1.08, -0.02), (-2.07, -1.24)):
        box(G, "Glass", (sx * 0.845, (y0 + y1) / 2, 1.63), (0.012, y1 - y0, 0.56), 0.0)
box(G, "Glass", (0, -2.185, 1.63), (1.56, 0.012, 0.56), 0.0)
# ---------------------------------------------------------------- interior
I = "Interior"
def seat(x, y, back_tilt=-0.18, w=0.50):
    box(I, "Black", (x, y + 0.02, 0.55), (w - 0.06, 0.44, 0.12), 0.01)                  # bastidor
    box(I, "Cloth", (x, y, 0.70), (w - 0.10, 0.48, 0.12), 0.04)                         # cojín
    for sx in (-1, 1): box(I, "Cloth", (x + sx * (w / 2 - 0.04), y, 0.74), (0.08, 0.48, 0.16), 0.04)   # laterales
    box(I, "Black", (x, y, 0.765), (w - 0.14, 0.006, 0.004), 0.0)                       # costura
    by = y - 0.24
    box(I, "Cloth", (x, by, 1.08), (w - 0.06, 0.12, 0.62), 0.05, (back_tilt, 0, 0))     # respaldo
    for sx in (-1, 1): box(I, "Cloth", (x + sx * (w / 2 - 0.05), by + 0.02, 1.06), (0.09, 0.14, 0.56), 0.04, (back_tilt, 0, 0))
    for z in (0.95, 1.18): box(I, "Black", (x, by + 0.064 - (z - 1.08) * math.sin(back_tilt), z), (w - 0.18, 0.004, 0.006), 0.0, (back_tilt, 0, 0))
for sx in (-1, 1):
    seat(sx * 0.42, 0.20)
    box(I, "Cloth", (sx * 0.42, -0.06, 1.48), (0.28, 0.10, 0.18), 0.04)     # reposacabezas
    for dx in (-0.08, 0.08): cyl(I, "Chrome", (sx * 0.42 + dx, -0.05, 1.40), 0.007, 0.10, 'Z', 6)
    box(I, "Cloth", (sx * 0.825, 0.55, 0.92), (0.03, 0.80, 0.55), 0.01)     # guarnecido de puerta
    box(I, "Black", (sx * 0.81, 0.55, 1.02), (0.03, 0.30, 0.04), 0.01)       # tirador
    box(I, "Cloth", (sx * 0.825, -0.50, 0.92), (0.03, 0.90, 0.55), 0.01)
    box(I, "Cloth", (sx * 0.40, 1.02, 1.92), (0.42, 0.20, 0.015), 0.006, (0.2, 0, 0))   # parasol
box(I, "Cloth", (0, -0.95, 0.72), (1.40, 0.50, 0.14), 0.04)                 # banco trasero
box(I, "Black", (0, -0.95, 0.795), (1.30, 0.006, 0.004), 0.0)
box(I, "Cloth", (0, -1.20, 1.06), (1.40, 0.12, 0.58), 0.05, (-0.15, 0, 0))
box(I, "Rubber", (0, -1.70, 0.475), (1.62, 0.90, 0.01), 0.0)                 # alfombra de la zona de carga
for sx in (-0.42, 0.42): box(I, "Rubber", (sx, 0.62, 0.475), (0.48, 0.52, 0.01), 0.004)   # alfombrillas
# salpicadero
box(I, "Black", (0, 0.92, 1.20), (1.60, 0.32, 0.20), 0.04)
box(I, "Cloth", (0, 0.98, 1.315), (1.62, 0.30, 0.04), 0.015)                 # cubierta acolchada
box(I, "Black", (-0.42, 0.80, 1.27), (0.36, 0.10, 0.16), 0.02)               # binnacle de relojes
for k, (gx, gz, gr) in enumerate(((-0.50, 1.28, 0.045), (-0.34, 1.28, 0.045), (-0.42, 1.21, 0.026), (-0.31, 1.21, 0.022))):
    cyl(I, "Chrome", (gx, 0.748, gz), gr + 0.006, 0.012, 'Y', 24)            # aro
    cyl(I, "Black", (gx, 0.743, gz), gr, 0.006, 'Y', 24)                     # esfera
    for t in range(10):   # marcas blancas
        a = math.radians(-135 + 27 * t)
        box(I, "Plate", (gx + math.sin(a) * gr * 0.8, 0.739, gz + math.cos(a) * gr * 0.8), (0.003, 0.002, gr * 0.22), 0.0, (0, a, 0), 1)
    box(I, "Amber", (gx + 0.01, 0.738, gz + 0.01), (0.002, 0.002, gr * 0.75), 0.0, (0, -0.8 + k * 0.5, 0), 1)   # aguja
box(I, "Black", (0, 0.86, 1.13), (0.36, 0.18, 0.14), 0.015)                  # consola central
box(I, "Black", (0, 0.770, 1.15), (0.20, 0.01, 0.06), 0.004)                 # radio
for x in (-0.07, 0.07): cyl(I, "Chrome", (x, 0.762, 1.15), 0.012, 0.012, 'Y', 12)
for x in (-0.65, -0.12, 0.12, 0.65):   # rejillas de aireación
    for k in range(4): box(I, "Black", (x, 0.765, 1.24 + k * 0.016), (0.11, 0.012, 0.006), 0.0)
box(I, "Black", (0.42, 0.765, 1.18), (0.36, 0.006, 0.14), 0.0)               # junta de la guantera
box(I, "Black", (0, 0.55, 0.62), (0.22, 0.60, 0.22), 0.03)                   # túnel
cyl(I, "Metal", (0.02, 0.50, 0.86), 0.010, 0.30, 'Z', 8); sphere(I, "Black", (0.02, 0.50, 1.02), 0.028, 12)   # palanca de cambios
cyl(I, "Metal", (0.08, 0.44, 0.82), 0.008, 0.24, 'Z', 8); sphere(I, "Amber", (0.08, 0.44, 0.95), 0.020, 10)   # reductora
box(I, "Black", (0, 0.10, 0.80), (0.05, 0.30, 0.04), 0.01, (0.35, 0, 0))     # freno de mano
for k, px in enumerate((-0.50, -0.42, -0.32)): box(I, "Metal", (px, 0.95, 0.62), (0.06 if k < 2 else 0.09, 0.02, 0.08), 0.006, (0.5, 0, 0))   # pedales
box(I, "Black", (0, 1.02, 1.86), (0.20, 0.02, 0.06), 0.01); box(I, "Chrome", (0, 1.012, 1.86), (0.18, 0.004, 0.045), 0.004)   # retrovisor
# columna de dirección (fija) alineada con el volante
d = Vector((0, math.sin(math.radians(62)), -math.cos(math.radians(62))))   # hacia delante y abajo, dentro del salpicadero
cpos = Vector((-0.42, 0.66, 1.25)) + d * 0.2
cyl(I, "Black", tuple(cpos), 0.032, 0.32, 'Z', 12, 0.0, None, (math.radians(62), 0, 0))
# volante aparte (gira): aro, tres radios, bocina
torus("Steering", "Black", (0, 0, 0), 0.19, 0.021, (0, 0, 0), 40, 10)
cyl("SteeringHub", "Black", (0, 0, 0.01), 0.055, 0.05, 'Z', 16)
cyl("SteeringHub", "Chrome", (0, 0, 0.037), 0.025, 0.004, 'Z', 16)
for a in (0, 2.1, 4.2): box("SteeringHub", "Black", (math.cos(a) * 0.11, math.sin(a) * 0.11, 0), (0.17, 0.035, 0.014), 0.006, (0, 0, a))
# ---------------------------------------------------------------- rueda: neumático de tacos + llanta de acero; eje = X, centrada
def wheel(group):
    prof = [(0.25, -0.125), (0.29, -0.132), (0.33, -0.136), (0.36, -0.132), (0.383, -0.118), (0.395, -0.095), (0.398, -0.04),
            (0.398, 0.04), (0.395, 0.095), (0.383, 0.118), (0.36, 0.132), (0.33, 0.136), (0.29, 0.132), (0.25, 0.125)]
    bm = bmesh.new(); seg = 56; rings = []
    for k in range(seg):
        a = 2 * math.pi * k / seg; ring = []
        for (r, x) in prof: ring.append(bm.verts.new((x, r * math.cos(a), r * math.sin(a))))
        rings.append(ring)
    for k in range(seg):
        A, Bn = rings[k], rings[(k + 1) % seg]
        for i in range(len(prof) - 1): bm.faces.new((A[i], A[i + 1], Bn[i + 1], Bn[i]))
    me = bpy.data.meshes.new("tyre"); bm.to_mesh(me); bm.free()
    t = bpy.data.objects.new(group, me); sc.collection.objects.link(t); t.data.materials.append(M["Rubber"]); parts.setdefault(group, []).append(t)
    P = 32
    for k in range(P):   # tacos en tres filas al tresbolillo; los de los hombros bajan por el flanco
        a = 2 * math.pi * k / P; ca, sa = math.cos(a), math.sin(a); rot = (a, 0, 0)
        box(group, "Rubber", (0.0 + (0.012 if k % 2 else -0.012), 0.413 * ca, 0.413 * sa), (0.055, 0.045, 0.03), 0.0, rot, 1)
        ao = a + math.pi / P; co, so = math.cos(ao), math.sin(ao)
        for sxx in (-1, 1):
            box(group, "Rubber", (sxx * 0.074, 0.411 * co, 0.411 * so), (0.06, 0.05, 0.028), 0.0, (ao, 0, 0), 1)
            box(group, "Rubber", (sxx * 0.124, 0.382 * ca, 0.382 * sa), (0.03, 0.05, 0.05), 0.0, rot, 1)   # taco del hombro
    for k in range(44):   # letras en relieve del flanco exterior
        if k % 11 in (9, 10): continue
        a = 2 * math.pi * k / 44
        box(group, "Rubber", (0.139, 0.315 * math.cos(a), 0.315 * math.sin(a)), (0.006, 0.018, 0.026 if k % 3 else 0.02), 0.0, (a, 0, 0), 1)
    torus(group, "Rubber", (0.137, 0, 0), 0.355, 0.004, (0, math.pi / 2, 0), 48, 4)
    cyl(group, "Rim", (0, 0, 0), 0.252, 0.20, 'X', 32, 0.008)          # llanta de acero
    torus(group, "Rim", (0.10, 0, 0), 0.245, 0.012, (0, math.pi / 2, 0), 32, 6)   # pestaña
    cyl(group, "Rim", (0.095, 0, 0), 0.17, 0.03, 'X', 32, 0.01, 0.14)   # disco abombado
    for k in range(6):
        a = 2 * math.pi * k / 6 + math.pi / 6
        cyl(group, "Black", (0.104, 0.205 * math.cos(a), 0.205 * math.sin(a)), 0.024, 0.008, 'X', 12)   # ventanas de la llanta
        a2 = 2 * math.pi * k / 5 if k < 5 else None
    for k in range(5):
        a = 2 * math.pi * k / 5
        cyl(group, "Metal", (0.118, 0.10 * math.cos(a), 0.10 * math.sin(a)), 0.014, 0.02, 'X', 6)   # tuercas
    cyl(group, "Black", (0.115, 0, 0), 0.055, 0.03, 'X', 16, 0.006)     # tapacubos del buje (fuera)
    cyl(group, "Metal", (0.10, 0.22, 0.05), 0.005, 0.03, 'X', 6)        # válvula
wheel("Wheel")
wheel("Spare")
bpy.context.view_layer.update()   # matrix_world al día (si no, las piezas pierden su propia rotación)
for o in parts["Spare"]:
    o.matrix_world = Matrix.Translation((0, -2.43, 1.15)) @ Matrix.Rotation(-math.pi / 2, 4, 'Z') @ o.matrix_world
box(B, "Black", (0, -2.26, 1.15), (0.14, 0.10, 0.14), 0.01)            # soporte de la rueda de repuesto
# ---------------------------------------------------------------- unir grupos
def join(name, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join()
    o = bpy.context.active_object; o.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o
body = join("DCar_Body", parts["Body"] + parts["Spare"])
glass = join("DCar_Glass", parts["Glass"]); interior = join("DCar_Interior", parts["Interior"])
lamps = join("DCar_Lamps", parts["Lamps"]); tails = join("DCar_Tails", parts["Tails"])
wheelo = join("DCar_Wheel", parts["Wheel"])
steer = join("DCar_Steering", parts["Steering"] + parts["SteeringHub"])
steer.matrix_world = Matrix.Translation((-0.42, 0.66, 1.25)) @ Matrix.Rotation(math.radians(62), 4, 'X') @ steer.matrix_world   # cara hacia el conductor, arriba inclinada hacia delante
for o in (body, glass, interior, lamps, tails, wheelo, steer):
    for p in o.data.polygons: p.use_smooth = False
# ---------------------------------------------------------------- desgaste horneado (Cycles): polvo, chorretones, óxido, desconchones
def weather(m, dust_amt, rust_amt, chip_amt):
    nt = m.node_tree; N = nt.nodes; L = nt.links; bsdf = N["Principled BSDF"]
    base = m["base"]
    geo = N.new("ShaderNodeNewGeometry"); sep = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], sep.inputs[0])
    nrm = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Normal"], nrm.inputs[0])
    def noise(scale, detail=6.0, vec=None):
        n = N.new("ShaderNodeTexNoise"); n.inputs["Scale"].default_value = scale; n.inputs["Detail"].default_value = detail
        L.new(vec if vec is not None else geo.outputs["Position"], n.inputs["Vector"]); return n.outputs["Fac"]
    def mr(v, a, b, c=0.0, d=1.0):
        n = N.new("ShaderNodeMapRange"); n.clamp = True; L.new(v, n.inputs["Value"])
        n.inputs["From Min"].default_value = a; n.inputs["From Max"].default_value = b; n.inputs["To Min"].default_value = c; n.inputs["To Max"].default_value = d
        return n.outputs[0]
    def op(o, a, b):
        n = N.new("ShaderNodeMath"); n.operation = o; n.use_clamp = True
        if isinstance(a, float): n.inputs[0].default_value = a
        else: L.new(a, n.inputs[0])
        if isinstance(b, float): n.inputs[1].default_value = b
        else: L.new(b, n.inputs[1])
        return n.outputs[0]
    # chorretones: ruido estirado en vertical
    vs = N.new("ShaderNodeVectorMath"); vs.operation = 'MULTIPLY'; L.new(geo.outputs["Position"], vs.inputs[0]); vs.inputs[1].default_value = (9.0, 9.0, 0.7)
    streak = mr(noise(1.0, 4.0, vs.outputs[0]), 0.45, 0.75)
    low = mr(sep.outputs["Z"], 0.30, 1.20, 1.0, 0.0)
    top = mr(nrm.outputs["Z"], 0.5, 0.95)
    blot = mr(noise(2.5, 8.0), 0.35, 0.70, 0.35, 1.0)
    dmask = op('MULTIPLY', op('ADD', op('ADD', op('MULTIPLY', low, 0.85), op('MULTIPLY', top, 0.55)), op('MULTIPLY', streak, 0.30)), blot)
    dmask = op('MULTIPLY', mr(dmask, 0.15, 0.95), dust_amt)
    # óxido: cantos vivos (pointiness) y abajo, en manchas
    pt = N.new("ShaderNodeNewGeometry")
    edge = mr(pt.outputs["Pointiness"], 0.52, 0.62)
    rspot = mr(noise(7.0, 10.0), 0.55, 0.70)
    rmask = op('MULTIPLY', op('MULTIPLY', op('ADD', op('MULTIPLY', edge, 0.8), op('MULTIPLY', low, 0.5)), rspot), rust_amt)
    # desconchones/arañazos: ruido fino umbral (deja ver imprimación clara)
    vs2 = N.new("ShaderNodeVectorMath"); vs2.operation = 'MULTIPLY'; L.new(geo.outputs["Position"], vs2.inputs[0]); vs2.inputs[1].default_value = (40.0, 6.0, 40.0)
    chip = op('MULTIPLY', mr(noise(1.0, 2.0, vs2.outputs[0]), 0.70, 0.725), chip_amt)
    var = N.new("ShaderNodeMixRGB"); var.inputs["Color1"].default_value = (base[0] * 0.80, base[1] * 0.80, base[2] * 0.80, 1); var.inputs["Color2"].default_value = (base[0] * 1.15, base[1] * 1.15, base[2] * 1.15, 1)
    L.new(noise(25.0, 3.0), var.inputs["Fac"])
    c1 = N.new("ShaderNodeMixRGB"); L.new(chip, c1.inputs["Fac"]); L.new(var.outputs[0], c1.inputs["Color1"]); c1.inputs["Color2"].default_value = (0.32, 0.31, 0.28, 1)
    c2 = N.new("ShaderNodeMixRGB"); L.new(rmask, c2.inputs["Fac"]); L.new(c1.outputs[0], c2.inputs["Color1"]); c2.inputs["Color2"].default_value = (0.13, 0.045, 0.014, 1)
    dc = N.new("ShaderNodeMixRGB"); dc.inputs["Color1"].default_value = (0.42, 0.31, 0.19, 1); dc.inputs["Color2"].default_value = (0.55, 0.43, 0.28, 1); L.new(noise(14.0, 4.0), dc.inputs["Fac"])
    c3 = N.new("ShaderNodeMixRGB"); L.new(dmask, c3.inputs["Fac"]); L.new(c2.outputs[0], c3.inputs["Color1"]); L.new(dc.outputs[0], c3.inputs["Color2"])
    L.new(c3.outputs[0], bsdf.inputs["Base Color"])
WEAR = {"Paint": (0.9, 0.55, 0.25), "Black": (0.85, 0.25, 0.0), "Metal": (0.6, 0.7, 0.0), "Chrome": (0.35, 0.3, 0.0), "Rubber": (0.9, 0.0, 0.0),
        "Rim": (0.8, 0.6, 0.3), "Cloth": (0.35, 0.0, 0.0), "Amber": (0.3, 0.0, 0.0), "Plate": (0.45, 0.2, 0.2), "Orange": (0.55, 0.0, 0.3),
        "Canvas": (0.5, 0.0, 0.0), "Jerry": (0.6, 0.4, 0.5)}
for k, (dd, rr, ch) in WEAR.items(): weather(M[k], dd, rr, ch)
sc.render.engine = 'CYCLES'; sc.cycles.samples = 4; sc.cycles.device = 'CPU'
sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False
def bake(obj, fname, res, angle=1.15):
    bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = obj; obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.smart_project(angle_limit=angle, island_margin=0.003); bpy.ops.object.mode_set(mode='OBJECT')
    img = bpy.data.images.new(fname, res, res)
    added = []
    for m in obj.data.materials:
        tn = m.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img; m.node_tree.nodes.active = tn; added.append((m, tn))
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=4)
    img.filepath_raw = os.path.join(OUT, fname + ".png"); img.file_format = 'PNG'; img.save()
    for m, tn in added: m.node_tree.nodes.remove(tn)
bake(body, "DCar_paint", 2048)
bake(wheelo, "DCar_wheel", 1024)
bake(interior, "DCar_interior", 1024)
# ---------------------------------------------------------------- exportar
bpy.ops.object.select_all(action='DESELECT')
for o in (body, glass, interior, lamps, tails, wheelo, steer): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "DCar.fbx"), use_selection=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True, bake_space_transform=True, path_mode='STRIP')
tri = lambda o: sum(len(p.vertices) - 2 for p in o.data.polygons)
print("CAR", {o.name: tri(o) for o in (body, glass, interior, lamps, tails, wheelo, steer)}, "mats", [m.name for m in body.data.materials], flush=True)
