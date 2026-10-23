# v7.3.0: barca de remos con fueraborda viejo (diseño propio). Casco de tingladillo (tablas solapadas) con remaches,
# cuadernas, bancadas, suelo de tablas, regala, toletes, dos remos, poste con farol y motor fueraborda modelado.
# Pintura gastada y desconchada, veta de la madera, algas en la línea de flotación: texturas procedurales horneadas en la GPU.
# Se construye en coordenadas de Unity (x, y arriba, z proa) y al final se gira a Blender (-Y delante, Z arriba).
import bpy, bmesh, sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from props_common import *
import props_common as pc
from mathutils import Vector, Matrix, noise as mnoise
argv = sys.argv[sys.argv.index('--') + 1:]; OUT = argv[0]
TEX = int(os.environ.get('PROP_TEX', '2048')); SKIP = os.environ.get('PROP_SKIP_BAKE') == '1'
init(OUT, 'boat_build.log'); rnd = random.Random(7312)
TOB = Matrix.Rotation(math.radians(90), 4, 'X')   # Unity (x, y, z) -> Blender (x, -z, y)
WL = -0.02

# ---------------- forma del casco (la misma que el casco antiguo del juego: colisiones, asientos y farol no cambian)
def W(t): return (0.78 + (0.06 - 0.78) * min(1.0, max(0.0, (t - 0.55) / 0.45)) ** 1.6) * ((0.88 + 0.12 * t / 0.08) if t < 0.08 else 1.0)
def KEEL(t): return -0.42 + (-0.12 + 0.42) * min(1.0, max(0.0, (t - 0.6) / 0.4)) ** 1.4
def GUN(t): return 0.32 + t ** 4 * 0.18
def Z(t): return -2.0 + 4.0 * t
def catmull(pts, n):
    out = []; P = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for k in range(n):
            u = k / n
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u ** 3))
    out.append(pts[-1]); return out
_cache = {}
def section(t):
    if t in _cache: return _cache[t]
    w, k, g = W(t), KEEL(t), GUN(t)
    pts = [Vector((0.0, k + 0.03)), Vector((0.45 * w, k + 0.05)), Vector((0.86 * w, -0.05)), Vector((w, g))]
    poly = catmull(pts, 24); cum = [0.0]
    for i in range(1, len(poly)): cum.append(cum[-1] + (poly[i] - poly[i - 1]).length)
    _cache[t] = (poly, cum); return _cache[t]
def sec(t, s, side=1):
    """punto (Unity) y normal hacia fuera del casco en la estación t (0 popa .. 1 proa), s (0 quilla .. 1 borda)"""
    poly, cum = section(t); L_ = cum[-1]; d = max(0.0, min(1.0, s)) * L_
    i = 1
    while i < len(cum) - 1 and cum[i] < d: i += 1
    f = (d - cum[i - 1]) / max(1e-9, cum[i] - cum[i - 1]); p = poly[i - 1].lerp(poly[i], f)
    tg = (poly[i] - poly[i - 1]).normalized(); n2 = Vector((tg.y, -tg.x))
    if n2.x < 0 or (abs(n2.x) < 1e-3 and n2.y > 0): n2 = -n2
    return Vector((side * p.x, p.y, Z(t))), Vector((side * n2.x, n2.y, 0.0)).normalized(), L_
def inner_x(t, y, off=0.03):
    best = None
    for j in range(201):
        p, n, _ = sec(t, j / 200)
        if best is None or abs(p.y - y) < abs(best[0].y - y): best = (p, n)
    return best[0].x - off

parts = []
def make(name, verts, faces, mask, grain):
    bm = bmesh.new(); vs = [bm.verts.new(v) for v in verts]; bm.verts.ensure_lookup_table()
    for f in faces:
        try: bm.faces.new([vs[i] for i in f])
        except ValueError: pass
    o = obj_from_bm(name, bm)
    me = o.data
    for nm, arr in (('Mask', mask), ('Grain', grain)):
        a = me.color_attributes.new(nm, 'FLOAT_COLOR', 'POINT')
        for i, c in enumerate(arr): a.data[i].color = c
    parts.append(o); return o
