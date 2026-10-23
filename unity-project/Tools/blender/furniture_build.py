# v7.3.0: muebles de las casas (diseño propio): mesa de madera con patas y faldón (vetas, anillos de agua), silla con respaldo
# de listones y patas, cama de hierro con colchón de cutí, sábanas arrugadas húmedas y sucias, manta y almohada.
# Blender: X largo, +Y = frente (Unity -Z), +Z arriba. Origen de cada pieza en el suelo, centrada. Un material (atlas) horneado en la GPU.
import bpy, bmesh, sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from props_common import *
import props_common as pc
from mathutils import Vector, Matrix, noise as mnoise
argv = sys.argv[sys.argv.index('--') + 1:]; OUT = argv[0]
TEX = int(os.environ.get('PROP_TEX', '2048')); SKIP = os.environ.get('PROP_SKIP_BAKE') == '1'
init(OUT, 'furniture_build.log'); rnd = random.Random(7317)
R_WOOD, R_SHEET, R_MATT, R_IRON, R_PILLOW, R_BLANKET = 0.0, 0.2, 0.4, 0.6, 0.8, 1.0
def tag(o, region, along=0):
    rr = rnd.random()
    set_attr(o, 'Mask', lambda co, n, i: (region, along, rr, 1.0)); return o
def wbox(name, lo, hi, bevel=0.006, along=0, warp=0.0):
    o = box(name, lo, hi, bevel, 2, 0)
    if warp:
        for v in o.data.vertices: v.co.z += warp * mnoise.noise(v.co * 3.0 + Vector((rnd.random() * 9, 0, 0)))
    return tag(o, R_WOOD, along)
def leg(name, x, y, z0, z1, w0, w1, rake=(0, 0)):
    o = box(name, (x - w0 / 2, y - w0 / 2, z0), (x + w0 / 2, y + w0 / 2, z1), 0.004, 2, 0)
    for v in o.data.vertices:
        f = (v.co.z - z0) / (z1 - z0); s = (w0 + (w1 - w0) * (1 - f)) / w0 if False else 1.0
        k = (w1 + (w0 - w1) * f) / w0          # más fina abajo
        v.co.x = x + (v.co.x - x) * k + rake[0] * (1 - f); v.co.y = y + (v.co.y - y) * k + rake[1] * (1 - f)
    return tag(o, R_WOOD, 2)

# ---------------- mesa (1,40 × 1,30 × 0,76)
tp = []
top = wbox('Top', (-0.70, -0.65, 0.728), (0.70, 0.65, 0.762), 0.008, 0, warp=0.004)
# juntas entre tablas del tablero (3 tablas a lo largo de X)
tp.append(top)
for y in (-0.217, 0.217):
    g = box('Gap', (-0.69, y - 0.003, 0.759), (0.69, y + 0.003, 0.7625), 0.0, 1, 0); tag(g, R_WOOD, 0.5); tp.append(g)
for s in (-1, 1):
    tp.append(wbox('ApronX', (-0.6, s * 0.55 - 0.012, 0.64), (0.6, s * 0.55 + 0.012, 0.728), 0.004))
    tp.append(wbox('ApronY', (s * 0.6 - 0.012, -0.55, 0.64), (s * 0.6 + 0.012, 0.55, 0.728), 0.004, along=1))
    for t_ in (-1, 1): tp.append(leg('Leg', s * 0.6, t_ * 0.55, 0.0, 0.728, 0.062, 0.045))
for s in (-1, 1): tp.append(wbox('Stretch', (s * 0.6 - 0.012, -0.55, 0.14), (s * 0.6 + 0.012, 0.55, 0.18), 0.004, along=1))
table = join(tp, 'Table'); smooth(table, 40)
# ---------------- silla (asiento a 0,45; respaldo a 0,95)
cp = []
cp.append(wbox('Seat', (-0.215, -0.2, 0.43), (0.215, 0.22, 0.458), 0.008, warp=0.002))
for s in (-1, 1):
    cp.append(leg('FrontLeg', s * 0.19, 0.18, 0.0, 0.43, 0.036, 0.03))
    bl = leg('BackLeg', s * 0.19, -0.18, 0.0, 0.95, 0.038, 0.032, rake=(0, 0.04))
    for v in bl.data.vertices:
        if v.co.z > 0.46: v.co.y -= 0.06 * (v.co.z - 0.46) / 0.49      # respaldo inclinado
    cp.append(bl)
    cp.append(wbox('SideRail', (s * 0.19 - 0.01, -0.17, 0.38), (s * 0.19 + 0.01, 0.17, 0.43), 0.003, along=1))
    cp.append(wbox('LowRail', (s * 0.19 - 0.008, -0.17, 0.13), (s * 0.19 + 0.008, 0.17, 0.15), 0.003, along=1))
