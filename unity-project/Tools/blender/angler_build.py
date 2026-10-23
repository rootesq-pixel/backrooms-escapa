# v7.3.0: El Farolero (diseño propio). Modelado procedural en Blender 4.2 (metabolas + remallado de vóxeles + booleanas),
# dientes de aguja, aletas desgarradas, ilicio carnoso con esca; texturas procedurales propias horneadas con Cycles en la GPU.
# Coordenadas Blender: frente = -Y, arriba = +Z, metros.  Salida: Farolero.fbx + FaroleroSkin_{a,n,m,e}.png
import bpy, bmesh, sys, os, math, random
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]; os.makedirs(OUT, exist_ok=True)
TEX = int(os.environ.get('ANG_TEX', '2048')); SKIP_BAKE = os.environ.get('ANG_SKIP_BAKE') == '1'
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
log = open(os.path.join(OUT, 'angler_build.log'), 'w')
def L(*a): log.write(' '.join(str(x) for x in a) + '\n'); log.flush()
rnd = random.Random(7303)

def activate(o):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
def apply_mods(o):
    activate(o)
    for m in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)
def metaball(name, elems, res=0.07):
    mb = bpy.data.metaballs.new(name); mb.resolution = res; mb.render_resolution = res; mb.threshold = 0.6
    o = bpy.data.objects.new(name, mb); sc.collection.objects.link(o)
    for co, sz, r in elems:
        e = mb.elements.new(); e.type = 'ELLIPSOID'; e.co = co; e.radius = r; e.size_x, e.size_y, e.size_z = sz; e.stiffness = 2.0
    bpy.context.view_layer.update(); activate(o); bpy.ops.object.convert(target='MESH')
    m = bpy.context.view_layer.objects.active; m.name = name; return m
def ellipsoid(name, c, r, seg=48):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=seg // 2, radius=1.0)
    for v in bm.verts: v.co = Vector((c[0] + v.co.x * r[0], c[1] + v.co.y * r[1], c[2] + v.co.z * r[2]))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); return o
def boolean(o, cutter, op='DIFFERENCE'):
    m = o.modifiers.new('B', 'BOOLEAN'); m.operation = op; m.object = cutter; m.solver = 'EXACT'; apply_mods(o)
    bpy.data.objects.remove(cutter)