def ribbon_solid(name, P, Nn, th, region, grainf, ext_outer=1.0, rid=None):
    """P[i][j] puntos (cara vista), Nn[i][j] normal hacia fuera; grosor th hacia -Nn"""
    I, J = len(P), len(P[0]); verts = []; mask = []; grain = []; rid = rnd.random() if rid is None else rid
    for layer in (0, 1):
        for i in range(I):
            for j in range(J):
                verts.append(P[i][j] - Nn[i][j] * th * layer)
                g = grainf(i, j); mask.append((region, ext_outer if layer == 0 else 0.0, rid, 1.0)); grain.append((g[0], g[1], rid, 1.0))
    idx = lambda l, i, j: l * I * J + i * J + j
    a, b, d = P[0][0], P[1][0], P[0][1]; flip = (b - a).cross(d - a).dot(Nn[0][0]) < 0
    faces = []
    def q(f, rev): faces.append(tuple(reversed(f)) if rev else f)
    for i in range(I - 1):
        for j in range(J - 1):
            q((idx(0, i, j), idx(0, i + 1, j), idx(0, i + 1, j + 1), idx(0, i, j + 1)), flip)
            q((idx(1, i, j), idx(1, i + 1, j), idx(1, i + 1, j + 1), idx(1, i, j + 1)), not flip)
    for i in range(I - 1):
        for j in (0, J - 1):
            q((idx(0, i, j), idx(1, i, j), idx(1, i + 1, j), idx(0, i + 1, j)), flip == (j == 0))
    for j in range(J - 1):
        for i in (0, I - 1):
            q((idx(0, i, j), idx(0, i, j + 1), idx(1, i, j + 1), idx(1, i, j)), flip == (i == 0))
    return make(name, verts, faces, mask, grain)
def boxp(name, lo, hi, region, bevel=0.006, seg=2, subd=0, axis=2):
    o = box(name, lo, hi, bevel, seg, subd); rid = rnd.random()
    set_attr(o, 'Mask', lambda co, n, i: (region, 0.0, rid, 1.0))
    set_attr(o, 'Grain', lambda co, n, i: (co[axis], co[(axis + 1) % 3] * 0.5 + co[(axis + 2) % 3] * 0.5, rid, 1.0)); parts.append(o); return o
def cylp(name, c, r, h, axis, region, seg=16, r2=None):
    o = cyl(name, c, r, h, axis, seg, r2); rid = rnd.random(); ax = 'XYZ'.index(axis)
    set_attr(o, 'Mask', lambda co, n, i: (region, 0.0, rid, 1.0))
    set_attr(o, 'Grain', lambda co, n, i: (co[ax], math.atan2(co[(ax + 1) % 3] - c[(ax + 1) % 3], co[(ax + 2) % 3] - c[(ax + 2) % 3]) * r, rid, 1.0)); parts.append(o); return o
def sweepp(name, pts, w, h, region, side=None):
    o = sweep(name, pts, w, h, Vector((0, 1, 0)), side=side); rid = rnd.random()
    acc = [0.0]
    for i in range(1, len(pts)): acc.append(acc[-1] + (pts[i] - pts[i - 1]).length)
    set_attr(o, 'Mask', lambda co, n, i: (region, 0.0, rid, 1.0))
    set_attr(o, 'Grain', lambda co, n, i: (co.z + co.y * 0.3, co.x * 0.7 + co.y * 0.7, rid, 1.0)); parts.append(o); return o

R_HULL, R_RIB, R_TRIM, R_WOOD, R_OAR, R_IRON, R_COWL, R_ALU, R_GLASS, R_POST = [k / 10 for k in range(10)]
NS = 64; TS = [i / NS for i in range(NS + 1)]
STRAKES = [0.0, 0.15, 0.29, 0.43, 0.57, 0.71, 0.855, 1.0]; LAP = 0.011; TH = 0.014
# ---------------- tracas solapadas (las dos bandas)
for side in (1, -1):
    for k in range(len(STRAKES) - 1):
        s0, s1 = STRAKES[k], STRAKES[k + 1]; ROWS = 4
        P = []; Nn = []; G = []
        for t in TS:
            row = []; nrow = []; grow = []
            _, _, Ls = sec(t, 0)
            ext = (0.022 / max(Ls, 0.05)) if k > 0 else 0.0           # la traca de arriba tapa el canto de la de abajo
            for r in range(ROWS + 1):
                f = r / ROWS; s = (s0 - ext) + (s1 - s0 + ext) * f
                p, n, _ = sec(t, s, side)
                off = LAP * (1 - f) if k > 0 else 0.0
                row.append(p + n * off); nrow.append(n); grow.append((Z(t), (s - (s0 - ext)) * Ls))
            P.append(row); Nn.append(nrow); G.append(grow)
        ribbon_solid('Strake', P, Nn, TH, R_HULL, lambda i, j, G=G: G[i][j])