cp.append(wbox('FrontRail', (-0.18, 0.17, 0.38), (0.18, 0.19, 0.43), 0.003))
for z, hgt in ((0.88, 0.07), (0.70, 0.045), (0.58, 0.045)):
    yb = -0.18 - 0.06 * (z - 0.46) / 0.49
    sl = wbox('BackSlat', (-0.18, yb - 0.01, z), (0.18, yb + 0.01, z + hgt), 0.004)
    for v in sl.data.vertices: v.co.y -= 0.025 * (1 - (v.co.x / 0.18) ** 2)    # curvado
    cp.append(sl)
chair = join(cp, 'Chair'); smooth(chair, 40)
# ---------------- cama de hierro (2,04 × 1,84; colchón a 0,46)
bp = []
LX, WY = 1.0, 0.9
def tube(name, a, b, r, seg=10):
    a = Vector(a); b = Vector(b); d = b - a; o = cyl(name, (0, 0, 0), r, d.length, seg=seg)
    o.data.transform(Matrix.Translation((a + b) / 2) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4())
    return tag(o, R_IRON)
for sx, top_ in ((1, 1.02), (-1, 0.74)):     # cabecero (+X) y pie
    for sy in (-1, 1):
        bp.append(tube('Post', (sx * LX, sy * WY, 0.0), (sx * LX, sy * WY, top_), 0.024))
        bm_ = bmesh.new(); bmesh.ops.create_uvsphere(bm_, u_segments=12, v_segments=8, radius=0.04)
        for v in bm_.verts: v.co += Vector((sx * LX, sy * WY, top_ + 0.02))
        bp.append(tag(obj_from_bm('Knob', bm_), R_IRON))
    for z in (top_ - 0.03, top_ - 0.32 if sx > 0 else top_ - 0.25): bp.append(tube('Rail', (sx * LX, -WY, z), (sx * LX, WY, z), 0.016))
    zb = 0.30; zt = top_ - 0.03
    for k in range(9):
        y = -WY + (k + 1) * 2 * WY / 10
        bp.append(tube('Bar', (sx * LX, y, zb), (sx * LX, y, zt), 0.009, 8))
    # arco del cabecero
    if sx > 0:
        pts = [Vector((LX, -WY + 2 * WY * i / 16, top_ - 0.03 + 0.14 * math.sin(math.pi * i / 16))) for i in range(17)]
        for i in range(16): bp.append(tube('Arch', pts[i], pts[i + 1], 0.014, 8))
for sy in (-1, 1): bp.append(tube('SideRail', (-LX, sy * WY, 0.27), (LX, sy * WY, 0.27), 0.02))
for k in range(5): bp.append(wbox('Slat', (-LX + 0.2 + k * 0.4 - 0.04, -WY, 0.27), (-LX + 0.2 + k * 0.4 + 0.04, WY, 0.29), 0.003, along=1))
m = box('Mattress', (-0.98, -0.88, 0.29), (0.98, 0.88, 0.455), 0.05, 4, 2)
for v in m.data.vertices:
    fx = max(0.0, 1 - (v.co.x / 0.98) ** 2); fy = max(0.0, 1 - (v.co.y / 0.88) ** 2)
    v.co.z -= 0.05 * fx * fy * (v.co.z > 0.38)                     # hundido
    v.co += v.normal * 0.004 * mnoise.noise(v.co * 6)
