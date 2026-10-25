# v7.8.0: todoterreno 4x4 del Nivel 114 hecho a mano en Blender (sin modelos de terceros): estilo «Defender» de caja,
# carrocería con cabina hueca (se entra y se ve el interior), capó, aletas, parachoques, rejilla, faros redondos, snorkel,
# baca, rueda de repuesto, espejos; interior con asientos, salpicadero, volante y palanca; rueda con taco (una malla, 4 copias en Unity).
# Pintura con polvo y óxido horneada en Cycles (albedo 1024). Salida: Resources/Desert/Car/DCar.fbx (+ texturas).
# Ejes: delante = +Y de Blender, arriba = +Z. Medidas reales: 4,55 m de largo, 1,79 m de ancho, 2,0 m de alto, batalla 2,80 m.
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix
OUT = sys.argv[sys.argv.index("--") + 1]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

def mat(name, col, rough=0.6, metal=0.0, emit=None, alpha=1.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*col, 1); b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    if emit: b.inputs["Emission Color"].default_value = (*emit, 1); b.inputs["Emission Strength"].default_value = 1.0
    if alpha < 1: b.inputs["Alpha"].default_value = alpha
    return m
M = {
 "Paint": mat("Paint", (0.62, 0.55, 0.42), 0.55, 0.1),
 "Black": mat("Black", (0.035, 0.035, 0.035), 0.75),
 "Metal": mat("Metal", (0.55, 0.55, 0.53), 0.35, 0.9),
 "Glass": mat("Glass", (0.25, 0.3, 0.3), 0.05, 0.0, None, 0.3),
 "Cloth": mat("Cloth", (0.16, 0.14, 0.12), 0.95),
 "Lamp": mat("Lamp", (0.9, 0.9, 0.85), 0.1, 0.0, (1, 0.95, 0.8)),
 "Tail": mat("Tail", (0.6, 0.05, 0.03), 0.2, 0.0, (0.8, 0.05, 0.02)),
 "Rubber": mat("Rubber", (0.03, 0.03, 0.03), 0.92),
 "Rim": mat("Rim", (0.3, 0.3, 0.29), 0.45, 0.8),
}
parts = {}   # nombre de objeto -> lista de objetos a unir

def box(group, mname, c, s, bev=0.02, rot=(0, 0, 0)):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * s[0], v.co.y * s[1], v.co.z * s[2]))
    if bev > 0: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bev, segments=2, affect='EDGES', profile=0.5)
    me = bpy.data.meshes.new(group); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(group, me); sc.collection.objects.link(o)
    o.location = c; o.rotation_euler = rot; o.data.materials.append(M[mname])
    parts.setdefault(group, []).append(o); return o

def cyl(group, mname, c, r, depth, axis='X', verts=24, bev=0.0, r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r2 if r2 is not None else r, depth=depth)
    if bev > 0: bmesh.ops.bevel(bm, geom=[e for e in bm.edges if not e.is_wire], offset=bev, segments=2, affect='EDGES')
    me = bpy.data.meshes.new(group); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(group, me); sc.collection.objects.link(o); o.location = c
    o.rotation_euler = (0, math.pi / 2, 0) if axis == 'X' else (math.pi / 2, 0, 0) if axis == 'Y' else (0, 0, 0)
    o.data.materials.append(M[mname]); parts.setdefault(group, []).append(o); return o

W = 1.79 / 2; FR = 2.30; RR = -2.25; WB_F = 1.40; WB_R = -1.40; WR = 0.40
# --- carrocería (bañera inferior hueca: suelo, laterales, frontal de cabina y trasera)
B = "Body"
box(B, "Paint", (0, 1.73, 0.95), (1.70, 1.15, 0.62), 0.03)           # vano motor / capó (macizo)
box(B, "Paint", (0, 1.73, 1.275), (1.66, 1.10, 0.04), 0.02)          # chapa del capó, algo más alta
box(B, "Paint", (0, -0.55, 0.42), (1.70, 3.30, 0.10), 0.02)          # suelo de la cabina y caja
for sx in (-1, 1):
    box(B, "Paint", (sx * 0.86, -0.55, 0.86), (0.06, 3.30, 0.80), 0.02)   # laterales hasta la cintura
    box(B, "Paint", (sx * 0.96, WB_F, 0.86), (0.22, 1.10, 0.10), 0.03)    # aleta delantera (plana, típica)
    box(B, "Black", (sx * 0.95, WB_F, 0.62), (0.20, 1.05, 0.30), 0.04)    # paso de rueda negro
    box(B, "Black", (sx * 0.95, WB_R, 0.62), (0.20, 1.05, 0.30), 0.04)
    box(B, "Black", (sx * 0.90, 0.0, 0.40), (0.10, 1.60, 0.06), 0.01)     # estribo