# ---------------- espejo de popa
P = []; Nn = []
for j in range(13):
    s = j / 12; pl, _, _ = sec(0.0, s, 1); pr, _, _ = sec(0.0, s, -1)
    P.append([pr + Vector((0, 0, -0.012)), pl + Vector((0, 0, -0.012))]); Nn.append([Vector((0, 0, -1))] * 2)
ribbon_solid('Transom', P, Nn, -0.035, R_HULL, lambda i, j: (P[i][j].x, P[i][j].y, 0), ext_outer=1.0)
# ---------------- quilla y roda
kp = [Vector((0, KEEL(t) + 0.005, Z(t))) for t in TS[:-1]]
for j in range(9):
    a = j / 8; kp.append(Vector((0, KEEL(1.0) + 0.005 + (GUN(1.0) + 0.06 - KEEL(1.0)) * a, 2.0 + 0.035 * math.sin(a * math.pi * 0.5))))
nk = NS
sweepp('Keel', kp, 0.06, 0.065, R_TRIM, side=lambda i, p: Vector((0, 1, 0)) if i < nk - 1 else Vector((0, 0, -1)))
# ---------------- cuadernas (costillas) por dentro
RIBS = [0.05 + 0.065 * i for i in range(13)]
for t in RIBS:
    P = []; Nn = []; G = []
    for side in (-1, 1):
        rng = range(40, -1, -1) if side == -1 else range(0, 41)
        for j in rng:
            s = 0.02 + 0.95 * j / 40
            if side == 1 and j == 0: continue
            p, n, _ = sec(t, s, side)
            base = p - n * (TH + 0.004)
            P.append([base - n * 0.024 + Vector((0, 0, -0.019)), base - n * 0.024 + Vector((0, 0, 0.019))]); Nn.append([-n, -n]); G.append(s * side)
    ribbon_solid('Rib', P, Nn, 0.024, R_RIB, lambda i, j, G=G: (G[i] * 1.6, Z(t) + j * 0.04))
# ---------------- tablas del suelo, durmientes, bancadas y puntal
for x in (-0.3, -0.15, 0.0, 0.15, 0.3):
    zs = [Z(t) for t in [i / 200 for i in range(201)] if inner_x(t, -0.31, 0.045) > abs(x) + 0.065]
    if zs: boxp('Floor', (x - 0.062, -0.333, max(min(zs), -1.86)), (x + 0.062, -0.31, min(max(zs), 1.32)), R_WOOD, 0.004)
for side in (1, -1):
    pts = [Vector((side * inner_x(t, 0.035, 0.018 + TH), 0.035, Z(t))) for t in [0.03 + 0.85 * i / 40 for i in range(41)]]
    sweepp('Riser', pts, 0.028, 0.05, R_RIB)
for zc in (-1.35, 0.05, 1.15):
    t = (zc + 2) / 4; hw = inner_x(t, 0.075, 0.02 + TH)
    boxp('Thwart', (-hw, 0.055, zc - 0.13), (hw, 0.1, zc + 0.13), R_WOOD, 0.008, axis=0)
    for side in (1, -1):   # escuadras (curvas) bajo los extremos
        boxp('Knee', (side * (hw - 0.06) - 0.025, -0.06, zc - 0.03), (side * (hw - 0.06) + 0.025, 0.055, zc + 0.03), R_RIB, 0.005)
cylp('Pillar', (0, -0.12, 0.05), 0.022, 0.38, 'Y', R_RIB, 12)
# ---------------- regala: tapa, regala interior y verduguillo
for side in (1, -1):
    cap = []; inw = []; rub = []
    for t in [i / 48 for i in range(49)]:
        p, n, _ = sec(t, 1.0, side); cap.append(p + Vector((0, 0.014, 0)) + n * 0.004)
        inw.append(p - n * 0.03 - Vector((0, 0.03, 0)))
        q, m, _ = sec(t, 0.9, side); rub.append(q + m * (LAP + 0.012))
    sweepp('Cap', cap, 0.075, 0.028, R_TRIM); sweepp('Inwale', inw, 0.028, 0.05, R_TRIM); sweepp('Rubrail', rub, 0.032, 0.03, R_TRIM)
