# Nivel 7460 — pasillo infinito de puertas blancas. Módulo de 3,4 m (paredes gris pizarra, zócalos, molduras y puertas
# blancas con plafón en el techo) + hoja de puerta + habitación lateral, horneados con Cycles (irradiancia difusa, UV2).
# Blender: x = Unity x (ancho), y = Unity z (a lo largo), z = Unity y (arriba). Pasillo centrado en x = 0, módulo y ∈ [-CS/2, CS/2].
# Uso: blender -b -t 8 -P build_corridor.py -- <spp> <res_modulo> <res_hab> [bake|render]
import bpy, bmesh, math, sys, json, struct, os
import numpy as np
from mathutils import Vector, Matrix
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
SPP = int(argv[0]) if argv else 128; RES = int(argv[1]) if len(argv) > 1 else 2048; RESR = int(argv[2]) if len(argv) > 2 else 1024
MODE = argv[3] if len(argv) > 3 else 'bake'
OUT = os.environ.get('C7_OUT', '/workspace/v7460/bake/').rstrip('/') + '/'
os.makedirs(OUT, exist_ok=True)
def cycles_device(sc):
    dev = os.environ.get('CYCLES_DEVICE', 'CPU').upper()
    if dev == 'CPU': sc.cycles.device = 'CPU'; return 'CPU'
    p = bpy.context.preferences.addons['cycles'].preferences
    for t in (('OPTIX', 'CUDA') if dev in ('GPU', 'OPTIX') else (dev,)):
        try: p.compute_device_type = t; p.get_devices()
        except TypeError: continue
        if any(d.type == t for d in p.devices): break
    for d in p.devices: d.use = (d.type == p.compute_device_type)
    sc.cycles.device = 'GPU'; return p.compute_device_type

for o in list(bpy.data.objects): bpy.data.objects.remove(o)

# ---------------- medidas (m) ----------------
CS = 3.4; HW = 1.7; H = 3.2                   # módulo, medio ancho, altura del techo
DW = 0.94; DH = 2.14                          # hueco de la puerta
JT = 0.16                                     # fondo del hueco (grueso del muro)
XF = HW + 0.03; LT = 0.04; XB = XF + LT       # cara de la hoja hacia el pasillo / grueso / cara trasera
XR = HW + JT                                  # cara interior de la habitación (muro del pasillo)
CW = 0.075; CP = 0.014; BW = 0.024; BP = 0.022 # moldura: ancho, saliente, junquillo interior (ancho, saliente)
SKH = 0.11; SKT = 0.016                       # zócalo
GAP = 0.003                                   # holgura de la hoja
LW = DW - 2 * GAP; LH = DH - GAP - 0.008      # hoja
PX = 1.22; PY = 0.62; PF = 0.035              # plafón (x a lo ancho, y a lo largo), marco
RD = 3.0; RWh = 3.2; RHt = 3.0                # habitación: fondo, ancho, alto
HY = DW / 2 - 0.07; HZ = 1.0                  # manilla: posición a lo largo (lado +y) y altura
M_FLOOR, M_WALL, M_CEIL, M_TRIM, M_DOOR, M_METAL, M_EMIT, M_BRICK = range(8)
NMAT = 8
TILE = {M_FLOOR: 6.8, M_WALL: 2.0, M_CEIL: 2.0, M_TRIM: 1.0, M_DOOR: 1.0, M_METAL: 1.0, M_EMIT: 1.0, M_BRICK: 2.0}

def face(bm, pts, m, nrm):
    vs = [bm.verts.new(p) for p in pts]
    a, b, c = Vector(pts[0]), Vector(pts[1]), Vector(pts[2])
    if (b - a).cross(c - a).dot(Vector(nrm)) < 0: vs.reverse()
    f = bm.faces.new(vs); f.material_index = m; return f
def rect_x(bm, x, y0, y1, z0, z1, m, nx):   # plano x = cte, normal ±x
    if y1 - y0 > 1e-5 and z1 - z0 > 1e-5: face(bm, [(x, y0, z0), (x, y1, z0), (x, y1, z1), (x, y0, z1)], m, (nx, 0, 0))