box(B, "Paint", (0, -2.18, 0.86), (1.70, 0.06, 0.80), 0.02)          # puerta trasera (parte baja)
box(B, "Paint", (0, 1.13, 0.98), (1.70, 0.06, 0.62), 0.02)           # mamparo entre motor y cabina
# --- cabina: pilares, techo, marco de parabrisas
for sx in (-1, 1):
    box(B, "Paint", (sx * 0.83, 1.05, 1.62), (0.07, 0.07, 0.66), 0.015, (0.12, 0, 0))   # pilar A (algo inclinado)
    box(B, "Paint", (sx * 0.84, 0.05, 1.62), (0.07, 0.10, 0.66), 0.015)                  # pilar B
    box(B, "Paint", (sx * 0.84, -1.15, 1.62), (0.07, 0.10, 0.66), 0.015)                 # pilar C
    box(B, "Paint", (sx * 0.84, -2.14, 1.62), (0.07, 0.10, 0.66), 0.015)                 # pilar D
    box(B, "Paint", (sx * 0.84, -0.55, 1.30), (0.07, 3.30, 0.06), 0.01)                  # línea de cintura
box(B, "Paint", (0, -0.55, 1.97), (1.72, 3.30, 0.07), 0.025)          # techo
box(B, "Paint", (0, 1.06, 1.94), (1.70, 0.10, 0.08), 0.015)           # marco superior del parabrisas
box(B, "Paint", (0, 1.11, 1.33), (1.70, 0.10, 0.08), 0.015)           # marco inferior
box(B, "Paint", (0, -2.18, 1.94), (1.70, 0.08, 0.08), 0.015)
box(B, "Paint", (0, -2.18, 1.33), (1.70, 0.08, 0.08), 0.015)
# --- delante: rejilla, faros, parachoques, intermitentes
box(B, "Black", (0, 2.31, 0.98), (1.10, 0.04, 0.42), 0.01)            # rejilla
for i in range(7): box(B, "Metal", (0, 2.335, 0.80 + i * 0.06), (1.04, 0.015, 0.018), 0.0)
for sx in (-1, 1):
    box(B, "Black", (sx * 0.68, 2.31, 0.98), (0.30, 0.05, 0.30), 0.02)    # marco del faro
    cyl("Lamps", "Lamp", (sx * 0.68, 2.345, 0.98), 0.105, 0.03, 'Y', 24)   # faro redondo
    box("Lamps", "Lamp", (sx * 0.80, 2.33, 0.84), (0.08, 0.02, 0.05), 0.005)  # intermitente
    box("Tails", "Tail", (sx * 0.80, -2.23, 0.95), (0.08, 0.03, 0.16), 0.008) # pilotos traseros
box(B, "Black", (0, 2.42, 0.62), (1.85, 0.18, 0.16), 0.03)            # parachoques delantero (barra)
box(B, "Black", (0, -2.30, 0.60), (1.75, 0.14, 0.14), 0.03)           # parachoques trasero
for sx in (-1, 1): box(B, "Metal", (sx * 0.35, 2.50, 0.62), (0.08, 0.06, 0.08), 0.01)  # ganchos de remolque
# --- espejos, snorkel, baca, rueda de repuesto, manillas
for sx in (-1, 1):
    box(B, "Black", (sx * 1.00, 0.98, 1.50), (0.22, 0.03, 0.03), 0.005)
    box(B, "Black", (sx * 1.10, 0.98, 1.55), (0.05, 0.06, 0.16), 0.01)
    box(B, "Metal", (sx * 0.875, 0.55, 1.18), (0.02, 0.12, 0.025), 0.004)   # manilla