boxp('Breasthook', (-0.09, GUN(0.97) - 0.03, 1.8), (0.09, GUN(0.97) + 0.0, 1.93), R_TRIM, 0.01)
boxp('QuarterKnee', (-0.62, 0.27, -1.98), (0.62, 0.32, -1.9), R_TRIM, 0.008, axis=0)
# ---------------- toletes (horquillas de hierro) junto a la bancada de remar
for side in (1, -1):
    t = (0.42 + 2) / 4; p, n, _ = sec(t, 1.0, side); top = p.y + 0.03
    boxp('LockPad', (p.x - 0.035, top - 0.012, p.z - 0.05), (p.x + 0.035, top + 0.006, p.z + 0.05), R_IRON, 0.004)
    cylp('LockStem', (p.x, top - 0.03, p.z), 0.009, 0.06, 'Y', R_IRON, 10)
    arc = [Vector((p.x, top + 0.012 + 0.04 * math.sin(a), p.z + 0.035 * math.cos(a))) for a in [math.pi * (1 + i / 12) for i in range(13)]]
    arc = [Vector((v.x, 2 * (top + 0.012) - v.y + 0.0, v.z)) for v in arc]
    sweepp('Horn', arc, 0.012, 0.012, R_IRON, side=lambda i, q: Vector((1, 0, 0)))
# ---------------- remos (descansan sobre las bancadas)
for side, yaw in ((1, 0.035), (-1, -0.025)):
    a = Vector((side * 0.2, 0.128, -1.62)); b = Vector((side * 0.2 + yaw * 2.6, 0.128, 1.0)); d = (b - a)
    def at(u): return a + d * u
    rid = rnd.random()
    def oarpart(o):
        set_attr(o, 'Mask', lambda co, n, i: (R_OAR, 0.0, rid, 1.0)); set_attr(o, 'Grain', lambda co, n, i: (co.z, co.x * 0.5 + co.y, rid, 1.0)); parts.append(o)
    rot = Matrix.Rotation(math.atan2(d.x, d.z), 4, 'Y')
    for u0, u1, r0, r1 in ((0.0, 0.05, 0.017, 0.017), (0.05, 0.3, 0.026, 0.024), (0.3, 0.78, 0.022, 0.02)):
        c = at((u0 + u1) * 0.5); o = cyl('Oar', (0, 0, 0), r0, d.length * (u1 - u0), 'Z', 14, r1)
        o.data.transform(rot); o.data.transform(Matrix.Translation(c)); oarpart(o)
    bl = box('Blade', (-0.065, -0.007, 0), (0.065, 0.007, d.length * 0.25), 0.006, 2, 0)
    for v in bl.data.vertices:
        f = v.co.z / (d.length * 0.25); v.co.x *= 0.55 + 0.45 * min(1.0, f * 3.0); v.co.y *= 1.6 - f * 0.8
    bl.data.transform(rot); bl.data.transform(Matrix.Translation(at(0.75))); oarpart(bl)
# ---------------- poste del farol y farol de tormenta (luz del juego en (0, 1.0, 1.55))
boxp('Post', (-0.024, KEEL(0.8875) + 0.06, 1.526), (0.024, 0.885, 1.574), R_POST, 0.006, axis=1)
hw = inner_x(0.8875, 0.39, 0.02 + TH)
boxp('PostBrace', (-hw, 0.37, 1.535), (hw, 0.4, 1.565), R_POST, 0.005, axis=0)
cylp('LampBase', (0, 0.905, 1.55), 0.052, 0.045, 'Y', R_IRON, 20)
g = cylp('LampGlobe', (0, 0.995, 1.55), 0.036, 0.13, 'Y', R_GLASS, 20)
for v in g.data.vertices:
    f = (v.co.y - 0.995) / 0.065; v.co.x *= 1.25 - 0.45 * f * f; v.co.z = 1.55 + (v.co.z - 1.55) * (1.25 - 0.45 * f * f)
for k in range(4):
    a = k * math.pi / 2 + math.pi / 4
    cylp('LampGuard', (0.05 * math.cos(a), 0.995, 1.55 + 0.05 * math.sin(a)), 0.0035, 0.15, 'Y', R_IRON, 6)