def remesh(o, vox, smooth=6, fac=0.6):
    m = o.modifiers.new('R', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = vox; m.use_smooth_shade = True
    if smooth:
        s = o.modifiers.new('S', 'SMOOTH'); s.factor = fac; s.iterations = smooth
    apply_mods(o)
def decimate(o, faces):
    n = len(o.data.polygons)
    if n > faces:
        m = o.modifiers.new('D', 'DECIMATE'); m.ratio = faces / n; apply_mods(o)
    for p in o.data.polygons: p.use_smooth = True
def bounds(o):
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    return tuple(round(min(v[i] for v in vs), 2) for i in range(3)), tuple(round(max(v[i] for v in vs), 2) for i in range(3))

# ---------------- cuerpo (cabeza enorme que se estrecha hacia la cola)
CAV_C, CAV_R = Vector((0, -1.75, -0.78)), Vector((1.22, 1.75, 0.92))
body = metaball('Body', [
    ((0, -0.75, 0.25), (1.7, 2.15, 1.65), 1.0),     # cráneo
    ((0, -1.55, 0.75), (1.25, 1.35, 1.05), 0.9),    # frente abombada
    ((0, 0.35, -0.35), (1.45, 1.5, 1.05), 1.0),     # vientre colgante
    ((0, 1.35, 0.1), (1.2, 1.55, 1.2), 1.0),
    ((0, 2.3, 0.12), (0.9, 1.4, 1.0), 1.0),
    ((0, 3.1, 0.15), (0.62, 1.3, 0.72), 1.0),
    ((0, 3.8, 0.18), (0.42, 1.1, 0.52), 1.0),
    ((0, 4.4, 0.2), (0.28, 0.9, 0.36), 1.0),
    ((0, 4.9, 0.2), (0.18, 0.6, 0.26), 1.0),        # pedúnculo caudal
    ((0.85, -1.15, 0.75), (0.45, 0.6, 0.4), 0.6),   # cejas sobre los ojos
    ((-0.85, -1.15, 0.75), (0.45, 0.6, 0.4), 0.6),
], 0.06)
remesh(body, 0.05, 4)
L('body raw', bounds(body))
boolean(body, ellipsoid('Cav', CAV_C, CAV_R))
remesh(body, 0.035, 6, 0.5)
# ---------------- mandíbula inferior: cuenco enorme que sobresale (prognatismo)
HINGE = Vector((0, 0.35, -0.62))
jaw = ellipsoid('Jaw', (0, -1.55, -1.02), (1.5, 2.15, 0.78), 64)
remesh(jaw, 0.05, 0)
boolean(jaw, ellipsoid('JC', (0, -1.62, -0.66), (1.32, 1.95, 0.7), 64))
cut = ellipsoid('JTop', (0, -1.0, 0.6), (3.0, 4.0, 1.25), 32)   # recorta el borde por arriba (línea del labio)
boolean(jaw, cut)
chin = metaball('Chin', [((0, -3.45, -1.15), (0.9, 0.55, 0.5), 0.8), ((0, -2.9, -1.45), (1.2, 0.9, 0.45), 0.8), ((0, -3.2, -1.62), (0.55, 0.5, 0.35), 0.7), ((0, -1.6, -1.65), (0.9, 1.4, 0.3), 0.7)], 0.06)
m = jaw.modifiers.new('U', 'BOOLEAN'); m.operation = 'UNION'; m.object = chin; m.solver = 'EXACT'; apply_mods(jaw); bpy.data.objects.remove(chin)
remesh(jaw, 0.03, 6, 0.5)
L('jaw', bounds(jaw))
decimate(body, 26000); decimate(jaw, 9000)

def ell_s(p, c, r): d = (p - c); return math.sqrt((d.x / r.x) ** 2 + (d.y / r.y) ** 2 + (d.z / r.z) ** 2)

# ---------------- dientes de aguja (translúcidos, irregulares, superpuestos)
def tooth(bm, base, d, length, r0, bend, sides=6, segs=7):
    d = d.normalized(); rings = []
    pts = [base + d * length * (k / segs) + bend * length * 0.22 * (k / segs) ** 2 for k in range(segs + 1)]
    for k, p in enumerate(pts):
        t = k / segs; r = max(r0 * (1 - t) ** 1.15, 0.002)
        tg = (pts[min(k + 1, segs)] - pts[max(k - 1, 0)]).normalized()
        a = tg.orthogonal().normalized(); b = tg.cross(a)
        rings.append([bm.verts.new(p + (a * math.cos(2 * math.pi * s / sides) + b * math.sin(2 * math.pi * s / sides)) * r) for s in range(sides)])
    for k in range(segs):
        for s in range(sides):
            bm.faces.new((rings[k][s], rings[k][(s + 1) % sides], rings[k + 1][(s + 1) % sides], rings[k + 1][s]))
    bm.faces.new(list(reversed(rings[0])))
def rim_points(o, test, n, zsel):
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    cand = [v for v in vs if test(v)]
    cand.sort(key=zsel)
    return cand
def teeth(name, o, cand, count, up, out_c, lmin, lmax):
    bm = bmesh.new(); used = []
    pool = cand[:]
    rnd.shuffle(pool)
    for p in pool:
        if len(used) >= count: break
        if any((p - q).length < 0.085 for q in used): continue
        used.append(p)
    for p in used:
        outward = Vector((p.x - out_c.x, p.y - out_c.y, 0)).normalized()
        front = max(0.0, -p.y - 1.0) / 2.0     # más largos delante
        L_ = rnd.uniform(lmin, lmax) * (0.6 + 0.8 * front) * (1.6 if rnd.random() < 0.12 else 1.0)
        d = up * 1.0 + outward * rnd.uniform(0.1, 0.9) + Vector((rnd.uniform(-0.3, 0.3), rnd.uniform(-0.45, 0.15), 0))
        bend = -outward * rnd.uniform(0.6, 1.6) + Vector((rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), 0))
        tooth(bm, p - up * 0.03, d, L_, rnd.uniform(0.014, 0.032) * (0.8 + 0.4 * L_), bend)
        if rnd.random() < 0.35:   # diente secundario detrás (fila interior, superpuesto)
            q = p - outward * 0.09
            tooth(bm, q, up + outward * rnd.uniform(-0.4, 0.5) + Vector((rnd.uniform(-0.3, 0.3), 0, 0)), L_ * rnd.uniform(0.35, 0.8), 0.014, -outward * rnd.uniform(0.3, 1.2))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    t = bpy.data.objects.new(name, me); sc.collection.objects.link(t)
    for pl in me.polygons: pl.use_smooth = True
    L(name, len(used), 'teeth'); return t
