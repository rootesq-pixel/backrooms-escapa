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
sc.cycles.max_bounces = 6; sc.cycles.diffuse_bounces = 4; sc.cycles.volume_bounces = 0
sc.render.resolution_x = int(os.environ.get('RX', '1600')); sc.render.resolution_y = int(os.environ.get('RY', '1200'))
sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'; sc.view_settings.exposure = -0.15
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
X0, X1, Y0, Y1 = -6.0, 8.0, -6.0, 10.0
quad('Floor', [(X0, Y0, 0), (X1, Y0, 0), (X1, Y1, 0), (X0, Y1, 0)], [(X0, Y0), (X1, Y0), (X1, Y1), (X0, Y1)], carpet)
# techo con 2 huecos de panel (0,6 × 1,2): se hace como placas alrededor (rejilla de 0,6 × 1,2), saltando las de los paneles
PANELS = [(0.3, 3.0), (2.1, 6.6)]   # centros (x, y) de los 2 paneles (en la rejilla 0,6 × 1,2)
def is_panel(cx, cy): return any(abs(cx - px) < 0.05 and abs(cy - py) < 0.05 for px, py in PANELS)
verts, faces, uvs = [], [], []
x = X0
while x < X1 - 1e-6:
    y = Y0
    while y < Y1 - 1e-6:
        cx, cy = x + 0.3, y + 0.6
        if not is_panel(cx, cy):
            k = len(verts); verts += [(x, y, H), (x, y + 1.2, H), (x + 0.6, y + 1.2, H), (x + 0.6, y, H)]; faces.append([k, k + 1, k + 2, k + 3])
        y += 1.2
    x += 0.6
me = bpy.data.meshes.new('Ceiling'); me.from_pydata(verts, [], faces); me.update(); ul = me.uv_layers.new()
for i, l in enumerate(me.loops): v = me.vertices[l.vertex_index].co; ul.data[i].uv = (v.x, v.y)
co = bpy.data.objects.new('Ceiling', me); sc.collection.objects.link(co); me.materials.append(ceil)
# paneles fluorescentes rehundidos: caja blanca de 6 cm, difusor emisivo verdoso
lm = bpy.data.materials.new('Diffuser'); lm.use_nodes = True; e = lm.node_tree.nodes['Principled BSDF']
e.inputs['Base Color'].default_value = (0.9, 0.95, 0.8, 1); e.inputs['Emission Color'].default_value = (0.86, 1.0, 0.62, 1); e.inputs['Emission Strength'].default_value = 9.0
fm = bpy.data.materials.new('PanelFrame'); fm.use_nodes = True; fb = fm.node_tree.nodes['Principled BSDF']; fb.inputs['Base Color'].default_value = (0.82, 0.82, 0.78, 1); fb.inputs['Roughness'].default_value = 0.4
for px, py in PANELS:
    for (a, b) in [((px - 0.3, py - 0.6, H), (px + 0.3, py - 0.585, H + 0.06)), ((px - 0.3, py + 0.585, H), (px + 0.3, py + 0.6, H + 0.06)),
                   ((px - 0.3, py - 0.6, H), (px - 0.285, py + 0.6, H + 0.06)), ((px + 0.285, py - 0.6, H), (px + 0.3, py + 0.6, H + 0.06))]:
        box('PanelFrame', a, b, fm)
    d = box('Diffuser', (px - 0.285, py - 0.585, H + 0.04), (px + 0.285, py + 0.585, H + 0.05), lm)
    L = bpy.data.lights.new('Tube', 'AREA'); L.shape = 'RECTANGLE'; L.size = 0.55; L.size_y = 1.15; L.energy = 210; L.color = (0.96, 1.0, 0.70)
    lo = bpy.data.objects.new('Tube', L); lo.location = (px, py, H + 0.035); sc.collection.objects.link(lo)