cylp('LampCap', (0, 1.095, 1.55), 0.055, 0.05, 'Y', R_IRON, 20, 0.022)
bail = [Vector((0.052 * math.cos(a), 1.105 + 0.05 * math.sin(a), 1.55)) for a in [math.pi * i / 14 for i in range(15)]]
sweepp('LampBail', bail, 0.006, 0.006, R_IRON, side=lambda i, q: Vector((0, 0, 1)))
# ---------------- motor fueraborda viejo (antes era un bloque)
boxp('Clamp', (-0.065, 0.2, -2.095), (0.065, 0.37, -1.955), R_ALU, 0.012)
for x in (-0.04, 0.04): cylp('ClampScrew', (x, 0.24, -1.93), 0.008, 0.06, 'Z', R_IRON, 8); cylp('ClampHandle', (x, 0.24, -1.9), 0.004, 0.06, 'X', R_IRON, 6)
cylp('Leg', (0, -0.02, -2.17), 0.034, 0.62, 'Y', R_ALU, 16, 0.03)
cw = box('Cowl', (-0.11, 0.36, -2.31), (0.11, 0.62, -2.09), 0.06, 5, 2)   # (v7.3.0b) más pequeño
for v in cw.data.vertices:
    f = (v.co.y - 0.36) / 0.26; v.co.x *= 1.0 - 0.18 * f; v.co.z = -2.2 + (v.co.z + 2.2) * (1.0 - 0.1 * f)
set_attr(cw, 'Mask', lambda co, n, i: (R_COWL, 0.0, 0.3, 1.0)); set_attr(cw, 'Grain', lambda co, n, i: (co.z, co.y, 0.3, 1.0)); parts.append(cw)
cylp('Flywheel', (0, 0.635, -2.2), 0.07, 0.03, 'Y', R_ALU, 24)
boxp('StartGrip', (-0.035, 0.652, -2.14), (0.035, 0.668, -2.125), R_POST, 0.005, axis=0)
boxp('CavPlate', (-0.09, -0.335, -2.31), (0.09, -0.322, -2.06), R_ALU, 0.004)
gc = cylp('Gearcase', (0, -0.5, -2.2), 0.045, 0.23, 'Z', R_ALU, 18, 0.03)
for v in gc.data.vertices:
    f = (v.co.z + 2.2) / 0.115; v.co.x *= 1 - 0.25 * max(0, f) ** 2; v.co.y = -0.5 + (v.co.y + 0.5) * (1 - 0.25 * max(0, f) ** 2)
sk = boxp('Skeg', (-0.006, -0.64, -2.27), (0.006, -0.52, -2.15), R_ALU, 0.003)
for v in sk.data.vertices:
    if v.co.y < -0.6: v.co.z = -2.2 + (v.co.z + 2.2) * 0.5
cylp('Tiller', (0, 0.48, -1.82), 0.016, 0.42, 'Z', R_ALU, 12)
cylp('TillerGrip', (0, 0.48, -1.56), 0.022, 0.14, 'Z', R_POST, 14)
# ---------------- hélice (objeto aparte: gira en el juego con el Transform «Prop» en (0, -0.5, -2.32))
pp = []
o = cyl('Hub', (0, 0, 0), 0.026, 0.07, 'Z', 14, 0.018); pp.append(o)
for k in range(3):
    bl = box('PBlade', (-0.032, 0.02, -0.004), (0.032, 0.1, 0.004), 0.004, 2, 0)
    for v in bl.data.vertices:
        f = (v.co.y - 0.02) / 0.08; v.co.x *= 1.0 - 0.3 * (f - 0.5) ** 2 * 2
    bl.data.transform(Matrix.Rotation(math.radians(28), 4, 'Y')); bl.data.transform(Matrix.Rotation(k * 2 * math.pi / 3, 4, 'Z')); pp.append(bl)
for o in pp:
    set_attr(o, 'Mask', lambda co, n, i: (R_ALU, 0.0, 0.8, 1.0)); set_attr(o, 'Grain', lambda co, n, i: (co.z, co.x, 0.8, 1.0))
prop = join(pp, 'Propeller')

hull = join(parts, 'BoatHull')
for ob in (hull, prop):
    ob.data.transform(TOB)