# borde del paladar: vértices del cuerpo sobre la superficie de la cavidad, en su banda más baja
def rim_edge(o, c, r, extra):
    bm = bmesh.new(); bm.from_mesh(o.data); mw = o.matrix_world; out_ = []
    for v in bm.verts:
        p = mw @ v.co
        if abs(ell_s(p, c, r) - 1) > 0.05 or not extra(p): continue
        ring = set([v]); fr = [v]
        for _ in range(3):
            fr = [e.other_vert(x) for x in fr for e in x.link_edges]; ring.update(fr)
        if any(ell_s(mw @ q.co, c, r) > 1.08 for q in ring): out_.append(p)
    bm.free(); return out_
up_c = rim_edge(body, CAV_C, CAV_R, lambda v: v.y < 0.1)
L('upper rim cand', len(up_c))
teethU = teeth('TeethUpper', body, up_c, 50, Vector((0, 0, -1)), Vector((0, -1.6, 0)), 0.35, 0.9)
JIN_C, JIN_R = Vector((0, -1.62, -0.66)), Vector((1.32, 1.95, 0.7))
lo_c = rim_points(jaw, lambda v: abs(ell_s(v, JIN_C, JIN_R) - 1) < 0.07 and v.y < 0.1, 60, lambda v: -v.z)
teethL = teeth('TeethLower', jaw, lo_c[:1100], 60, Vector((0, 0, 1)), Vector((0, -1.6, 0)), 0.45, 1.15)

# ---------------- ojos pequeños (lechosos, sin vida): sobre la superficie lateral
bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
eyes = []
for sx, nm in ((1, 'EyeL'), (-1, 'EyeR')):
    org = Vector((sx * 4.0, -1.25, 0.42)); hit = bvh.ray_cast(org, Vector((-sx, 0, 0)))
    p = hit[0] if hit[0] is not None else Vector((sx * 1.4, -1.25, 0.42))
    e = ellipsoid(nm, p - Vector((sx * 0.03, 0, 0)), (0.12, 0.12, 0.11), 24); eyes.append(e)
    for pl in e.data.polygons: pl.use_smooth = True
# ---------------- ilicio carnoso y esca
hit = bvh.ray_cast(Vector((0, -1.35, 6)), Vector((0, 0, -1))); top = hit[0] if hit[0] is not None else Vector((0, -1.35, 1.8))
BASE = top - Vector((0, 0, 0.06))
ctrl = [BASE, BASE + Vector((0, -0.25, 0.9)), BASE + Vector((0, -1.1, 1.65)), BASE + Vector((0, -2.25, 1.55)), BASE + Vector((0, -2.85, 0.95)), BASE + Vector((0, -2.95, 0.45))]
def catmull(p, n):
    out = []
    P = [p[0]] + p + [p[-1]]
    for i in range(1, len(P) - 2):
        for k in range(n):
            t = k / n; p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(p[-1]); return out
path = catmull(ctrl, 10)
bm = bmesh.new(); rings = []; sides = 10
for k, p in enumerate(path):
    t = k / (len(path) - 1)
    r = 0.11 * (1 - t) + 0.035 * t + 0.012 * math.sin(t * 37) + (0.06 * (1 - t / 0.08) if t < 0.08 else 0)
    tg = (path[min(k + 1, len(path) - 1)] - path[max(k - 1, 0)]).normalized()
    a = Vector((1, 0, 0)); a = (a - tg * a.dot(tg)).normalized(); b = tg.cross(a)
    rings.append([bm.verts.new(p + (a * math.cos(2 * math.pi * s / sides) + b * math.sin(2 * math.pi * s / sides)) * r) for s in range(sides)])
for k in range(len(rings) - 1):
    for s in range(sides): bm.faces.new((rings[k][s], rings[k][(s + 1) % sides], rings[k + 1][(s + 1) % sides], rings[k + 1][s]))
bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
me = bpy.data.meshes.new('Stalk'); bm.to_mesh(me); bm.free()
stalk = bpy.data.objects.new('Stalk', me); sc.collection.objects.link(stalk)
for pl in me.polygons: pl.use_smooth = True
TIP = path[-1]
esca = metaball('Esca', [((TIP.x, TIP.y, TIP.z - 0.16), (0.2, 0.2, 0.26), 0.75), ((TIP.x, TIP.y, TIP.z - 0.02), (0.08, 0.08, 0.12), 0.5),
                         ((TIP.x + 0.12, TIP.y - 0.05, TIP.z - 0.36), (0.05, 0.05, 0.16), 0.5), ((TIP.x - 0.1, TIP.y + 0.06, TIP.z - 0.38), (0.05, 0.05, 0.2), 0.5),
                         ((TIP.x, TIP.y - 0.1, TIP.z - 0.42), (0.04, 0.04, 0.22), 0.5)], 0.025)
