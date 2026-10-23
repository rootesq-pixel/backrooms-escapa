# v7.3.0: esquina de prueba del nivel 0 (look de la foto clásica) con las texturas procedurales horneadas (WallpaperL0, CarpetL0,
# CeilingL0): una pared empapelada (más el retorno en esquina), un pilar cuadrado, moqueta, techo de placas con 2 paneles
# fluorescentes, rodapiés, luz plana amarillenta y bruma. Cycles en GPU, sin ventana. Uso: blender -b -P l0_testcorner.py -- <tex> <out>
import bpy, sys, os, math
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
TEX, OUT = argv[0], argv[1]; os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
try:
    p = bpy.context.preferences.addons['cycles'].preferences
    for t in ('OPTIX', 'CUDA'):
        try:
            p.compute_device_type = t; p.get_devices()
            if any(d.type == t for d in p.devices): break
        except Exception: pass
    for d in p.devices: d.use = d.type == p.compute_device_type
    sc.cycles.device = 'GPU'; print('device', p.compute_device_type, flush=True)
except Exception as e: print('cpu', e)
sc.cycles.samples = int(os.environ.get('SAMPLES', '160')); sc.cycles.use_denoising = True
sc.cycles.max_bounces = 6; sc.cycles.diffuse_bounces = 3; sc.cycles.use_light_tree = True; sc.cycles.volume_bounces = 0
sc.render.resolution_x = int(os.environ.get('RX', '1600')); sc.render.resolution_y = int(os.environ.get('RY', '1200'))
sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'; sc.view_settings.exposure = float(os.environ.get('EXPO', '-0.85'))
H = 3.0

def img(name, data=False):
    i = bpy.data.images.load(os.path.join(TEX, name)); i.colorspace_settings.name = 'Non-Color' if data else 'sRGB'; return i

def surf_mat(name, style, su, sv, bump=0.6, rough_bias=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; N = nt.nodes; Lk = nt.links.new
    b = N['Principled BSDF']
    tc = N.new('ShaderNodeTexCoord'); mp = N.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (1 / su, 1 / sv, 1)
    Lk(tc.outputs['UV'], mp.inputs['Vector'])
    ta = N.new('ShaderNodeTexImage'); ta.image = img(style + '_albedo.png'); Lk(mp.outputs[0], ta.inputs[0])
    td = N.new('ShaderNodeTexImage'); td.image = img(style + '_data.png', True); Lk(mp.outputs[0], td.inputs[0])
    sp = N.new('ShaderNodeSeparateColor'); Lk(td.outputs[0], sp.inputs[0])
    Lk(ta.outputs[0], b.inputs['Base Color'])
    inv = N.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0 - rough_bias; Lk(sp.outputs[1], inv.inputs[1])
    Lk(inv.outputs[0], b.inputs['Roughness'])
    bp = N.new('ShaderNodeBump'); bp.inputs['Strength'].default_value = bump; bp.inputs['Distance'].default_value = 0.004
    Lk(sp.outputs[0], bp.inputs['Height']); Lk(bp.outputs[0], b.inputs['Normal'])
    b.inputs['Specular IOR Level'].default_value = 0.35
    return m

def quad(name, verts, uvs, mat):
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], [list(range(len(verts)))]); me.update()
    ul = me.uv_layers.new()
    for i, l in enumerate(me.loops): ul.data[i].uv = uvs[l.vertex_index]
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); me.materials.append(mat); return o

def box(name, lo, hi, mat):
    bpy.ops.mesh.primitive_cube_add(size=1, location=[(a + b) / 2 for a, b in zip(lo, hi)])
    o = bpy.context.active_object; o.name = name; o.scale = [b - a for a, b in zip(lo, hi)]
    bpy.ops.object.transform_apply(scale=True); o.data.materials.append(mat); return o

wall = surf_mat('Wallpaper', 'WallpaperL0', 2.4, 3.0, 0.5)
carpet = surf_mat('Carpet', 'CarpetL0', 2.0, 2.0, 0.9)
ceil = surf_mat('Ceiling', 'CeilingL0', 2.4, 2.4, 0.5)
# v2 (revisión del usuario): sala grande y profunda (26 × 36 m) con muchos pilares que se alejan, bloques de pared, techo crema con
# paneles fluorescentes en filas (uno cada 3 × 2 placas) que iluminan de verdad; sin luz de relleno (no quemar las paredes).
X0, X1, Y0, Y1 = -9.6, 15.6, -6.0, 30.0
quad('Floor', [(X0, Y0, 0), (X1, Y0, 0), (X1, Y1, 0), (X0, Y1, 0)], [(X0, Y0), (X1, Y0), (X1, Y1), (X0, Y1)], carpet)
PANELS = []
for ix in range(-5, 9):
    for iy in range(-2, 12):
        PANELS.append((ix * 1.8 + 0.3 + (0.6 if iy % 2 else 0.0), iy * 2.4 + 0.6))
