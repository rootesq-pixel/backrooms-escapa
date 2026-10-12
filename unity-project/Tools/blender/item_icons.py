# Iconos realistas de los objetos del inventario: modela cada objeto con primitivas de Blender, materiales PBR
# y lo renderiza con Cycles (fondo transparente). Uso:
#   blender -b -P Tools/blender/item_icons.py -- <carpeta_salida> [tamaño]
import bpy, sys, math, os
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '/workspace/item-icons'
SIZE = int(argv[1]) if len(argv) > 1 else 512
os.makedirs(OUT, exist_ok=True)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 96
    try: sc.cycles.use_denoising = True
    except Exception: pass
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = SIZE
    sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA'
    sc.view_settings.view_transform = 'Filmic' if 'Filmic' in [i.identifier for i in sc.view_settings.bl_rna.properties['view_transform'].enum_items] else 'AgX'
    sc.view_settings.look = 'None'
    w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes['Background']; bg.inputs[0].default_value = (0.05, 0.05, 0.055, 1); bg.inputs[1].default_value = 0.6
    return sc

def mat(name, col, rough=0.5, metal=0.0, emit=None, estr=0.0, trans=0.0, ior=1.45, alpha=1.0, tex=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*col, 1)
    b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    if 'Transmission Weight' in b.inputs: b.inputs['Transmission Weight'].default_value = trans
    b.inputs['IOR'].default_value = ior
    if alpha < 1: b.inputs['Alpha'].default_value = alpha
    if emit is not None:
        b.inputs['Emission Color'].default_value = (*emit, 1); b.inputs['Emission Strength'].default_value = estr
    if tex == 'grime':
        # suciedad/arañazos: ruido sobre color y rugosidad
        n = nt.nodes.new('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = 60; n.inputs['Detail'].default_value = 10
        r = nt.nodes.new('ShaderNodeValToRGB'); r.color_ramp.elements[0].position = 0.45; r.color_ramp.elements[1].position = 0.75
        mx = nt.nodes.new('ShaderNodeMixRGB'); mx.blend_type = 'MULTIPLY'; mx.inputs[0].default_value = 0.35
        mx.inputs[1].default_value = (*col, 1)
        nt.links.new(n.outputs['Fac'], r.inputs['Fac']); nt.links.new(r.outputs['Color'], mx.inputs[2])
        nt.links.new(mx.outputs['Color'], b.inputs['Base Color'])
        mr = nt.nodes.new('ShaderNodeMapRange'); mr.inputs['To Min'].default_value = rough * 0.7; mr.inputs['To Max'].default_value = min(1, rough * 1.4)
        nt.links.new(n.outputs['Fac'], mr.inputs['Value']); nt.links.new(mr.outputs['Result'], b.inputs['Roughness'])
    return m

def obj_set(o, m, smooth=True):
    o.data.materials.append(m)
    if smooth:
        for p in o.data.polygons: p.use_smooth = True
    return o

def cyl(r, d, loc, m, verts=48, rot=(0, 0, 0), smooth=True):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d, location=loc, rotation=rot)
    o = bpy.context.object; obj_set(o, m, smooth)
    return o