def rect_y(bm, y, x0, x1, z0, z1, m, ny):
    if x1 - x0 > 1e-5 and z1 - z0 > 1e-5: face(bm, [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], m, (0, ny, 0))
def rect_z(bm, z, x0, x1, y0, y1, m, nz):
    if x1 - x0 > 1e-5 and y1 - y0 > 1e-5: face(bm, [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], m, (0, 0, nz))
def box(bm, x0, x1, y0, y1, z0, z1, m, skip=(), bevel=0.0):
    if x1 - x0 < 1e-5 or y1 - y0 < 1e-5 or z1 - z0 < 1e-5: return
    if bevel > 0:
        tb = bmesh.new(); bmesh.ops.create_cube(tb, size=1.0)
        for v in tb.verts: v.co = Vector((x0 + (v.co.x + 0.5) * (x1 - x0), y0 + (v.co.y + 0.5) * (y1 - y0), z0 + (v.co.z + 0.5) * (z1 - z0)))
        bmesh.ops.bevel(tb, geom=list(tb.edges), offset=min(bevel, (x1 - x0) * 0.45, (y1 - y0) * 0.45, (z1 - z0) * 0.45), segments=2, profile=0.5, affect='EDGES', clamp_overlap=True)
        merge(bm, tb, m); tb.free(); return
    if '-x' not in skip: rect_x(bm, x0, y0, y1, z0, z1, m, -1)
    if '+x' not in skip: rect_x(bm, x1, y0, y1, z0, z1, m, 1)
    if '-y' not in skip: rect_y(bm, y0, x0, x1, z0, z1, m, -1)
    if '+y' not in skip: rect_y(bm, y1, x0, x1, z0, z1, m, 1)
    if '-z' not in skip: rect_z(bm, z0, x0, x1, y0, y1, m, -1)
    if '+z' not in skip: rect_z(bm, z1, x0, x1, y0, y1, m, 1)
def merge(dst, src, m=None):
    mp = {}
    for v in src.verts: mp[v] = dst.verts.new(v.co)
    for f in src.faces:
        nf = dst.faces.new([mp[v] for v in f.verts]); nf.material_index = f.material_index if m is None else m
def cyl(bm, c, axis, r, L, m, seg=20, caps=(True, True)):
    """cilindro de radio r y largo L desde c a lo largo de axis (vector unitario)"""
    ax = Vector(axis).normalized(); u = ax.orthogonal().normalized(); w = ax.cross(u)
    c0 = Vector(c); c1 = c0 + ax * L
    ring0 = [bm.verts.new(c0 + (u * math.cos(2 * math.pi * i / seg) + w * math.sin(2 * math.pi * i / seg)) * r) for i in range(seg)]
    ring1 = [bm.verts.new(c1 + (u * math.cos(2 * math.pi * i / seg) + w * math.sin(2 * math.pi * i / seg)) * r) for i in range(seg)]
    for i in range(seg):
        j = (i + 1) % seg; f = bm.faces.new([ring0[i], ring0[j], ring1[j], ring1[i]]); f.material_index = m; f.smooth = True
    if caps[0]: f = bm.faces.new(list(reversed(ring0))); f.material_index = m
    if caps[1]: f = bm.faces.new(ring1); f.material_index = m