tag(m, R_MATT); bp.append(m)
# almohada
pl = box('Pillow', (0.62, -0.42, 0.43), (0.94, 0.4, 0.56), 0.05, 4, 2)
for v in pl.data.vertices:
    fx = max(0.0, 1 - ((v.co.x - 0.78) / 0.16) ** 2); fy = max(0.0, 1 - (v.co.y / 0.41) ** 2)
    v.co.z += (0.05 * fx * fy - 0.03) * (1 if v.co.z > 0.5 else -0.3)
    v.co.z -= 0.035 * math.exp(-((v.co.y + 0.05) ** 2) / 0.02) * fx * (v.co.z > 0.5)      # hueco de la cabeza
    v.co += v.normal * 0.006 * mnoise.noise(v.co * 9 + Vector((3, 0, 0)))
tag(pl, R_PILLOW); bp.append(pl)
# sábana: rejilla sobre el colchón que cae por los lados con arrugas; manta arrugada a los pies
def drape(name, x0, x1, ext, ztop, region, amp, hang, seed, fold_k):
    bm_ = bmesh.new(); nx, ny = 96, 84
    vs = []
    for j in range(ny + 1):
        row = []
        for i in range(nx + 1):
            x = x0 + (x1 - x0) * i / nx; y = -ext + 2 * ext * j / ny
            ox = max(0.0, x - 0.98, -0.98 - x); oy = max(0.0, abs(y) - 0.88)
            z = ztop - 0.02 * max(0.0, 1 - (x / 0.98) ** 2) * max(0.0, 1 - (y / 0.88) ** 2) * 2
            xx, yy = x, y
            if oy > 0: z -= oy * 1.0; yy = math.copysign(0.88 + 0.03 + oy * 0.1, y)       # cae por el lateral
            if ox > 0: z -= ox * 1.0; xx = math.copysign(0.98 + 0.03 + ox * 0.1, x)
            z = max(z, hang)
            q = Vector((x * fold_k, y * fold_k, seed))
            w = amp * (mnoise.noise(q) + 0.5 * mnoise.noise(q * 2.7) + 0.25 * mnoise.noise(q * 6.1))
            w += amp * 0.8 * math.sin(x * 9 + y * 4 + 2 * mnoise.noise(q * 0.5)) * (0.5 + 0.5 * mnoise.noise(q * 0.7))
            if oy > 0 or ox > 0: w *= 1.6
            row.append(bm_.verts.new((xx + (w * 0.4 if ox > 0 else 0), yy + (w * 0.4 if oy > 0 else 0), z + (w if ox == 0 and oy == 0 else 0))))
        vs.append(row)
    for j in range(ny):
        for i in range(nx): bm_.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
    o = obj_from_bm(name, bm_)
    s = o.modifiers.new('so', 'SOLIDIFY'); s.thickness = 0.008; s.offset = 1.0
    sm = o.modifiers.new('sm', 'SMOOTH'); sm.iterations = 1; apply_mods(o)
    return tag(o, region)
bp.append(drape('Sheet', -1.16, 0.66, 1.12, 0.468, R_SHEET, 0.034, 0.1, 1.3, 5.0))
bp.append(drape('Blanket', -1.18, -0.3, 1.14, 0.50, R_BLANKET, 0.04, 0.06, 5.7, 3.6))
for o in bp: smooth(o, 45)
bed = join(bp, 'Bed'); smooth(bed, 45)
objs = [table, chair, bed]
for o in objs: L('faces', o.name, len(o.data.polygons))