prop.data.transform(Matrix.Translation(Vector((0, 0, 0))))
prop.location = TOB @ Vector((0, -0.5, -2.32))
for ob in (hull, prop):
    activate(ob); bpy.ops.mesh.customdata_custom_splitnormals_clear() if ob.data.has_custom_normals else None
    smooth(ob, 35)
L('faces hull', len(hull.data.polygons), 'prop', len(prop.data.polygons))

# ---------------- material procedural
mat = bpy.data.materials.new('BoatWood'); t = NT(mat)
for ob in (hull, prop): ob.data.materials.append(mat)
reg, ext, prnd = t.attr('Mask'); along, across, plank = t.attr('Grain')
def rw(k): return t.mr(t.m('ABSOLUTE', t.m('SUBTRACT', reg, k)), 0.045, 0.0)
H = t.PZ                                         # altura (y de Unity)
gvec = t.vec(t.m('MULTIPLY', along, 2.2), t.m('MULTIPLY', across, 48.0), t.m('MULTIPLY', plank, 17.0))
grain = t.noise(1.0, 6, 0.62, 0.5, gvec)
rings = t.wave(1.2, 6.0, t.vec(t.m('MULTIPLY', across, 20.0), t.m('MULTIPLY', along, 0.6), t.m('MULTIPLY', plank, 9.0)), 3, 'RINGS')
wood_l = t.mr(t.m('ADD', t.m('MULTIPLY', grain, 0.8), t.m('MULTIPLY', rings, 0.2)), 0.3, 0.75)
bare = t.mix(wood_l, (0.12, 0.105, 0.085), (0.30, 0.27, 0.22))          # madera gris envejecida
bare = t.mix(t.m('MULTIPLY', t.noise(3.0, 3), 0.5), bare, (0.24, 0.17, 0.10))
oak = t.mix(wood_l, (0.22, 0.13, 0.06), (0.42, 0.27, 0.13))             # remos barnizados
# pintura del casco: verde azulado desvaído, calcárea, con chorretones verticales
fade = t.noise(2.0, 4, 0.6)
streak = t.noise(1.0, 3, 0.5, 0.0, t.vec(t.m('MULTIPLY', t.PX, 26.0), t.m('MULTIPLY', t.PY, 26.0), t.m('MULTIPLY', H, 1.5)))
paint = t.mix(fade, (0.10, 0.22, 0.235), (0.24, 0.36, 0.36))
paint = t.mix(t.mr(streak, 0.45, 0.75), paint, (0.08, 0.15, 0.15))
trim = t.mix(fade, (0.25, 0.07, 0.045), (0.40, 0.14, 0.09))
cowl = t.mix(fade, (0.028, 0.028, 0.03), (0.075, 0.068, 0.06))   # (v7.3.0b) negro/gris oscuro gastado
# desconchones: más en los cantos de las tablas, en la tapa de la regala y cerca del agua
lapedge = t.mr(across, 0.02, 0.0)
chipn = t.m('ADD', t.noise(11, 5, 0.66, 0.2), t.m('ADD', t.m('MULTIPLY', lapedge, 0.12), t.m('MULTIPLY', t.mr(t.m('ABSOLUTE', t.m('SUBTRACT', H, WL + 0.06)), 0.1, 0.0), 0.08)))
chip = t.mr(chipn, 0.69, 0.71)
chiprim = t.m('MULTIPLY', t.mr(t.m('ABSOLUTE', t.m('SUBTRACT', chipn, 0.69)), 0.012, 0.0), 0.6)
capwear = t.m('MULTIPLY', rw(R_TRIM), t.mr(H, 0.28, 0.34))
chipT = t.mr(t.m('ADD', chipn, t.m('MULTIPLY', capwear, 0.16)), 0.66, 0.68)
# remaches de cobre en los solapes y en las cuadernas
pitch = 0.075
fa = t.m('SUBTRACT', t.m('FRACT', t.m('DIVIDE', along, pitch)), 0.5)
dl = t.vm('LENGTH', t.vec(t.m('MULTIPLY', fa, pitch), t.m('SUBTRACT', across, 0.011), 0.0))
riv = t.mr(dl, 0.0065, 0.0045)
ribd = None
for zr in [-2.0 + 4.0 * (0.05 + 0.065 * i) for i in range(13)]:
    d_ = t.m('ABSOLUTE', t.m('SUBTRACT', along, zr)); ribd = d_ if ribd is None else t.m('MINIMUM', ribd, d_)