# ---------------- módulo del pasillo ----------------
def wall_side(bm, s):
    """muro, hueco, molduras y zócalo del lado s (+1 derecha, -1 izquierda)"""
    x = s * HW; n = -s
    y0, y1 = -CS / 2, CS / 2
    rect_x(bm, x, y0, -DW / 2, 0, H, M_WALL, n); rect_x(bm, x, DW / 2, y1, 0, H, M_WALL, n); rect_x(bm, x, -DW / 2, DW / 2, DH, H, M_WALL, n)
    # forro del hueco (jambas y dintel) hasta la habitación
    xa, xb = sorted((x, s * XR))
    rect_y(bm, -DW / 2, xa, xb, 0, DH, M_TRIM, 1); rect_y(bm, DW / 2, xa, xb, 0, DH, M_TRIM, -1); rect_z(bm, DH, xa, xb, -DW / 2, DW / 2, M_TRIM, -1)
    rect_z(bm, 0.002, xa, xb, -DW / 2, DW / 2, M_TRIM, 1)       # umbral
    # tope de la hoja (listón tras la hoja)
    t0, t1 = sorted((s * XB, s * (XB + 0.012)))
    box(bm, t0, t1, -DW / 2, -DW / 2 + 0.012, 0, DH, M_TRIM); box(bm, t0, t1, DW / 2 - 0.012, DW / 2, 0, DH, M_TRIM); box(bm, t0, t1, -DW / 2, DW / 2, DH - 0.012, DH, M_TRIM)
    # moldura (dos escalones: tablero plano + junquillo junto al hueco)
    c0, c1 = sorted((x, x - s * CP)); b0, b1 = sorted((x, x - s * BP))
    box(bm, c0, c1, -DW / 2 - CW, -DW / 2, 0, DH + CW, M_TRIM, bevel=0.003)
    box(bm, c0, c1, DW / 2, DW / 2 + CW, 0, DH + CW, M_TRIM, bevel=0.003)
    box(bm, c0, c1, -DW / 2, DW / 2, DH, DH + CW, M_TRIM, bevel=0.003)
    box(bm, b0, b1, -DW / 2 - BW, -DW / 2, 0, DH + BW, M_TRIM, bevel=0.004)
    box(bm, b0, b1, DW / 2, DW / 2 + BW, 0, DH + BW, M_TRIM, bevel=0.004)
    box(bm, b0, b1, -DW / 2 - BW, DW / 2 + BW, DH, DH + BW, M_TRIM, bevel=0.004)
    # zócalo con canto superior biselado (de esquina a moldura)
    k0, k1 = sorted((x, x - s * SKT))
    box(bm, k0, k1, y0, -DW / 2 - CW, 0, SKH, M_TRIM, bevel=0.004)
    box(bm, k0, k1, DW / 2 + CW, y1, 0, SKH, M_TRIM, bevel=0.004)

def ceiling_panel(bm, cx, cy, zc, emit=True):
    hx, hy = PX / 2, PY / 2
    # techo alrededor del hueco del plafón
    rect_z(bm, zc, -HW, HW, -CS / 2, cy - hy, M_CEIL, -1); rect_z(bm, zc, -HW, HW, cy + hy, CS / 2, M_CEIL, -1)
    rect_z(bm, zc, -HW, cx - hx, cy - hy, cy + hy, M_CEIL, -1); rect_z(bm, zc, cx + hx, HW, cy - hy, cy + hy, M_CEIL, -1)
    # marco de aluminio lacado blanco (sobresale 8 mm) y difusor rehundido 6 mm
    d = 0.008
    box(bm, cx - hx, cx + hx, cy - hy, cy - hy + PF, zc - d, zc, M_TRIM, skip=('+z',), bevel=0.002)
    box(bm, cx - hx, cx + hx, cy + hy - PF, cy + hy, zc - d, zc, M_TRIM, skip=('+z',), bevel=0.002)
    box(bm, cx - hx, cx - hx + PF, cy - hy + PF, cy + hy - PF, zc - d, zc, M_TRIM, skip=('+z',), bevel=0.002)
    box(bm, cx + hx - PF, cx + hx, cy - hy + PF, cy + hy - PF, zc - d, zc, M_TRIM, skip=('+z',), bevel=0.002)
    e = 0.006; ix0, ix1, iy0, iy1 = cx - hx + PF, cx + hx - PF, cy - hy + PF, cy + hy - PF
    rect_x(bm, ix0, iy0, iy1, zc - d, zc + e, M_TRIM, 1); rect_x(bm, ix1, iy0, iy1, zc - d, zc + e, M_TRIM, -1)
    rect_y(bm, iy0, ix0, ix1, zc - d, zc + e, M_TRIM, 1); rect_y(bm, iy1, ix0, ix1, zc - d, zc + e, M_TRIM, -1)
    rect_z(bm, zc + e, ix0, ix1, iy0, iy1, M_EMIT, -1)