# ---------------- material
mat = bpy.data.materials.new('HouseFurn'); t = NT(mat)
for o in objs: o.data.materials.append(mat)
reg, along, prnd = t.attr('Mask')
P = t.geo.outputs['Position']
nrm = t.N('ShaderNodeSeparateXYZ'); t.Lk(t.geo.outputs['Normal'], nrm.inputs[0]); NZ = nrm.outputs[2]
isW = t.mr(reg, 0.1, 0.0); isSheet = t.m('MULTIPLY', t.mr(reg, 0.1, 0.2), t.mr(reg, 0.3, 0.2)); isMatt = t.m('MULTIPLY', t.mr(reg, 0.3, 0.4), t.mr(reg, 0.5, 0.4))
isIron = t.m('MULTIPLY', t.mr(reg, 0.5, 0.6), t.mr(reg, 0.7, 0.6)); isPil = t.m('MULTIPLY', t.mr(reg, 0.7, 0.8), t.mr(reg, 0.9, 0.8)); isBlk = t.mr(reg, 0.9, 1.0)
# madera: vetas a lo largo de X (o Y / Z según la pieza), gastada, oscurecida por la humedad
gx = t.wave(4.0, 2.5, t.vm('ADD', P, t.vec(t.m('MULTIPLY', prnd, 7.0), 0.0, 0.0)), 4, 'BANDS', 'Y')
gy = t.wave(4.0, 2.5, t.vm('ADD', P, t.vec(0.0, t.m('MULTIPLY', prnd, 7.0), 0.0)), 4, 'BANDS', 'X')
grain = t.mix(t.mr(along, 0.4, 0.6), gx, gy)
grainF = t.noise(60, 3, 0.5, 0.0, t.vec(t.m('MULTIPLY', t.PX, t.m('ADD', 0.08, t.m('MULTIPLY', along, 0.92))), t.m('MULTIPLY', t.PY, t.m('SUBTRACT', 1.0, t.m('MULTIPLY', along, 0.92))), t.PZ))
wood = t.mix(t.mr(grain, 0.2, 0.9), (0.11, 0.075, 0.045), (0.19, 0.13, 0.08))
wood = t.mulc(wood, t.mr(grainF, 0.3, 0.7, 0.8, 1.12))
wood = t.mulc(wood, t.mr(prnd, 0, 1, 0.85, 1.1))
wear = t.mr(t.noise(9, 4, 0.6), 0.55, 0.75)
wood = t.mix(t.m('MULTIPLY', wear, 0.35), wood, (0.30, 0.24, 0.17))
# anillos de agua y manchas en superficies horizontales de arriba
up = t.mr(NZ, 0.8, 0.95)
vr = t.voro(2.2, 'F1', 'Distance', P)
rings = t.m('MAXIMUM', t.mr(t.m('ABSOLUTE', t.m('SUBTRACT', vr, 0.11)), 0.006, 0.0), t.m('MULTIPLY', t.mr(t.m('ABSOLUTE', t.m('SUBTRACT', vr, 0.075)), 0.004, 0.0), 0.6))
rings = t.m('MULTIPLY', t.m('MULTIPLY', rings, up), t.mr(t.noise(4, 2), 0.4, 0.6))
wood = t.mix(t.m('MULTIPLY', rings, 0.7), wood, (0.06, 0.04, 0.025))
wet = t.mr(t.noise(1.5, 4, 0.6), 0.4, 0.7)
wood = t.mulc(wood, t.m('SUBTRACT', 1.0, t.m('MULTIPLY', wet, 0.35)))
mold = t.m('MULTIPLY', t.mr(t.voro(90), 0.14, 0.05), t.mr(t.noise(3, 3), 0.55, 0.7))
wood = t.mix(t.m('MULTIPLY', mold, 0.8), wood, (0.05, 0.06, 0.04))
# tela: sábana/almohada blanco roto sucio, manchas, humedad y moho; colchón de cutí a rayas; manta de lana gris verdosa
blot = t.noise(2.4, 5, 0.62, 0.3)
stain = t.mr(blot, 0.5, 0.64)
sring = t.mr(t.m('ABSOLUTE', t.m('SUBTRACT', blot, 0.56)), 0.006, 0.0)
cloth = t.mix(t.noise(3, 3), (0.36, 0.34, 0.28), (0.46, 0.43, 0.35))
cloth = t.mix(t.m('MULTIPLY', stain, 0.55), cloth, (0.36, 0.30, 0.20))
cloth = t.mix(t.m('MULTIPLY', sring, 0.6), cloth, (0.26, 0.21, 0.14))
damp = t.mr(t.noise(1.2, 4, 0.6, 0.0, t.vm('ADD', P, (5, 1, 0))), 0.45, 0.62)
cloth = t.mulc(cloth, t.m('SUBTRACT', 1.0, t.m('MULTIPLY', damp, 0.3)))
stripe = t.wave(18, 0.0, P, 0, 'BANDS', 'Y')
ticking = t.mix(t.mr(stripe, 0.45, 0.55), (0.33, 0.32, 0.27), (0.19, 0.21, 0.23))
ticking = t.mix(t.m('MULTIPLY', stain, 0.7), ticking, (0.30, 0.24, 0.15))
wool = t.mix(t.noise(25, 4, 0.7), (0.13, 0.14, 0.11), (0.22, 0.23, 0.18))
wool = t.mix(t.m('MULTIPLY', stain, 0.5), wool, (0.12, 0.10, 0.07))
mil = t.m('MULTIPLY', t.mr(t.voro(140), 0.15, 0.04), t.mr(t.noise(2.5, 3), 0.5, 0.66))
# hierro: pintura negra desconchada sobre óxido
chip = t.mr(t.noise(14, 5, 0.65), 0.52, 0.56)
rust = t.mix(t.noise(30, 4), (0.20, 0.08, 0.03), (0.38, 0.17, 0.06))
iron = t.mix(chip, (0.05, 0.05, 0.05), rust)
c = wood
c = t.mix(isSheet, c, cloth); c = t.mix(isPil, c, cloth); c = t.mix(isMatt, c, ticking); c = t.mix(isBlk, c, wool); c = t.mix(isIron, c, iron)
c = t.mix(t.m('MULTIPLY', mil, t.m('SUBTRACT', 1.0, t.m('ADD', isW, isIron))), c, (0.06, 0.07, 0.05))
t.Lk(c, t.bsdf.inputs['Base Color'])
rough = t.m('SUBTRACT', 0.82, t.m('MULTIPLY', wet, 0.25))
rough = t.mix(t.m('ADD', isSheet, t.m('ADD', isPil, t.m('ADD', isMatt, isBlk))), rough, t.m('SUBTRACT', 0.95, t.m('MULTIPLY', damp, 0.25)))
rough = t.mix(isIron, rough, t.mr(chip, 0, 1, 0.45, 0.85))
t.Lk(rough, t.bsdf.inputs['Roughness'])
metal = t.m('MULTIPLY', isIron, t.m('SUBTRACT', 1.0, chip))
hw = t.m('ADD', t.m('MULTIPLY', grain, 0.25), t.m('MULTIPLY', grainF, 0.5))
hw = t.m('ADD', hw, t.m('MULTIPLY', along, 0.0))
hc = t.m('ADD', t.m('MULTIPLY', t.noise(40, 4, 0.6), 0.35), t.m('MULTIPLY', t.wave(220, 0.4, P, 1, 'BANDS', 'X'), 0.12))
hc = t.m('ADD', hc, t.m('MULTIPLY', t.noise(1.0, 3, 0.5, 0.0, t.vec(t.m('MULTIPLY', t.PX, 14.0), t.m('MULTIPLY', t.PY, 5.0), t.m('MULTIPLY', t.PZ, 14.0))), 1.2))
hi = t.m('MULTIPLY', chip, 0.6)
h = t.mix(t.m('SUBTRACT', 1.0, isW), hw, hc); h = t.mix(isIron, h, hi)
t.bump(h, 0.5, 0.004)

gpu()
uv(objs, 0.002)
if not SKIP: bake(objs, t, TEX, OUT, 'HouseFurn', metal=metal)
try:
    chair.location = (1.6, 0, 0); bed.location = (-2.4, 0, 0)
    preview(OUT, [('all', (0.2, 5.2, 2.6), (-0.6, 0, 0.45)), ('table', (1.0, 1.6, 1.3), (0, 0, 0.7)), ('chair', (2.4, 1.2, 0.9), (1.6, 0, 0.5)), ('bed', (-1.4, 2.1, 1.5), (-2.4, 0, 0.4))],
            samples=int(os.environ.get('PROP_PREV', '48')), world=(0.35, 0.38, 0.4), lights=(('K', (1.5, 3, 3.5), 500), ('R', (-3, -2, 2.5), 250)))
    chair.location = (0, 0, 0); bed.location = (0, 0, 0)
    L('preview ok')
except Exception as e: L('preview fail', e)
export(OUT, 'Furniture.fbx', objs)
pc.LOG.close()