def cone(r1, r2, d, loc, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(vertices=48, radius1=r1, radius2=r2, depth=d, location=loc, rotation=rot)
    o = bpy.context.object; obj_set(o, m); return o

def box(sx, sy, sz, loc, m, bevel=0.0, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.object; o.scale = (sx, sy, sz)
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        md = o.modifiers.new('B', 'BEVEL'); md.width = bevel; md.segments = 3
    obj_set(o, m, smooth=bevel > 0)
    return o

def text(s, size, loc, m, rot=(math.radians(90), 0, 0), extrude=0.0005, font=None, align='CENTER'):
    bpy.ops.object.text_add(location=loc, rotation=rot)
    o = bpy.context.object; o.data.body = s; o.data.size = size; o.data.extrude = extrude
    o.data.align_x = align; o.data.align_y = 'CENTER'
    if font and os.path.exists(font): o.data.font = bpy.data.fonts.load(font)
    o.data.materials.append(m)
    return o

def group(objs, name):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e)
    for o in objs: o.parent = e
    return e

def lights_camera(target=(0, 0, 0), dist=1.0, elev=22, azim=-35, ortho=None, fov=30):
    sc = bpy.context.scene
    t = Vector(target)
    az, el = math.radians(azim), math.radians(elev)
    pos = t + Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el))) * dist
    bpy.ops.object.camera_add(location=pos); cam = bpy.context.object; sc.camera = cam
    d = t - pos; cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens_unit = 'FOV'; cam.data.angle = math.radians(fov)
    if ortho: cam.data.type = 'ORTHO'; cam.data.ortho_scale = ortho
    def area(loc, energy, size, col=(1, 1, 1)):
        bpy.ops.object.light_add(type='AREA', location=loc); l = bpy.context.object
        l.data.energy = energy; l.data.size = size; l.data.color = col
        l.rotation_euler = (t - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    area(t + Vector((-1.2, -1.4, 1.6)) * dist, 60 * dist * dist, 1.2 * dist, (1.0, 0.95, 0.85))   # principal cálida
    area(t + Vector((1.6, 0.8, 0.6)) * dist, 35 * dist * dist, 0.8 * dist, (0.75, 0.85, 1.0))     # contra fría
    area(t + Vector((0.3, -1.6, -0.4)) * dist, 10 * dist * dist, 1.5 * dist)                      # relleno

def render(name):
    bpy.context.scene.render.filepath = os.path.join(OUT, name + '.png')
    bpy.ops.render.render(write_still=True)
    print('[icons] ' + name)

FONT_B = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
FONT_H = '/usr/share/fonts/truetype/sand-box/google/Courier Prime/CourierPrime-Bold.ttf'

# ---------------- Linterna ----------------
reset()
alu = mat('alu', (0.035, 0.035, 0.038), 0.32, 1.0, tex='grime')
knurl = mat('knurl', (0.02, 0.02, 0.022), 0.6, 0.9)
rubber = mat('rubber', (0.012, 0.012, 0.012), 0.85)
lens = mat('lens', (1, 0.97, 0.88), 0.05, emit=(1, 0.93, 0.75), estr=6)
refl = mat('refl', (0.9, 0.9, 0.9), 0.08, 1.0)
yel = mat('yel', (0.85, 0.62, 0.05), 0.45)
parts = []
R = (0, math.radians(90), 0)
parts.append(cyl(0.032, 0.30, (0, 0, 0), alu, rot=R))
for i in range(14): parts.append(cyl(0.0335, 0.006, (-0.11 + i * 0.012, 0, 0), knurl, rot=R))
parts.append(cone(0.034, 0.05, 0.07, (0.185, 0, 0), alu, rot=R))
parts.append(cyl(0.052, 0.035, (0.235, 0, 0), alu, rot=R))
parts.append(cyl(0.044, 0.004, (0.2535, 0, 0), lens, rot=R))
parts.append(cyl(0.035, 0.03, (-0.165, 0, 0), rubber, rot=R))
parts.append(box(0.022, 0.016, 0.012, (0.09, 0, 0.033), rubber, 0.004))
parts.append(box(0.012, 0.008, 0.006, (0.09, 0, 0.040), yel, 0.002))
g = group(parts, 'F'); g.rotation_euler = (0, math.radians(-18), math.radians(28))
lights_camera((0.04, 0, 0), 0.95, 18, -32, fov=30)
render('Flashlight')

# ---------------- Pila (dos pilas AA) ----------------
reset()
wrap = mat('wrap', (0.02, 0.02, 0.02), 0.35, 0.3, tex='grime')
copper = mat('copper', (0.75, 0.38, 0.12), 0.3, 1.0)
nick = mat('nick', (0.8, 0.8, 0.8), 0.25, 1.0)
white = mat('white', (0.85, 0.83, 0.78), 0.5)
def battery(x, y, rz):
    p = []
    p.append(cyl(0.0072, 0.030, (0, 0, -0.0105), copper))
    p.append(cyl(0.0073, 0.020, (0, 0, 0.014), wrap))
    p.append(cyl(0.0068, 0.0012, (0, 0, 0.0245), nick))
    p.append(cyl(0.0028, 0.0018, (0, 0, 0.0257), nick))
    p.append(cyl(0.0068, 0.0012, (0, 0, -0.0257), nick))
    t = text('1.5V', 0.0042, (0, -0.0074, 0.012), white, extrude=0.0002, font=FONT_B)
    t.rotation_euler = (math.radians(90), math.radians(90), 0)
    p.append(t)
    gg = group(p, 'B'); gg.location = (x, y, 0); gg.rotation_euler = (math.radians(90), 0, rz)
    return gg
battery(-0.006, 0.0, math.radians(-25)); battery(0.008, 0.012, math.radians(-12))
lights_camera((0.0, 0.004, 0), 0.13, 34, -20, fov=30)
render('Battery')

# ---------------- Agua de almendras ----------------
reset()
plastic = mat('plastic', (0.95, 0.95, 0.97), 0.08, 0.0, trans=0.9, ior=1.4)
milk = mat('milk', (0.93, 0.88, 0.78), 0.3)
cap = mat('cap', (0.62, 0.42, 0.22), 0.5)
label = mat('label', (0.86, 0.79, 0.62), 0.6, tex='grime')
ink = mat('ink', (0.32, 0.18, 0.08), 0.6)
p = []
p.append(cyl(0.032, 0.15, (0, 0, 0.075), plastic, 64))
p.append(cone(0.032, 0.014, 0.045, (0, 0, 0.1725), plastic))
p.append(cyl(0.0145, 0.022, (0, 0, 0.205), cap, 48))
p.append(cyl(0.0295, 0.125, (0, 0, 0.065), milk, 64))
p.append(cyl(0.0326, 0.06, (0, 0, 0.08), label, 64))
t1 = text('AGUA DE', 0.0105, (0, -0.0335, 0.091), ink, font=FONT_H)
t2 = text('ALMENDRAS', 0.0105, (0, -0.0335, 0.078), ink, font=FONT_H)
for t in (t1, t2):
    md = t.modifiers.new('C', 'SIMPLE_DEFORM'); md.deform_method = 'BEND'; md.deform_axis = 'Z'; md.angle = math.radians(35)
    p.append(t)
g = group(p, 'A'); g.rotation_euler = (0, 0, math.radians(14))
lights_camera((0, 0, 0.105), 0.62, 14, -18, fov=30)
render('AlmondWater')

# ---------------- Barra luminosa ----------------
reset()
glow = mat('glow', (0.35, 1.0, 0.25), 0.15, 0.0, emit=(0.35, 1.0, 0.25), estr=7, trans=0.3)
capm = mat('capm', (0.08, 0.3, 0.06), 0.4)
p = [cyl(0.0075, 0.15, (0, 0, 0), glow, 32), cyl(0.0085, 0.012, (0, 0, 0.081), capm, 32), cyl(0.0085, 0.01, (0, 0, -0.08), capm, 32)]
bpy.ops.mesh.primitive_torus_add(major_radius=0.007, minor_radius=0.0016, location=(0, 0, 0.094), rotation=(math.radians(90), 0, 0))
o = bpy.context.object; obj_set(o, capm); p.append(o)
g = group(p, 'G'); g.rotation_euler = (math.radians(55), 0, math.radians(35))
lights_camera((0, 0, 0), 0.5, 20, -20, fov=30)
render('Glowstick')

# ---------------- Cinta VHS ----------------
reset()
shell = mat('shell', (0.018, 0.018, 0.02), 0.42, 0.0, tex='grime')
win = mat('win', (0.2, 0.2, 0.22), 0.05, 0.0, trans=0.8, alpha=0.6)
reel = mat('reel', (0.85, 0.85, 0.85), 0.4)
tape = mat('tape', (0.06, 0.04, 0.03), 0.3, 0.3)
lab = mat('lab', (0.9, 0.87, 0.78), 0.65, tex='grime')
pen = mat('pen', (0.12, 0.1, 0.35), 0.6)
p = [box(0.187, 0.025, 0.103, (0, 0, 0), shell, 0.002)]
p.append(box(0.085, 0.002, 0.03, (0, -0.0126, 0.014), win, 0.001))
for x in (-0.03, 0.03):
    p.append(cyl(0.017 if x < 0 else 0.011, 0.016, (x, -0.003, 0.014), tape, 48, rot=(math.radians(90), 0, 0)))
    p.append(cyl(0.008, 0.017, (x, -0.003, 0.014), reel, 6, rot=(math.radians(90), 0, 0)))
p.append(box(0.12, 0.0015, 0.035, (0, -0.0127, -0.028), lab, 0.0005))
t = text('NO ABRIR - 1994', 0.0105, (0, -0.0137, -0.028), pen, font=FONT_H); t.rotation_euler = (math.radians(90), 0, math.radians(0)); p.append(t)
g = group(p, 'V'); g.rotation_euler = (math.radians(8), math.radians(-6), math.radians(22))
lights_camera((0, 0, 0), 0.62, 16, -24, fov=30)
render('VhsTape')