def leaf(bm, s):
    """hoja de puerta de dos plafones rehundidos, con manilla de palanca, bocallave y bisagras (lado s, cerrada)"""
    xf, xb = s * XF, s * XB; y0, y1 = -LW / 2, LW / 2; z0, z1 = 0.008, 0.008 + LH
    st = 0.115; rails = [z0, z0 + 0.17, z0 + 0.80, z0 + 0.98, z1 - 0.13, z1]; cols = [y0, y0 + st, y1 - st, y1]
    tmp = bmesh.new(); grid = {}
    for i in range(4):
        for j in range(6): grid[i, j] = tmp.verts.new((xf, cols[i], rails[j]))
    panel_faces = []
    for i in range(3):
        for j in range(5):
            vs = [grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]]
            if -s < 0: vs.reverse()   # normal hacia el pasillo (-s)
            f = tmp.faces.new(vs); f.material_index = M_DOOR
            if i == 1 and j in (1, 3): panel_faces.append(f)
    tmp.normal_update()
    r1 = bmesh.ops.inset_individual(tmp, faces=panel_faces, thickness=0.022, depth=-0.007, use_even_offset=True)
    r2 = bmesh.ops.inset_individual(tmp, faces=panel_faces, thickness=0.012, depth=0.0, use_even_offset=True)
    merge(bm, tmp); tmp.free()
    xa, xb2 = sorted((xf, xb))
    box(bm, xa, xb2, y0, y1, z0, z1, M_DOOR, skip=('-x',) if s > 0 else ('+x',), bevel=0.0)
    # manilla (roseta, cuello y palanca hacia la bisagra) y bocallave, en la cara del pasillo
    out = Vector((-s, 0, 0))
    cyl(bm, (xf, HY, HZ), out, 0.027, 0.010, M_METAL, 28)
    cyl(bm, (xf - s * 0.010, HY, HZ), out, 0.0095, 0.050, M_METAL, 20)
    cyl(bm, (xf - s * 0.052, HY + 0.008, HZ), (0, -1, 0), 0.0098, 0.128, M_METAL, 20)
    cyl(bm, (xf, HY, HZ - 0.115), out, 0.017, 0.006, M_METAL, 24)
    cyl(bm, (xf - s * 0.0061, HY, HZ - 0.115), out, 0.0045, 0.0006, M_EMIT if False else M_DOOR, 10)
    # bisagras (canutos a la vista en el lado -y)
    for zc in (0.24, 1.06, 1.90):
        cyl(bm, (xf - s * 0.003, -DW / 2 + 0.002, zc - 0.05), (0, 0, 1), 0.0085, 0.10, M_METAL, 16)

def module_mesh():
    bm = bmesh.new()
    rect_z(bm, 0.0, -HW, HW, -CS / 2, CS / 2, M_FLOOR, 1)
    ceiling_panel(bm, 0.0, 0.0, H)
    for s in (1, -1): wall_side(bm, s)
    for s in (1, -1): leaf(bm, s)
    return bm