cyl(B, "Black", (0.93, 1.25, 1.55), 0.055, 1.25, 'Z', 16)               # snorkel (lado derecho)
box(B, "Black", (0.93, 1.30, 2.18), (0.13, 0.20, 0.10), 0.02)
for x in (-0.80, 0.80):
    box(B, "Black", (x, -0.55, 2.07), (0.04, 3.10, 0.04), 0.008)        # baca: largueros
for y in (-1.9, -1.2, -0.5, 0.2, 0.8):
    box(B, "Black", (0, y, 2.07), (1.64, 0.04, 0.04), 0.008)            # baca: travesaños
    for x in (-0.80, 0.80): box(B, "Black", (x, y, 2.02), (0.04, 0.04, 0.08), 0.0)
box(B, "Black", (0.2, -0.9, 2.16), (0.55, 0.40, 0.14), 0.03)          # bulto sobre la baca (petate)
# --- cristales (finos, en los huecos de la cabina)
G = "Glass"
box(G, "Glass", (0, 1.09, 1.63), (1.56, 0.012, 0.54), 0.0, (0.12, 0, 0))   # parabrisas
for sx in (-1, 1):
    for (y0, y1) in ((0.12, 0.98), (-1.08, -0.02), (-2.07, -1.24)):
        box(G, "Glass", (sx * 0.845, (y0 + y1) / 2, 1.63), (0.012, y1 - y0, 0.56), 0.0)
box(G, "Glass", (0, -2.185, 1.63), (1.56, 0.012, 0.56), 0.0)
# --- interior
I = "Interior"
for sx in (-1, 1):
    box(I, "Cloth", (sx * 0.42, 0.20, 0.72), (0.50, 0.50, 0.14), 0.04)       # asiento delantero: banqueta
    box(I, "Cloth", (sx * 0.42, -0.04, 1.08), (0.50, 0.12, 0.62), 0.05, (-0.18, 0, 0))   # respaldo
    box(I, "Cloth", (sx * 0.42, -0.06, 1.48), (0.30, 0.10, 0.18), 0.04)      # reposacabezas
box(I, "Cloth", (0, -0.95, 0.72), (1.40, 0.50, 0.14), 0.04)                 # banco trasero
box(I, "Cloth", (0, -1.20, 1.06), (1.40, 0.12, 0.58), 0.05, (-0.15, 0, 0))
box(I, "Black", (0, 0.92, 1.20), (1.60, 0.32, 0.20), 0.04)                  # salpicadero
box(I, "Black", (-0.40, 0.80, 1.22), (0.30, 0.04, 0.12), 0.01)             # cuadro
box(I, "Black", (0, 0.55, 0.62), (0.22, 0.60, 0.22), 0.03)                  # túnel central
cyl(I, "Metal", (0.02, 0.48, 0.86), 0.012, 0.28, 'Z', 8)                   # palanca
cyl(I, "Black", (0.02, 0.48, 1.00), 0.03, 0.04, 'Z', 12)
# volante aparte (gira)
bpy.ops.mesh.primitive_torus_add(major_radius=0.19, minor_radius=0.018, major_segments=32, minor_segments=8, location=(0, 0, 0))
sw = bpy.context.active_object; sw.name = "Steering"; sw.data.materials.append(M["Black"])
cyl("SteeringHub", "Black", (0, 0, 0), 0.045, 0.05, 'Z', 12)
for a in (0, 2.1, 4.2): box("SteeringHub", "Black", (math.cos(a) * 0.1, math.sin(a) * 0.1, 0), (0.19, 0.03, 0.015), 0.0, (0, 0, a))
# --- rueda: neumático de taco (perfil de revolución) + llanta; eje = X de Blender, centrada en el origen
def wheel(group):
    prof = [(0.25, -0.13), (0.33, -0.135), (0.385, -0.12), (0.40, -0.08), (0.40, 0.08), (0.385, 0.12), (0.33, 0.135), (0.25, 0.13)]
    bm = bmesh.new(); seg = 40; rings = []
    for k in range(seg):
        a = 2 * math.pi * k / seg; ring = []
        for (r, x) in prof: ring.append(bm.verts.new((x, r * math.cos(a), r * math.sin(a))))
        rings.append(ring)
    for k in range(seg):
        A, Bn = rings[k], rings[(k + 1) % seg]
        for i in range(len(prof) - 1): bm.faces.new((A[i], A[i + 1], Bn[i + 1], Bn[i]))
    me = bpy.data.meshes.new("tyre"); bm.to_mesh(me); bm.free()
    t = bpy.data.objects.new(group, me); sc.collection.objects.link(t); t.data.materials.append(M["Rubber"]); parts.setdefault(group, []).append(t)
    for k in range(30):   # tacos alternos
        a = 2 * math.pi * k / 30
        for sxx in (-1, 1):
            x = sxx * 0.05 + (0.012 if k % 2 else -0.012)
            b = box(group, "Rubber", (x, 0.404 * math.cos(a), 0.404 * math.sin(a)), (0.085, 0.05, 0.016), 0.004, (a, 0, 0))
    cyl(group, "Rim", (0, 0, 0), 0.24, 0.20, 'X', 28, 0.01)           # llanta de acero
    cyl(group, "Black", (0.105, 0, 0), 0.07, 0.02, 'X', 12)           # buje
    for k in range(5):
        a = 2 * math.pi * k / 5
        cyl(group, "Black", (0.101, 0.15 * math.cos(a), 0.15 * math.sin(a)), 0.03, 0.01, 'X', 10)   # agujeros (oscuros)