dl2 = t.vm('LENGTH', t.vec(ribd, t.m('SUBTRACT', t.m('FRACT', t.m('DIVIDE', across, 0.06)), 0.5), 0.0))
riv2 = t.mr(t.vm('LENGTH', t.vec(ribd, t.m('MULTIPLY', t.m('SUBTRACT', t.m('FRACT', t.m('DIVIDE', across, 0.06)), 0.5), 0.06), 0.0)), 0.0065, 0.0045)
rivet = t.m('MULTIPLY', t.m('MAXIMUM', riv, riv2), rw(R_HULL))
rhalo = t.m('MULTIPLY', t.m('MULTIPLY', t.mr(dl, 0.016, 0.006), rw(R_HULL)), 0.55)
# color por zonas
hullc = t.mix(ext, bare, paint)
hullc = t.mix(t.m('MULTIPLY', chip, ext), hullc, bare)
hullc = t.mix(t.m('MULTIPLY', chiprim, ext), hullc, (0.34, 0.42, 0.40))
oldp = t.m('MULTIPLY', t.mr(t.noise(5, 4), 0.62, 0.66), t.m('SUBTRACT', 1.0, ext))   # restos de pintura vieja por dentro
hullc = t.mix(t.m('MULTIPLY', oldp, 0.8), hullc, (0.13, 0.12, 0.10))   # v7.3.0b: sin restos azulados por dentro
c = hullc
c = t.mix(rw(R_RIB), c, bare)
c = t.mix(rw(R_WOOD), c, t.mix(t.mr(t.noise(4, 3), 0.4, 0.7), t.mix(0.55, bare, (0.10, 0.075, 0.05)), (0.16, 0.115, 0.075)))   # tablas del fondo: madera oscura seca
c = t.mix(rw(R_TRIM), c, t.mix(chipT, trim, bare))
blade = t.mr(t.m('MULTIPLY', t.PY, -1.0), 0.5, 0.55)
c = t.mix(rw(R_OAR), c, t.mix(t.m('MULTIPLY', blade, t.m('SUBTRACT', 1.0, chip)), oak, (0.14, 0.27, 0.27)))
rust = t.mix(t.noise(14, 4), (0.07, 0.05, 0.04), (0.33, 0.14, 0.05))
c = t.mix(rw(R_IRON), c, rust)
alu = t.mix(t.noise(9, 4), (0.075, 0.075, 0.072), (0.15, 0.15, 0.145))
alu = t.mix(t.m('MULTIPLY', t.mr(t.voro(140), 0.12, 0.05), 0.6), alu, (0.26, 0.25, 0.23))
c = t.mix(rw(R_COWL), c, t.mix(chip, cowl, alu))
c = t.mix(rw(R_ALU), c, alu)
c = t.mix(rw(R_GLASS), c, (0.75, 0.62, 0.40))
c = t.mix(rw(R_POST), c, t.mix(chip, (0.035, 0.035, 0.035), bare))
c = t.mix(rivet, c, (0.20, 0.17, 0.10))
c = t.mix(rhalo, c, (0.22, 0.12, 0.06))
# agua: algas y suciedad en la línea de flotación (por fuera), sentina húmeda por dentro
zw = t.m('ADD', H, t.m('MULTIPLY', t.m('SUBTRACT', t.noise(5.0, 4), 0.5), 0.07))
outside = t.m('MAXIMUM', ext, t.m('MAXIMUM', rw(R_ALU), t.m('MAXIMUM', rw(R_TRIM), rw(R_COWL))))
slime = t.m('MULTIPLY', t.mr(zw, WL + 0.05, WL - 0.03, 0, 1, True), outside)
deep = t.m('MULTIPLY', t.mr(zw, WL - 0.08, WL - 0.18, 0, 1, True), outside)
c = t.mix(t.m('MULTIPLY', slime, t.mr(t.noise(12, 4), 0.3, 0.6, 0.7, 1.0)), c, (0.09, 0.16, 0.045))
c = t.mix(t.m('MULTIPLY', deep, 0.85), c, (0.07, 0.08, 0.045))
barn = t.m('MULTIPLY', deep, t.mr(t.voro(60), 0.2, 0.1))
c = t.mix(t.m('MULTIPLY', barn, 0.7), c, (0.45, 0.43, 0.37))
splash = t.m('MULTIPLY', t.mr(zw, WL + 0.2, WL + 0.02, 0, 1, True), outside)
c = t.mulc(c, t.m('SUBTRACT', 1.0, t.m('MULTIPLY', splash, 0.3)))
inside = t.m('SUBTRACT', 1.0, outside)
bilge = t.m('MULTIPLY', t.mr(H, -0.22, -0.32, 0, 1, True), inside)
c = t.mulc(c, t.m('SUBTRACT', 1.0, t.m('MULTIPLY', bilge, 0.45)))
c = t.mix(t.m('MULTIPLY', bilge, t.mr(t.noise(8, 4), 0.45, 0.7, 0, 0.6)), c, (0.055, 0.042, 0.03))   # sentina: sombra seca, no verdosa
c = t.mulc(c, t.m('SUBTRACT', 1.0, t.m('MULTIPLY', t.m('MULTIPLY', inside, t.m('SUBTRACT', 1.0, t.m('MAXIMUM', rw(R_GLASS), t.m('MAXIMUM', rw(R_OAR), rw(R_ALU))))), 0.25)))
t.Lk(c, t.bsdf.inputs['Base Color'])
# rugosidad / metal / emisión
wetness = t.m('MAXIMUM', t.m('MAXIMUM', slime, deep), t.m('MULTIPLY', splash, 0.5))   # el interior queda seco
rough = t.m('SUBTRACT', 0.88, t.m('MULTIPLY', wetness, 0.45))
rough = t.m('SUBTRACT', rough, t.m('MULTIPLY', t.m('MULTIPLY', ext, t.m('SUBTRACT', 1.0, chip)), 0.12))
rough = t.m('SUBTRACT', rough, t.m('MULTIPLY', rw(R_OAR), 0.25))
rough = t.m('SUBTRACT', rough, t.m('MULTIPLY', t.m('MAXIMUM', rw(R_ALU), rw(R_GLASS)), 0.35))
t.Lk(rough, t.bsdf.inputs['Roughness'])
metal = t.m('ADD', t.m('ADD', t.m('MULTIPLY', rw(R_ALU), 0.75), t.m('MULTIPLY', rw(R_IRON), 0.35)), t.m('MULTIPLY', t.m('MULTIPLY', rw(R_COWL), chip), 0.6))
emitN = t.N('ShaderNodeEmission'); t.Lk(t.mulc((1.0, 0.78, 0.5), rw(R_GLASS)), emitN.inputs[0])
# relieve: veta, desconchones (escalón de la pintura), remaches abombados, algas
paintH = t.m('MULTIPLY', t.m('SUBTRACT', 1.0, chip), t.m('MAXIMUM', ext, t.m('MAXIMUM', rw(R_TRIM), rw(R_COWL))))
h = t.m('ADD', t.m('MULTIPLY', grain, 0.35), t.m('MULTIPLY', paintH, 0.5))
h = t.m('ADD', h, t.m('MULTIPLY', rivet, 1.4))
h = t.m('ADD', h, t.m('MULTIPLY', t.m('MAXIMUM', slime, deep), t.m('MULTIPLY', t.noise(40, 3), 0.6)))
h = t.m('ADD', h, t.m('MULTIPLY', barn, 0.8))
t.bump(h, 0.6, 0.004)

gpu()
uv([hull, prop], 0.002)
if not SKIP: bake([hull, prop], t, TEX, OUT, 'BoatWood', emit=emitN.outputs[0], metal=metal)
try:
    preview(OUT, [('ext', (3.2, 3.6, 1.4), (0, 0.2, 0.0)), ('stern', (1.6, 5.2, 1.9), (0, 0, 0.2)), ('inside', (0.0, 2.6, 1.6), (0, -0.4, -0.1)), ('motor', (1.0, 3.2, 0.5), (0, 2.2, 0.2))],
            samples=int(os.environ.get('PROP_PREV', '48')), world=(0.42, 0.46, 0.5), lights=(('K', (3, 3, 4), 600), ('R', (-3, -3, 3), 300)))
    L('preview ok')
except Exception as e: L('preview fail', e)
export(OUT, 'Boat.fbx', [hull, prop])
pc.LOG.close()