def room_mesh():
    """habitación vacía tras la puerta del lado +x (x ∈ [XR, XR+RD]); muro del pasillo con hueco, molduras y zócalos"""
    bm = bmesh.new(); x0, x1 = XR, XR + RD; y0, y1 = -RWh / 2, RWh / 2
    rect_z(bm, 0.0, x0, x1, y0, y1, M_FLOOR, 1)
    # techo con plafón propio (como el del pasillo, más pequeño en el centro)
    cx = (x0 + x1) / 2; hx, hy = 0.6 / 2, 0.6 / 2
    rect_z(bm, RHt, x0, x1, y0, -hy, M_CEIL, -1); rect_z(bm, RHt, x0, x1, hy, y1, M_CEIL, -1)
    rect_z(bm, RHt, x0, cx - hx, -hy, hy, M_CEIL, -1); rect_z(bm, RHt, cx + hx, x1, -hy, hy, M_CEIL, -1)
    rect_z(bm, RHt + 0.006, cx - hx, cx + hx, -hy, hy, M_EMIT, -1)
    for (a, b) in (((cx - hx, cx + hx), (-hy, -hy + 0.03)), ((cx - hx, cx + hx), (hy - 0.03, hy)), ((cx - hx, cx - hx + 0.03), (-hy, hy)), ((cx + hx - 0.03, cx + hx), (-hy, hy))):
        box(bm, a[0], a[1], b[0], b[1], RHt - 0.008, RHt, M_TRIM, skip=('+z',))
    rect_x(bm, x1, y0, y1, 0, RHt, M_WALL, -1)
    rect_y(bm, y0, x0, x1, 0, RHt, M_WALL, 1); rect_y(bm, y1, x0, x1, 0, RHt, M_WALL, -1)
    rect_x(bm, x0, y0, -DW / 2, 0, RHt, M_WALL, 1); rect_x(bm, x0, DW / 2, y1, 0, RHt, M_WALL, 1); rect_x(bm, x0, -DW / 2, DW / 2, DH, RHt, M_WALL, 1)
    # moldura del lado de la habitación
    box(bm, x0, x0 + CP, -DW / 2 - CW, -DW / 2, 0, DH + CW, M_TRIM, bevel=0.003); box(bm, x0, x0 + CP, DW / 2, DW / 2 + CW, 0, DH + CW, M_TRIM, bevel=0.003)
    box(bm, x0, x0 + CP, -DW / 2, DW / 2, DH, DH + CW, M_TRIM, bevel=0.003)
    # zócalos
    box(bm, x0, x0 + SKT, y0, -DW / 2 - CW, 0, SKH, M_TRIM, bevel=0.004); box(bm, x0, x0 + SKT, DW / 2 + CW, y1, 0, SKH, M_TRIM, bevel=0.004)
    box(bm, x1 - SKT, x1, y0, y1, 0, SKH, M_TRIM, bevel=0.004)
    box(bm, x0 + SKT, x1 - SKT, y0, y0 + SKT, 0, SKH, M_TRIM, bevel=0.004); box(bm, x0 + SKT, x1 - SKT, y1 - SKT, y1, 0, SKH, M_TRIM, bevel=0.004)
    return bm

def uv0(me):
    uv = me.uv_layers.new(name='UV0')
    for p in me.polygons:
        n = p.normal; ax = max(range(3), key=lambda k: abs(n[k])); ts = TILE[p.material_index]
        for li in p.loop_indices:
            c = me.vertices[me.loops[li].vertex_index].co
            if ax == 2: a, b = c.x, c.y
            elif ax == 0: a, b = c.y, c.z
            else: a, b = c.x, c.z
            uv.data[li].uv = (a / ts, b / ts)
    return uv

def make_obj(name, bm):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    for p in me.polygons: p.use_smooth = p.material_index == M_METAL
    uv0(me)
    return o

def uv1(o, res):
    me = o.data; u = me.uv_layers.new(name='UV1'); me.uv_layers.active = u
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_mode(type='FACE'); bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    for p in me.polygons: p.select = p.material_index not in (M_EMIT, M_METAL)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(40), island_margin=0.0, area_weight=0.0, scale_to_bounds=False)
    try: bpy.ops.uv.pack_islands(rotate=True, margin=6.0 / res, shape_method='CONCAVE')
    except TypeError: bpy.ops.uv.pack_islands(rotate=True, margin=6.0 / res)
    bpy.ops.object.mode_set(mode='OBJECT')
    for p in me.polygons:
        if p.material_index in (M_EMIT, M_METAL):
            for li in p.loop_indices: u.data[li].uv = (0.0005, 0.0005)

modo = make_obj('Module', module_mesh())
room = make_obj('Room', room_mesh())
uv1(modo, RES); uv1(room, RESR)