wheel("Wheel")
# rueda de repuesto en la puerta trasera (parte de la carrocería)
sp = []; before = set(bpy.data.objects)
wheel("Spare")
for o in parts["Spare"]:
    o.matrix_world = Matrix.Translation((0, -2.43, 1.15)) @ Matrix.Rotation(math.pi / 2, 4, 'Z') @ o.matrix_world
# --- unir grupos
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
steer = join("DCar_Steering", [sw] + parts["SteeringHub"])
# volante: delante del asiento izquierdo, inclinado
steer.matrix_world = Matrix.Translation((-0.42, 0.66, 1.30)) @ Matrix.Rotation(math.radians(-62), 4, 'X') @ steer.matrix_world
bpy.context.view_layer.objects.active = steer; bpy.ops.object.select_all(action='DESELECT'); steer.select_set(True)
# el pivote del volante queda en su centro (para girarlo en Unity)
for o in (body, glass, interior, lamps, tails, wheelo):
    for p in o.data.polygons: p.use_smooth = False
# --- pintura con polvo y óxido: UV automático + horneado (Cycles) de un material con ruido
bpy.context.view_layer.objects.active = body; bpy.ops.object.select_all(action='DESELECT'); body.select_set(True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.004); bpy.ops.object.mode_set(mode='OBJECT')
pm = M["Paint"]; nt = pm.node_tree; N = nt.nodes; L = nt.links; bsdf = N["Principled BSDF"]
geo = N.new("ShaderNodeNewGeometry"); sep = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], sep.inputs[0])
noise = N.new("ShaderNodeTexNoise"); noise.inputs["Scale"].default_value = 3.0; noise.inputs["Detail"].default_value = 8.0
L.new(geo.outputs["Position"], noise.inputs["Vector"])
fine = N.new("ShaderNodeTexNoise"); fine.inputs["Scale"].default_value = 60.0; fine.inputs["Detail"].default_value = 4.0; L.new(geo.outputs["Position"], fine.inputs["Vector"])
# polvo: más abajo y en las superficies horizontales
dz = N.new("ShaderNodeMapRange"); L.new(sep.outputs["Z"], dz.inputs["Value"]); dz.inputs["From Min"].default_value = 0.35; dz.inputs["From Max"].default_value = 1.25; dz.inputs["To Min"].default_value = 0.85; dz.inputs["To Max"].default_value = 0.15
up = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Normal"], up.inputs[0])
upm = N.new("ShaderNodeMath"); upm.operation = 'MULTIPLY'; L.new(up.outputs["Z"], upm.inputs[0]); upm.inputs[1].default_value = 0.45
dsum = N.new("ShaderNodeMath"); dsum.operation = 'ADD'; L.new(dz.outputs[0], dsum.inputs[0]); L.new(upm.outputs[0], dsum.inputs[1])
dn = N.new("ShaderNodeMath"); dn.operation = 'MULTIPLY'; L.new(dsum.outputs[0], dn.inputs[0]); L.new(noise.outputs["Fac"], dn.inputs[1])
dcl = N.new("ShaderNodeMath"); dcl.operation = 'MULTIPLY'; dcl.use_clamp = True; L.new(dn.outputs[0], dcl.inputs[0]); dcl.inputs[1].default_value = 1.5
paint = N.new("ShaderNodeMixRGB"); paint.inputs["Color1"].default_value = (0.36, 0.40, 0.33, 1); paint.inputs["Color2"].default_value = (0.47, 0.50, 0.42, 1)   # verde oliva gastado
L.new(fine.outputs["Fac"], paint.inputs["Fac"])
dust = N.new("ShaderNodeMixRGB"); L.new(dcl.outputs[0], dust.inputs["Fac"]); L.new(paint.outputs["Color"], dust.inputs["Color1"]); dust.inputs["Color2"].default_value = (0.70, 0.58, 0.42, 1)
rustn = N.new("ShaderNodeTexNoise"); rustn.inputs["Scale"].default_value = 9.0; rustn.inputs["Detail"].default_value = 10.0; L.new(geo.outputs["Position"], rustn.inputs["Vector"])
rr = N.new("ShaderNodeMapRange"); L.new(rustn.outputs["Fac"], rr.inputs["Value"]); rr.inputs["From Min"].default_value = 0.60; rr.inputs["From Max"].default_value = 0.68
rz = N.new("ShaderNodeMapRange"); L.new(sep.outputs["Z"], rz.inputs["Value"]); rz.inputs["From Min"].default_value = 0.45; rz.inputs["From Max"].default_value = 0.95; rz.inputs["To Min"].default_value = 1.0; rz.inputs["To Max"].default_value = 0.0
rm = N.new("ShaderNodeMath"); rm.operation = 'MULTIPLY'; rm.use_clamp = True; L.new(rr.outputs[0], rm.inputs[0]); L.new(rz.outputs[0], rm.inputs[1])
rust = N.new("ShaderNodeMixRGB"); L.new(rm.outputs[0], rust.inputs["Fac"]); L.new(dust.outputs["Color"], rust.inputs["Color1"]); rust.inputs["Color2"].default_value = (0.30, 0.15, 0.07, 1)
L.new(rust.outputs["Color"], bsdf.inputs["Base Color"])
img = bpy.data.images.new("DCar_paint", 1024, 1024); tex = N.new("ShaderNodeTexImage"); tex.image = img; N.active = tex
for m in body.data.materials:
    if m.name != "Paint":
        tn = m.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img; m.node_tree.nodes.active = tn
sc.render.engine = 'CYCLES'; sc.cycles.samples = 4; sc.cycles.device = 'CPU'
sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=4)
img.filepath_raw = os.path.join(OUT, "DCar_paint.png"); img.file_format = 'PNG'; img.save()
# quitar los nodos de horneado y la textura del resto de materiales (en Unity el color de cada ranura va aparte)
for m in body.data.materials:
    for n in list(m.node_tree.nodes):
        if n.type == 'TEX_IMAGE': m.node_tree.nodes.remove(n)
# --- exportar
bpy.ops.object.select_all(action='DESELECT')
for o in (body, glass, interior, lamps, tails, wheelo, steer): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "DCar.fbx"), use_selection=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True, bake_space_transform=True, path_mode='STRIP')
tri = lambda o: sum(len(p.vertices) - 2 for p in o.data.polygons)
print("CAR", {o.name: tri(o) for o in (body, glass, interior, lamps, tails, wheelo, steer)}, "mats", [m.name for m in body.data.materials], flush=True)