for pl in esca.data.polygons: pl.use_smooth = True
L('stalk base', tuple(round(x, 2) for x in BASE), 'tip', tuple(round(x, 2) for x in TIP), 'esca', bounds(esca))

# ---------------- aletas con membrana y radios, bordes desgarrados
def fin(name, root_a, root_b, dirs, lens, normal_hint, thick=0.02):
    # dirs: lista de direcciones de radio (de root_a a root_b se reparte la base)
    n = len(dirs); rows = 7
    bm = bmesh.new(); grid = []; lay = bm.verts.layers.float.new('ray')
    cols = []
    for i in range(n):
        cols.append((i, dirs[i], lens[i], 1.0))
        if i < n - 1:
            dm = (dirs[i] + dirs[i + 1]).normalized(); lm = min(lens[i], lens[i + 1]) * rnd.uniform(0.78, 0.95)
            if rnd.random() < 0.18: lm *= rnd.uniform(0.4, 0.65)     # desgarro profundo
            cols.append((i + 0.5, dm, lm, 0.0))
    for ci, (fi, d, ln, isray) in enumerate(cols):
        base = root_a.lerp(root_b, fi / max(n - 1, 1)); col = []
        for k in range(rows + 1):
            t = k / rows
            wob = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * 0.02 * t
            col.append(bm.verts.new(base + d * ln * t + wob))
        grid.append((col, fi))
    for col, fi in grid:
        for v in col: v[lay] = fi
    for c in range(len(grid) - 1):
        a, b = grid[c][0], grid[c + 1][0]
        for k in range(rows): bm.faces.new((a[k], b[k], b[k + 1], a[k + 1]))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o)
    nrm = sum((p.normal for p in me.polygons), Vector()).normalized()
    if nrm.dot(normal_hint) < 0:
        activate(o); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.flip_normals(); bpy.ops.object.mode_set(mode='OBJECT')
    s = o.modifiers.new('So', 'SOLIDIFY'); s.thickness = thick; s.offset = 0; apply_mods(o)
    for p in o.data.polygons: p.use_smooth = True
    return o
def fan(c, axis_a, axis_b, a0, a1, n, lmin, lmax):
    out = []
    for i in range(n):
        a = math.radians(a0 + (a1 - a0) * i / (n - 1)) + rnd.uniform(-0.04, 0.04)
        out.append((axis_a * math.cos(a) + axis_b * math.sin(a)).normalized())
    return out, [rnd.uniform(lmin, lmax) for _ in range(n)]
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
def surf(y, from_z, dz):
    h = bvh.ray_cast(Vector((0, y, from_z)), Vector((0, 0, dz)))
    return h[0] if h[0] is not None else Vector((0, y, 0))
def tailroot():
    best = None
    for k in range(60):
        y = 5.6 - k * 0.02; h = bvh.ray_cast(Vector((0, y, 4)), Vector((0, 0, -1)))
        if h[0] is not None: return y
    return 5.0
d, l = fan(None, Y, Z, -62, 62, 13, 1.25, 1.85); l = [x * (1.0 + 0.25 * abs(i - 6) / 6) for i, x in enumerate(l)]
TY = tailroot() - 0.12; tail = fin('Tail', Vector((0, TY, 0.05)), Vector((0, TY, 0.36)), d, l, X)
d, l = fan(None, Y, Z, 25, 80, 9, 0.6, 1.05)
DA, DB = surf(2.0, 4, -1) - Vector((0, 0, 0.08)), surf(3.4, 4, -1) - Vector((0, 0, 0.08)); dorsal = fin('Dorsal', DA, DB, d, l, X)
d, l = fan(None, Y, -Z, 20, 75, 8, 0.5, 0.85)
AA, AB = surf(3.0, -4, 1) + Vector((0, 0, 0.08)), surf(3.9, -4, 1) + Vector((0, 0, 0.08)); anal = fin('Anal', AA, AB, d, l, X)
pecs = []
for sx, nm in ((1, 'PecL'), (-1, 'PecR')):
    ax_out = Vector((sx * 0.75, 0.55, -0.1)).normalized()
    d, l = fan(None, ax_out, Vector((0, 0.35, 1)).normalized(), -50, 55, 10, 0.75, 1.2)
    hit = bvh.ray_cast(Vector((sx * 4, 0.95, -0.15)), Vector((-sx, 0, 0))); rp = hit[0] if hit[0] is not None else Vector((sx * 1.2, 0.95, -0.15))
    rp = rp - Vector((sx * 0.06, 0, 0))
    pecs.append(fin(nm, rp + Vector((0, -0.12, -0.3)), rp + Vector((0, 0.12, 0.3)), d, l, Vector((0, 0, 1))))