# ---------------- materiales (albedo lineal medio de las texturas de Unity) ----------------
def mat(name, col, rough, emit=0.0, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*col, 1); b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    if emit: b.inputs['Emission Color'].default_value = (1.0, 0.99, 0.97, 1); b.inputs['Emission Strength'].default_value = emit
    return m
mats = [mat('Floor', (0.205, 0.212, 0.226), 0.14), mat('Wall', (0.093, 0.104, 0.137), 0.55), mat('Ceil', (0.78, 0.80, 0.79), 0.9),
        mat('Trim', (0.84, 0.85, 0.83), 0.35), mat('Door', (0.84, 0.85, 0.83), 0.35), mat('Metal', (0.8, 0.8, 0.8), 0.25, metal=1.0),
        mat('Emit', (1, 1, 1), 1.0, emit=float(os.environ.get('PANEL_EMIT', '30'))), mat('Brick', (0.38, 0.38, 0.36), 0.8)]
for o in (modo, room):
    for m in mats: o.data.materials.append(m)

sc = bpy.context.scene; sc.render.engine = 'CYCLES'; cy = sc.cycles; dev = cycles_device(sc); cy.samples = SPP
cy.max_bounces = 8; cy.diffuse_bounces = 5; cy.glossy_bounces = 3; cy.use_denoising = False
sc.world = bpy.data.worlds.new('W'); sc.world.use_nodes = True; sc.world.node_tree.nodes['Background'].inputs[1].default_value = 0.0
LAMPW = float(os.environ.get('LAMPW', '140'))
def lamp(x, y, z, sx, sy, w):
    L = bpy.data.lights.new('Panel', 'AREA'); L.shape = 'RECTANGLE'; L.size = sx; L.size_y = sy; L.energy = w; L.color = (1.0, 0.985, 0.96)
    o = bpy.data.objects.new('Panel', L); o.location = (x, y, z); sc.collection.objects.link(o); return o

NCOPY = int(os.environ.get('NCOPY', '4')) if MODE == 'bake' else int(os.environ.get('NREF', '60'))
for k in range(-NCOPY, NCOPY + 1):
    if k != 0:
        c = bpy.data.objects.new('Mod%d' % k, modo.data); c.location = (0, k * CS, 0); sc.collection.objects.link(c)
    lamp(0, k * CS, H - 0.002, PX - 2 * PF, PY - 2 * PF, LAMPW)
lamp(XR + RD / 2, 0, RHt - 0.002, 0.54, 0.54, LAMPW * 0.9)
# tapas de los extremos (el pasillo sigue: un muro lejano gris para no dejar escapar la luz al vacío)
endw = bpy.data.meshes.new('Ends'); eb = bmesh.new()
for s in (-1, 1): rect_y(eb, s * (NCOPY + 0.5) * CS, -HW, HW, 0, H, M_WALL, -s)
eb.to_mesh(endw); eb.free(); eo = bpy.data.objects.new('Ends', endw); sc.collection.objects.link(eo); eo.data.materials.append(mats[M_WALL])