# pared empapelada larga (fondo, y = Y1w) y retorno en esquina (x = XLw); u = distancia horizontal / 2,4, v = z / 3
Y1w, XLw = 8.0, -4.0
def wallq(name, a, b):   # a, b = extremos (x, y) en planta, normal hacia la cámara
    L_ = math.dist(a, b)
    return quad(name, [(a[0], a[1], 0), (b[0], b[1], 0), (b[0], b[1], H), (a[0], a[1], H)], [(0, 0), (L_, 0), (L_, H), (0, H)], wall)
wallq('WallBack', (XLw, Y1w), (X1, Y1w))
wallq('WallLeft', (XLw, Y0 + 2.0), (XLw, Y1w))
# tabique a la derecha (como en la foto) con su rodapié
wallq('WallFarRight', (X1, Y1w), (X1, Y0)); wallq('WallRightF', (4.6, 3.2), (4.6, 7.2)); wallq('WallRightS', (4.6, 7.2), (6.4, 7.2)); wallq('WallRightB', (6.4, 3.2), (4.6, 3.2))
# pilar cuadrado de 0,66 m empapelado
PX, PY, S = 1.4, 3.3, 0.33
for i, (a, b) in enumerate([((PX - S, PY - S), (PX + S, PY - S)), ((PX + S, PY - S), (PX + S, PY + S)), ((PX + S, PY + S), (PX - S, PY + S)), ((PX - S, PY + S), (PX - S, PY - S))]):
    o = wallq('Pillar%d' % i, a, b)
    for l in o.data.uv_layers[0].data: l.uv = (l.uv[0] + i * 0.66, l.uv[1])
# rodapiés reales (1,2 cm de grueso, 10 cm) de color crema
bm = bpy.data.materials.new('Baseboard'); bm.use_nodes = True; bb = bm.node_tree.nodes['Principled BSDF']; bb.inputs['Base Color'].default_value = (0.70, 0.66, 0.40, 1); bb.inputs['Roughness'].default_value = 0.45
box('BaseBack', (XLw, Y1w - 0.012, 0), (X1, Y1w, 0.10), bm)
box('BaseR', (4.6 - 0.012, 3.2, 0), (4.6, 7.2, 0.10), bm)
box('BaseLeft', (XLw, Y0 + 2.0, 0), (XLw + 0.012, Y1w, 0.10), bm)
box('BaseP0', (PX - S - 0.012, PY - S - 0.012, 0), (PX + S + 0.012, PY + S + 0.012, 0.10), bm)
# cielo / rebote: mundo amarillo tenue + bruma (volumen homogéneo) para la distancia
w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True; bg = w.node_tree.nodes['Background']
bg.inputs['Color'].default_value = (0.30, 0.31, 0.11, 1); bg.inputs['Strength'].default_value = 0.75
vol = w.node_tree.nodes.new('ShaderNodeVolumePrincipled'); vol.inputs['Density'].default_value = float(os.environ.get('HAZE', '0.012'))
vol.inputs['Color'].default_value = (0.95, 0.9, 0.55, 1)
w.node_tree.links.new(vol.outputs[0], w.node_tree.nodes['World Output'].inputs['Volume'])
# luz de relleno muy suave (rebote del techo) para aplanar como en la foto
f = bpy.data.lights.new('Fill', 'AREA'); f.size = 10; f.energy = 380; f.color = (1.0, 0.95, 0.62)
fo = bpy.data.objects.new('Fill', f); fo.location = (1.0, 2.0, H - 0.05); sc.collection.objects.link(fo)
cam = bpy.data.cameras.new('Cam'); cam.lens = 20; cam.sensor_width = 36
co_ = bpy.data.objects.new('Cam', cam); sc.collection.objects.link(co_); sc.camera = co_
def shoot(name, pos, tgt, lens):
    cam.lens = lens; co_.location = pos
    d = Vector(tgt) - Vector(pos); co_.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(OUT, name + '.png'); bpy.ops.render.render(write_still=True); print('shot', name, flush=True)
shoot('l0_test_angulo_foto', (-2.9, -2.6, 1.55), (2.6, 6.2, 1.15), 18)
shoot('l0_test_pilar_cerca', (-0.4, 0.4, 1.6), (1.6, 4.6, 1.2), 26)
print('done', flush=True)