fins = [tail, dorsal, anal] + pecs
L('tail', bounds(tail), 'pec', bounds(pecs[0]))

# ---------------- máscara por vértice (R boca, G aleta, B fase de radio)
def mask_attr(o, fn):
    me = o.data
    if 'Mask' in me.color_attributes: me.color_attributes.remove(me.color_attributes['Mask'])
    ca = me.color_attributes.new('Mask', 'FLOAT_COLOR', 'POINT')
    ray = me.attributes.get('ray')
    for i, v in enumerate(me.vertices):
        p = o.matrix_world @ v.co
        ca.data[i].color = fn(p, ray.data[i].value if ray is not None else 0.0)
mask_attr(body, lambda p, r: (1.0 if ell_s(p, CAV_C, CAV_R) < 1.06 else 0.0, 0, 0, 1))
mask_attr(jaw, lambda p, r: (1.0 if ell_s(p, JIN_C, JIN_R) < 1.07 else 0.0, 0, 0, 1))
for f in fins: mask_attr(f, lambda p, r: (0, 1, (r % 1.0), 1))
mask_attr(stalk, lambda p, r: (0, 0, 0, 1))

# ---------------- material procedural de la piel (coordenadas de mundo)
def N(nt, t, **kw):
    n = nt.nodes.new(t)
    for k, v in kw.items(): setattr(n, k, v)
    return n
skin = bpy.data.materials.new('FaroleroSkin'); skin.use_nodes = True; nt = skin.node_tree; nt.nodes.clear(); Lk = nt.links.new
out = N(nt, 'ShaderNodeOutputMaterial'); bsdf = N(nt, 'ShaderNodeBsdfPrincipled'); geo = N(nt, 'ShaderNodeNewGeometry')
attr = N(nt, 'ShaderNodeAttribute'); attr.attribute_name = 'Mask'; sepm = N(nt, 'ShaderNodeSeparateColor'); Lk(attr.outputs['Color'], sepm.inputs[0])
MOUTH, FIN, RAY = sepm.outputs[0], sepm.outputs[1], sepm.outputs[2]
sep = N(nt, 'ShaderNodeSeparateXYZ'); Lk(geo.outputs['Position'], sep.inputs[0]); PX, PY, PZ = sep.outputs
def math_(op, a, b=None, c=None, clamp=False):
    n = N(nt, 'ShaderNodeMath', operation=op); n.use_clamp = clamp
    for i, x in enumerate([a, b, c]):
        if x is None: continue
        if isinstance(x, (int, float)): n.inputs[i].default_value = x
        else: Lk(x, n.inputs[i])
    return n.outputs[0]
def mr(x, a, b, c, d):
    n = N(nt, 'ShaderNodeMapRange'); n.clamp = True
    for i, v in zip(range(1, 5), (a, b, c, d)): n.inputs[i].default_value = v
    Lk(x, n.inputs[0]); return n.outputs[0]