if MODE == 'render':
    cam = bpy.data.cameras.new('Cam'); cam.lens = float(os.environ.get('LENS', '20')); co = bpy.data.objects.new('Cam', cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (0, -NCOPY * CS + 0.4, 1.6); co.rotation_euler = (math.radians(90), 0, 0)
    sc.render.resolution_x = 1280; sc.render.resolution_y = 720; cy.use_denoising = True
    sc.view_settings.view_transform = 'AgX'; sc.view_settings.exposure = float(os.environ.get('EXPO', '0.0'))
    sc.render.filepath = OUT + 'ref_cycles.png'; bpy.ops.render.render(write_still=True); print('render ok', flush=True); sys.exit(0)

# ---------------- horneado (irradiancia difusa directa + indirecta) ----------------
def bake_target(o, res, nm):
    img = bpy.data.images.new(nm, res, res, float_buffer=True, alpha=False)
    mm = []
    for i, m in enumerate(o.data.materials):
        m2 = m.copy(); o.data.materials[i] = m2; nt = m2.node_tree; t = nt.nodes.new('ShaderNodeTexImage'); t.image = img; nt.nodes.active = t
    return img
img_m = bake_target(modo, RES, 'LM_Module'); img_r = bake_target(room, RESR, 'LM_Room')
b = sc.render.bake; b.use_pass_direct = True; b.use_pass_indirect = True; b.use_pass_color = False; b.margin = 8; b.margin_type = 'EXTEND'; b.target = 'IMAGE_TEXTURES'
bpy.ops.object.select_all(action='DESELECT'); modo.select_set(True); room.select_set(True); bpy.context.view_layer.objects.active = modo
import time; t0 = time.time()
bpy.ops.object.bake(type='DIFFUSE', uv_layer='UV1')
print('bake ok %.0f s' % (time.time() - t0), flush=True)
np.save(OUT + 'lm_mod_raw.npy', np.array(img_m.pixels[:], dtype=np.float32).reshape(RES, RES, 4)[:, :, :3])
np.save(OUT + 'lm_room_raw.npy', np.array(img_r.pixels[:], dtype=np.float32).reshape(RESR, RESR, 4)[:, :, :3])

# ---------------- exportar mallas (.bytes: verts pos/normal/uv0/uv1 + submallas) ----------------
def export(o, path, keep=None, origin=(0, 0, 0)):
    me = o.data; me.calc_loop_triangles()
    try: cn = me.corner_normals; getn = lambda li: cn[li].vector
    except Exception:
        me.calc_normals_split(); getn = lambda li: me.loops[li].normal
    U0 = me.uv_layers['UV0']; U1 = me.uv_layers['UV1']
    verts = []; nrm = []; uvA = []; uvB = []; subs = {k: [] for k in range(NMAT)}; index = {}
    ox, oy, oz = origin
    for tri in me.loop_triangles:
        p = me.polygons[tri.polygon_index]; m = p.material_index
        if keep is not None and not keep(p): continue
        idx = []
        for li in tri.loops:
            v = me.vertices[me.loops[li].vertex_index].co; n = getn(li)
            key = (tuple(round(c, 5) for c in v), tuple(round(c, 3) for c in n), tuple(round(c, 5) for c in U0.data[li].uv), tuple(round(c, 6) for c in U1.data[li].uv))
            if key not in index:
                index[key] = len(verts); verts.append((v.x - ox, v.z - oz, v.y - oy)); nrm.append((n.x, n.z, n.y)); uvA.append(tuple(U0.data[li].uv)); uvB.append(tuple(U1.data[li].uv))
            idx.append(index[key])
        subs[m] += [idx[0], idx[2], idx[1]]
    with open(path, 'wb') as f:
        f.write(struct.pack('<ii', len(verts), NMAT))
        for i in range(len(verts)): f.write(struct.pack('<10f', *verts[i], *nrm[i], *uvA[i], *uvB[i]))
        for k in range(NMAT): f.write(struct.pack('<i', len(subs[k]))); f.write(struct.pack('<%di' % len(subs[k]), *subs[k]))
    print('export', path, len(verts), 'verts', sum(len(s) for s in subs.values()) // 3, 'tris', flush=True)
isleaf = lambda p: p.material_index in (M_DOOR, M_METAL)
export(modo, OUT + 'C7460Module.bytes', keep=lambda p: not isleaf(p))
HINGE = (XB, -DW / 2 + GAP, 0.0)
export(modo, OUT + 'C7460Leaf.bytes', keep=lambda p: isleaf(p) and p.center.x > 0, origin=HINGE)
export(room, OUT + 'C7460Room.bytes')
json.dump(dict(CS=CS, HW=HW, H=H, DW=DW, DH=DH, JT=JT, XF=XF, XB=XB, XR=XR, LT=LT, RD=RD, RW=RWh, RH=RHt, PX=PX, PY=PY, hinge=[HINGE[0], HINGE[2], HINGE[1]],
               handle=[XF, HZ, HY], lamp_w=LAMPW, spp=SPP, res=RES, resr=RESR, device=dev), open(OUT + 'corridor.json', 'w'), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=OUT + 'corridor.blend')
print('DONE', flush=True)