# v3 (más oscuro): paneles apagados en grupos y algunos parpadeando (en la foto, a media luz), deterministas: misma regla que el juego
def _h(a, b, k):
    n = (a * 73856093) ^ (b * 19349663) ^ (k * 83492791)
    n = (n ^ (n >> 13)) * 1274126177 & 0xffffffff
    return ((n ^ (n >> 16)) & 0xffff) / 65535.0
def _near_path(px, py):   # el camino que se ve delante desde la cámara queda iluminado
    ax, ay, bx, by = -2.6, -3.2, 4.0, 14.0
    dx, dy = bx - ax, by - ay; t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy)) < 1.9 and t < 0.75
PSTATE = {}
for px, py in PANELS:
    cx_, cy_ = int(math.floor(px / 5.4)), int(math.floor(py / 7.2))   # grupos de ~3 × 3 paneles
    dead = _h(cx_, cy_, 1) < 0.30 or _h(int(px * 10), int(py * 10), 2) < 0.08
    flick = (not dead) and _h(int(px * 10), int(py * 10), 3) < 0.10
    if _near_path(px, py): dead = False
    PSTATE[(round(px, 2), round(py, 2))] = 'dead' if dead else ('flick' if flick else 'on')
print('panels', len(PANELS), 'dead', sum(v == 'dead' for v in PSTATE.values()), 'flick', sum(v == 'flick' for v in PSTATE.values()))
PSET = set((round(px, 2), round(py, 2)) for px, py in PANELS)
def is_panel(cx, cy): return (round(cx, 2), round(cy, 2)) in PSET
verts, faces = [], []
for i in range(int(round((X1 - X0) / 0.6))):
    for j in range(int(round((Y1 - Y0) / 1.2))):
        x = X0 + i * 0.6; y = Y0 + j * 1.2
        if is_panel(x + 0.3, y + 0.6): continue
        k = len(verts); verts += [(x, y, H), (x, y + 1.2, H), (x + 0.6, y + 1.2, H), (x + 0.6, y, H)]; faces.append([k, k + 1, k + 2, k + 3])
me = bpy.data.meshes.new('Ceiling'); me.from_pydata(verts, [], faces); me.update(); ul = me.uv_layers.new()
for i, l in enumerate(me.loops): v = me.vertices[l.vertex_index].co; ul.data[i].uv = (v.x, v.y)
co = bpy.data.objects.new('Ceiling', me); sc.collection.objects.link(co); me.materials.append(ceil)
lm = bpy.data.materials.new('Diffuser'); lm.use_nodes = True; e = lm.node_tree.nodes['Principled BSDF']
e.inputs['Base Color'].default_value = (0.80, 0.90, 0.66, 1); e.inputs['Emission Color'].default_value = (0.80, 1.0, 0.62, 1); e.inputs['Emission Strength'].default_value = 1.25  # v2b: tinte verde visible (sin saturar a blanco)
fm = bpy.data.materials.new('PanelFrame'); fm.use_nodes = True; fb = fm.node_tree.nodes['Principled BSDF']; fb.inputs['Base Color'].default_value = (0.86, 0.85, 0.76, 1); fb.inputs['Roughness'].default_value = 0.4
TL = bpy.data.lights.new('Tube', 'AREA'); TL.shape = 'RECTANGLE'; TL.size = 0.55; TL.size_y = 1.15; TL.energy = float(os.environ.get('PANEL_W', '18')); TL.color = (0.90, 1.0, 0.66)
lmd = bpy.data.materials.new('DiffuserDead'); lmd.use_nodes = True; lmd.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.42, 0.43, 0.36, 1)
lmf = lm.copy(); lmf.name = 'DiffuserFlicker'; lmf.node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = 0.45
TLf = TL.copy(); TLf.energy = TL.energy * 0.3
for px, py in PANELS:
    for (a, b) in [((px - 0.3, py - 0.6, H), (px + 0.3, py - 0.585, H + 0.06)), ((px - 0.3, py + 0.585, H), (px + 0.3, py + 0.6, H + 0.06)),
                   ((px - 0.3, py - 0.6, H), (px - 0.285, py + 0.6, H + 0.06)), ((px + 0.285, py - 0.6, H), (px + 0.3, py + 0.6, H + 0.06))]:
        box('PanelFrame', a, b, fm)
    st = PSTATE[(round(px, 2), round(py, 2))]
    box('Diffuser', (px - 0.285, py - 0.585, H + 0.04), (px + 0.285, py + 0.585, H + 0.05), lm if st == 'on' else (lmf if st == 'flick' else lmd))
    if st != 'dead':
        lo = bpy.data.objects.new('Tube', TL if st == 'on' else TLf); lo.location = (px, py, H + 0.035); sc.collection.objects.link(lo)