def noise(scale, detail=4, rough=0.55, dist=0.0):
    n = N(nt, 'ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail; n.inputs['Roughness'].default_value = rough; n.inputs['Distortion'].default_value = dist
    Lk(geo.outputs['Position'], n.inputs['Vector']); return n.outputs['Fac']
def voro(scale, feat='F1', metric='EUCLIDEAN', out_='Distance', vec=None):
    n = N(nt, 'ShaderNodeTexVoronoi'); n.feature = feat; n.distance = metric; n.inputs['Scale'].default_value = scale
    Lk(vec if vec is not None else geo.outputs['Position'], n.inputs['Vector']); return n.outputs[out_]
def mixc(f, a, b):
    n = N(nt, 'ShaderNodeMix', data_type='RGBA')
    if isinstance(f, (int, float)): n.inputs[0].default_value = f
    else: Lk(f, n.inputs[0])
    for i, c in ((6, a), (7, b)):
        if isinstance(c, tuple): n.inputs[i].default_value = c + (1,)
        else: Lk(c, n.inputs[i])
    return n.outputs[2]
# texturas base
mott = noise(1.6, 5, 0.6)
bumpsV = voro(22, 'SMOOTH_F1'); tub = mr(bumpsV, 0.0, 0.32, 1, 0)                 # tubérculos pequeños
big = noise(5, 6, 0.62, 0.4)
# venas: bordes de voronoi distorsionados
wv = N(nt, 'ShaderNodeTexNoise'); wv.inputs['Scale'].default_value = 3.0; wv.inputs['Detail'].default_value = 3; Lk(geo.outputs['Position'], wv.inputs['Vector'])
vadd = N(nt, 'ShaderNodeVectorMath', operation='ADD'); Lk(geo.outputs['Position'], vadd.inputs[0]); 
vsc = N(nt, 'ShaderNodeVectorMath', operation='SCALE'); Lk(wv.outputs['Color'], vsc.inputs[0]); vsc.inputs['Scale'].default_value = 0.35; Lk(vsc.outputs[0], vadd.inputs[1])
vein_d = voro(2.6, 'DISTANCE_TO_EDGE', vec=vadd.outputs[0]); veins = mr(vein_d, 0.0, 0.018, 1, 0)
veins = math_('MULTIPLY', veins, mr(noise(1.2, 2), 0.42, 0.62, 0, 1))
# cicatrices: franjas finas en diagonal en unas pocas zonas
sc_w = N(nt, 'ShaderNodeTexWave'); sc_w.wave_type = 'BANDS'; sc_w.inputs['Scale'].default_value = 1.3; sc_w.inputs['Distortion'].default_value = 4.0; sc_w.inputs['Detail'].default_value = 1
Lk(geo.outputs['Position'], sc_w.inputs['Vector']); sc_w.bands_direction = 'DIAGONAL'
scars = math_('MULTIPLY', mr(sc_w.outputs['Fac'], 0.93, 0.985, 0, 1), mr(noise(0.9, 1), 0.66, 0.72, 0, 1))
# radios de las aletas
rayline = mr(math_('ABSOLUTE', math_('SUBTRACT', RAY, 0.0)), 0.0, 0.12, 1, 0)
belly = mr(PZ, -0.2, -1.1, 0, 1)
# color
col = mixc(mott, (0.008, 0.008, 0.010), (0.022, 0.019, 0.022))
col = mixc(math_('MULTIPLY', belly, 0.5), col, (0.04, 0.036, 0.036))
col = mixc(math_('MULTIPLY', tub, 0.2), col, (0.03, 0.028, 0.03))
col = mixc(math_('MULTIPLY', veins, 0.55), col, (0.075, 0.018, 0.03))
col = mixc(math_('MULTIPLY', scars, 0.8), col, (0.13, 0.115, 0.11))
fincol = mixc(math_('MULTIPLY', rayline, 0.7), mixc(noise(9, 4), (0.03, 0.028, 0.032), (0.06, 0.05, 0.052)), (0.012, 0.011, 0.013))
col = mixc(FIN, col, fincol)
gum = mixc(noise(8, 4), (0.05, 0.008, 0.01), (0.10, 0.018, 0.022))
col = mixc(MOUTH, col, gum)
# altura (normales)
h = math_('ADD', math_('MULTIPLY', tub, 0.18), math_('MULTIPLY', big, 0.5))
h = math_('ADD', h, math_('MULTIPLY', veins, 0.25))
h = math_('SUBTRACT', h, math_('MULTIPLY', scars, 0.6))
h = math_('ADD', h, math_('MULTIPLY', FIN, math_('MULTIPLY', rayline, 0.8)))
wr = N(nt, 'ShaderNodeTexWave'); wr.wave_type = 'RINGS'; wr.inputs['Scale'].default_value = 5; wr.inputs['Distortion'].default_value = 14; Lk(geo.outputs['Position'], wr.inputs['Vector'])
h = math_('ADD', h, math_('MULTIPLY', MOUTH, math_('MULTIPLY', wr.outputs['Fac'], 0.25)))
col = mixc(math_('MULTIPLY', MOUTH, math_('MULTIPLY', wr.outputs['Fac'], 0.6)), col, (0.02, 0.004, 0.006))
bump = N(nt, 'ShaderNodeBump'); bump.inputs['Strength'].default_value = 1.0; bump.inputs['Distance'].default_value = 0.02; Lk(h, bump.inputs['Height'])
rough = mr(noise(14, 4), 0, 1, 0.16, 0.34)
rough = math_('ADD', rough, math_('MULTIPLY', scars, 0.35))
rough = math_('ADD', rough, math_('MULTIPLY', FIN, 0.12))
# fotóforos: puntos tenues en dos hileras a los lados y en el borde de la mandíbula
cells = voro(7.0, 'F1'); dots = mr(cells, 0.035, 0.06, 1, 0)
band = math_('MULTIPLY', mr(math_('ABSOLUTE', math_('SUBTRACT', PZ, -0.35)), 0.15, 0.3, 1, 0), mr(PY, -1.0, 3.6, 1, 1))
band = math_('MULTIPLY', band, mr(PY, 3.4, 3.9, 1, 0))
side = mr(math_('ABSOLUTE', PX), 0.5, 0.75, 0, 1)
photo = math_('MULTIPLY', math_('MULTIPLY', dots, band), math_('MULTIPLY', side, math_('SUBTRACT', 1, FIN)))
emc = mixc(0.0, (0, 0, 0), (0, 0, 0)); 
emn = N(nt, 'ShaderNodeEmission'); emn.name = 'PhotoOut'
emcol = N(nt, 'ShaderNodeMix', data_type='RGBA'); Lk(photo, emcol.inputs[0]); emcol.inputs[6].default_value = (0, 0, 0, 1); emcol.inputs[7].default_value = (0.25, 0.9, 0.75, 1)
Lk(emcol.outputs[2], emn.inputs['Color'])
Lk(col, bsdf.inputs['Base Color']); Lk(rough, bsdf.inputs['Roughness']); Lk(bump.outputs['Normal'], bsdf.inputs['Normal'])
bsdf.inputs['Specular IOR Level'].default_value = 0.6
Lk(bsdf.outputs[0], out.inputs['Surface'])
skin_objs = [body, jaw, stalk] + fins
for o in skin_objs: o.data.materials.clear(); o.data.materials.append(skin)
for o, nm in ((teethU, 'FaroleroTeeth'), (teethL, 'FaroleroTeeth'), (eyes[0], 'FaroleroEye'), (eyes[1], 'FaroleroEye'), (esca, 'FaroleroEsca')):
    m = bpy.data.materials.get(nm) or bpy.data.materials.new(nm); o.data.materials.clear(); o.data.materials.append(m)

# ---------------- UV conjunto y horneado (GPU)
bpy.ops.object.select_all(action='DESELECT')
for o in skin_objs: o.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=0.004, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
bpy.ops.object.mode_set(mode='OBJECT')
L('faces', {o.name: len(o.data.polygons) for o in skin_objs + [teethU, teethL, esca]})
sc.render.engine = 'CYCLES'
prefs = bpy.context.preferences.addons['cycles'].preferences
for t in ('OPTIX', 'CUDA'):
    try:
        prefs.compute_device_type = t; prefs.get_devices()
        if any(d.type == t for d in prefs.devices): break
    except Exception as e: L('dev', t, e)
for d in prefs.devices: d.use = (d.type != 'CPU')
sc.cycles.device = 'GPU' if any(d.use for d in prefs.devices) else 'CPU'
L('device', prefs.compute_device_type, [(d.name, d.type, d.use) for d in prefs.devices])
if not SKIP_BAKE:
    S = TEX; imgs = {}
    for key in ('albedo', 'normal', 'rough', 'ao', 'emit'):
        im = bpy.data.images.new('A_' + key, S, S, alpha=False, float_buffer=(key == 'normal'))
        if key != 'albedo': im.colorspace_settings.name = 'Non-Color'
        imgs[key] = im
    tex = nt.nodes.new('ShaderNodeTexImage'); nt.nodes.active = tex
    bpy.ops.object.select_all(action='DESELECT')
    for o in skin_objs: o.select_set(True)
    bpy.context.view_layer.objects.active = body
    sc.render.bake.margin = 8; sc.render.bake.use_selected_to_active = False
    for key, typ, samples in (('albedo', 'DIFFUSE', 4), ('normal', 'NORMAL', 4), ('rough', 'ROUGHNESS', 4), ('ao', 'AO', 64), ('emit', 'EMIT', 4)):
        tex.image = imgs[key]; sc.cycles.samples = samples
        if key == 'emit': Lk(emn.outputs[0], out.inputs['Surface'])
        kw = dict(pass_filter={'COLOR'}) if typ == 'DIFFUSE' else {}
        bpy.ops.object.bake(type=typ, **kw)
        if key == 'emit': Lk(bsdf.outputs[0], out.inputs['Surface'])
        L('baked', key)
    nt.nodes.remove(tex)
    def arr(im): return np.array(im.pixels[:], dtype=np.float32).reshape(S, S, 4)
    def save(px, fn):
        im = bpy.data.images.new(fn, S, S, alpha=True); im.colorspace_settings.name = 'sRGB' if fn.endswith('_a') or fn.endswith('_e') else 'Non-Color'
        im.pixels[:] = px.ravel(); im.filepath_raw = os.path.join(OUT, fn + '.png'); im.file_format = 'PNG'; im.save()
    a = arr(imgs['albedo']); nrm = arr(imgs['normal']); r = arr(imgs['rough']); ao = arr(imgs['ao']); em = arr(imgs['emit'])
    alb = a.copy(); alb[..., :3] *= (0.6 + 0.4 * ao[..., :1]); alb[..., 3] = 1; save(alb, 'FaroleroSkin_a')
    nrm[..., 3] = 1; save(nrm, 'FaroleroSkin_n')
    mk = np.zeros_like(a); mk[..., 1] = ao[..., 0]; mk[..., 3] = 1 - r[..., 0]; save(mk, 'FaroleroSkin_m')
    em[..., 3] = 1; save(em, 'FaroleroSkin_e')
    L('albedo mean', alb[..., :3].mean(axis=(0, 1)), 'smooth', mk[..., 3].mean(), 'emit max', em[..., :3].max())

# ---------------- pivotes y jerarquía (mandíbula en la bisagra, ilicio en su base, aletas en su raíz)
def set_origin(o, p):
    mw = o.matrix_world.copy(); off = mw.inverted() @ p
    o.data.transform(Matrix.Translation(-off)); o.matrix_world = mw @ Matrix.Translation(off)
set_origin(jaw, HINGE); set_origin(stalk, BASE)
set_origin(tail, Vector((0, TY, 0.2))); set_origin(dorsal, DA); set_origin(anal, AA)
for p in pecs:
    vs = [p.matrix_world @ v.co for v in p.data.vertices]; c = sum(vs, Vector()) / len(vs)
    root = min(vs, key=lambda v: abs(v.x)); set_origin(p, Vector((root.x, c.y * 0 + root.y, root.z)))
for e in eyes:
    vs = [e.matrix_world @ v.co for v in e.data.vertices]; set_origin(e, sum(vs, Vector()) / len(vs))
vs = [esca.matrix_world @ v.co for v in esca.data.vertices]; set_origin(esca, sum(vs, Vector()) / len(vs))
def parent(c, p):
    mw = c.matrix_world.copy(); c.parent = p; c.matrix_parent_inverse = p.matrix_world.inverted(); c.matrix_world = mw
parent(teethL, jaw); parent(esca, stalk)

# ---------------- vista previa
try:
    w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.02, 0.03, 0.035, 1)
    for nm, loc, en in (('K', (6, -6, 4), 3000), ('R', (-5, 3, 3), 1500), ('F', (0, -7, -2), 900)):
        l_ = bpy.data.objects.new(nm, bpy.data.lights.new(nm, 'POINT')); l_.data.energy = en; l_.location = loc; sc.collection.objects.link(l_)
    tm = bpy.data.materials['FaroleroTeeth']; tm.use_nodes = True; tm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.75, 0.78, 0.7, 1); tm.node_tree.nodes['Principled BSDF'].inputs['Transmission Weight'].default_value = 0.6; tm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.15
    em2 = bpy.data.materials['FaroleroEsca']; em2.use_nodes = True; em2.node_tree.nodes['Principled BSDF'].inputs['Emission Color'].default_value = (0.4, 1, 0.8, 1); em2.node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = 8
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
    sc.render.resolution_x = 1200; sc.render.resolution_y = 800; sc.cycles.samples = int(os.environ.get('ANG_PREV_SAMPLES', '48'))
    def look(c, t):
        cam.location = c; d = (Vector(t) - Vector(c)); cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    jaw.rotation_euler = (math.radians(float(os.environ.get('ANG_PREV_JAW', '30'))), 0, 0); bpy.context.view_layer.update()
    for nm, c, t in (('side', (9.5, -2.5, 0.8), (0, 0.6, 0)), ('front', (1.2, -9.5, -0.6), (0, -1.5, -0.3)), ('mouth', (0.5, -6.2, -0.9), (0, -1.5, -0.7))):
        look(c, t); sc.render.filepath = os.path.join(OUT, 'preview_' + nm + '.png'); bpy.ops.render.render(write_still=True)
    jaw.rotation_euler = (0, 0, 0); bpy.context.view_layer.update()
    L('preview ok')
except Exception as e: L('preview fail', e)

# ---------------- exportación
for o in list(sc.objects):
    if o.type not in ('MESH',): bpy.data.objects.remove(o)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Farolero.fbx'), use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL',
                         bake_space_transform=True, mesh_smooth_type='FACE', use_tspace=False, path_mode='STRIP', bake_anim=False, use_mesh_modifiers=True)
L('exported'); log.close()