def wallq(name, a, b, u0=0.0):
    L_ = math.dist(a, b)
    return quad(name, [(a[0], a[1], 0), (b[0], b[1], 0), (b[0], b[1], H), (a[0], a[1], H)], [(u0, 0), (u0 + L_, 0), (u0 + L_, H), (u0, H)], wall)
bm = bpy.data.materials.new('Baseboard'); bm.use_nodes = True; bb = bm.node_tree.nodes['Principled BSDF']; bb.inputs['Base Color'].default_value = (0.72, 0.68, 0.42, 1); bb.inputs['Roughness'].default_value = 0.45
def block(name, cx, cy, wx, wy):
    """pilar o bloque de pared empapelado con rodapié"""
    x0, x1, y0, y1 = cx - wx / 2, cx + wx / 2, cy - wy / 2, cy + wy / 2
    u = 0.0
    for i, (a, b) in enumerate([((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))]):
        wallq('%s_%d' % (name, i), a, b, u); u += math.dist(a, b)
    box(name + '_base', (x0 - 0.012, y0 - 0.012, 0), (x1 + 0.012, y1 + 0.012, 0.10), bm)
# muros perimetrales (lejos) y bloques/pilares en rejilla de 4,8 m que se alejan
for (a, b) in [((X0, Y1), (X1, Y1)), ((X1, Y1), (X1, Y0)), ((X0, Y0), (X0, Y1))]: wallq('Perim', a, b)
import random
rnd = random.Random(7)
for gx in range(-2, 4):
    for gy in range(0, 6):
        cx, cy = gx * 4.8 + 1.2, gy * 4.8 + 2.4
        if abs(cx - (-2.6)) < 2.5 and abs(cy - (-2.0)) < 3.0: continue        # deja libre la cámara
        r_ = rnd.random()
        if r_ < 0.18: block('Wall%d_%d' % (gx, gy), cx, cy, 2.4, 0.66)       # tramo de pared
        elif r_ < 0.30: block('Wall%d_%d' % (gx, gy), cx, cy, 0.66, 2.4)
        else: block('Pillar%d_%d' % (gx, gy), cx, cy, 0.66, 0.66)
w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True; bg = w.node_tree.nodes['Background']
bg.inputs['Color'].default_value = (0.20, 0.21, 0.07, 1); bg.inputs['Strength'].default_value = 0.05
vol = w.node_tree.nodes.new('ShaderNodeVolumePrincipled'); vol.inputs['Density'].default_value = float(os.environ.get('HAZE', '0.016'))
vol.inputs['Color'].default_value = (0.55, 0.56, 0.30, 1); vol.inputs['Absorption Color'].default_value = (0.30, 0.30, 0.22, 1)  # bruma lejana más oscura
w.node_tree.links.new(vol.outputs[0], w.node_tree.nodes['World Output'].inputs['Volume'])
cam = bpy.data.cameras.new('Cam'); cam.lens = 20; cam.sensor_width = 36
co_ = bpy.data.objects.new('Cam', cam); sc.collection.objects.link(co_); sc.camera = co_
def shoot(name, pos, tgt, lens):
    cam.lens = lens; co_.location = pos
    d = Vector(tgt) - Vector(pos); co_.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(OUT, name + '.png'); bpy.ops.render.render(write_still=True); print('shot', name, flush=True)
shoot('l0_test_angulo_foto', (-2.6, -3.2, 1.6), (4.0, 14.0, 1.05), 16)
shoot('l0_test_pilar_cerca', (-1.0, -1.0, 1.6), (2.2, 6.0, 1.1), 22)
print('done', flush=True)
