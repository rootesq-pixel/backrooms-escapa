# v7.3.0: esculturas colosales del fondo (diseño propio, talla de piedra al estilo de las grandes cabezas monolíticas):
# cabeza de 8 m con frente y cejas marcadas, cuencas hondas, nariz larga, labios apretados y barbilla; mano gigante de ~8 m
# con nudillos y falanges; rostro tumbado boca arriba medio enterrado. Piedra tallada y erosionada, algas, percebes.
# Blender: Z arriba, la cara mira a -Y (Unity +Z). Origen de cada pieza al nivel de la arena (lo de debajo queda enterrado).
import bpy, bmesh, sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from props_common import *
import props_common as pc
from mathutils import Vector, Matrix, noise as mnoise
argv = sys.argv[sys.argv.index('--') + 1:]; OUT = argv[0]
TEX = int(os.environ.get('PROP_TEX', '2048')); SKIP = os.environ.get('PROP_SKIP_BAKE') == '1'
init(OUT, 'sculpt_build.log'); rnd = random.Random(7313)
def ss(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a))); return t * t * (3 - 2 * t)
def band(x, a, b, soft):   # 1 dentro de [a,b], suave en los bordes
    return ss(a - soft, a + soft, x) * (1 - ss(b - soft, b + soft, x))

def head_mesh(name, H, HW, HD, seed, parted=0.0):
    """cabeza tallada por secciones: superelipse (casi rectangular) + relieve frontal; z de -1.5 (enterrado) a H"""
    def widths(z):
        zn = z / H
        w = 0.80 + 0.12 * ss(0.10, 0.30, zn) + 0.06 * ss(0.35, 0.55, zn) - 0.10 * ss(0.78, 0.95, zn)
        d = 0.78 + 0.18 * ss(0.15, 0.45, zn) + 0.04 * ss(0.5, 0.7, zn)
        top = math.sqrt(max(0.0, 1 - ((zn - 0.86) / 0.14) ** 2)) if zn > 0.86 else 1.0
        return HW * w * top, HD * d * top
    def front(u, zn):
        """relieve frontal (m, hacia -Y) en función de u = x/semiancho y zn = z/H"""
        a = abs(u); d = 0.0
        # cejas: repisa que sobresale y corta en seco hacia las cuencas
        d += 0.55 * band(a, -1.0, 0.88, 0.08) * (ss(0.745, 0.70, zn) * ss(0.645, 0.685, zn))
        d -= 0.12 * band(a, -1.0, 0.9, 0.1) * band(zn, 0.74, 0.88, 0.04) * 0.0
        # cuencas de los ojos (hondas, en sombra bajo la repisa)
        eye = band(a, 0.16, 0.74, 0.07) * band(zn, 0.555, 0.655, 0.025)
        d -= 0.48 * eye
        d += 0.10 * band(a, 0.26, 0.62, 0.06) * band(zn, 0.575, 0.615, 0.015)        # párpado inferior cerrado
        # nariz: puente estrecho que crece hasta la punta; aletas anchas; corte debajo
        nz0, nz1 = 0.405, 0.69
        if nz0 - 0.03 < zn < nz1:
            f = (nz1 - zn) / (nz1 - nz0)                                                    # 0 arriba, 1 en la punta
            wN = 0.13 + 0.14 * f ** 1.5
            d += (0.25 + 0.95 * f ** 1.3) * band(a, -1, wN, 0.05) * ss(nz0 - 0.03, nz0 + 0.015, zn)
        d += 0.30 * band(a, 0.10, 0.30, 0.06) * band(zn, 0.405, 0.46, 0.015)               # aletas
        d -= 0.25 * band(a, 0.15, 0.24, 0.025) * band(zn, 0.405, 0.42, 0.01)               # orificios
        # labios apretados que sobresalen (labio de arriba largo), comisura y barbilla
        d += 0.62 * band(a, -1.0, 0.46, 0.07) * band(zn, 0.33, 0.395, 0.015)
        d -= 0.22 * band(a, -1.0, 0.5, 0.05) * band(zn, 0.318 - parted, 0.33, 0.006)        # línea de la boca
        d += 0.50 * band(a, -1.0, 0.42, 0.07) * band(zn, 0.27 - parted, 0.318 - parted, 0.014)
        d += 0.70 * band(a, -1.0, 0.5, 0.1) * band(zn, 0.12, 0.25, 0.03)                    # barbilla
        d -= 0.18 * band(a, -1.0, 0.55, 0.08) * band(zn, 0.25, 0.27, 0.012)                 # surco bajo el labio
        return d
    NZ, NT = 300, 220
    bm = bmesh.new(); rows = []
    for i in range(NZ + 1):
        z = -1.5 + (H + 1.5) * i / NZ; zn = z / H
        hw, hd = widths(max(z, 0.0))
        row = []
        for j in range(NT):
            th = 2 * math.pi * j / NT
            c, s = math.cos(th), math.sin(th)
            n = 4.0
            k = (abs(c) ** n + abs(s) ** n) ** (-1.0 / n)
            x, y = hw * c * k, hd * s * k
            if y < 0 and hw > 0.01:
                u = x / hw; wf = ss(0.35, 0.85, -y / hd)
                y -= front(u, zn) * wf
            # orejas largas a los lados
            if abs(y) < 0.45 * hd and hw > 0.01:
                x += math.copysign(0.32, x) * band(zn, 0.38, 0.70, 0.03) * band(y / hd, -0.42, 0.2, 0.1) * ss(0.6, 0.95, abs(c))
            p = Vector((x, y, z))
            q = p * 0.45 + Vector((seed, 0, 0)); e = 0.10 * mnoise.fractal(q, 0.6, 2.0, 4) + 0.03 * mnoise.fractal(p * 2.4 + Vector((0, seed, 0)), 0.6, 2.0, 3)
            nrm = Vector((x, y, 0)).normalized() if (x or y) else Vector((0, 0, 1))
            p += nrm * e
            row.append(bm.verts.new(p))
        rows.append(row)
    for i in range(NZ):
        for j in range(NT):
            bm.faces.new((rows[i][j], rows[i][(j + 1) % NT], rows[i + 1][(j + 1) % NT], rows[i + 1][j]))
    top = bm.verts.new(Vector((0, 0, H))); bot = bm.verts.new(Vector((0, 0, -1.5)))
    for j in range(NT):
        bm.faces.new((rows[NZ][(j + 1) % NT], rows[NZ][j], top)); bm.faces.new((rows[0][j], rows[0][(j + 1) % NT], bot))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = obj_from_bm(name, bm)
    sm = o.modifiers.new('sm', 'SMOOTH'); sm.iterations = 2; sm.factor = 0.5; apply_mods(o)
    return o

# ---------------- 1) cabeza colosal (8 m sobre la arena)
head = head_mesh('Head', 8.0, 2.2, 2.0, 1.7)
# grieta diagonal
for v in head.data.vertices:
    d = abs(v.co.x * 0.55 + v.co.z * 0.2 - 1.9 + 0.25 * mnoise.noise(Vector((v.co.z * 0.8, 3, 0))))
    if d < 0.07 and v.co.z > 4.5: v.co -= Vector((v.co.x, v.co.y, 0)).normalized() * 0.12 * (1 - d / 0.07)
# ---------------- 2) mano gigante: cápsulas unidas con remallado de vóxeles
caps = []
def capsule(a, b, r, r2=None):
    r2 = r if r2 is None else r2
    a = Vector(a); b = Vector(b); d = b - a
    o = cyl('C', (0, 0, 0), r, d.length, seg=20, r2=r2)
    o.data.transform(Matrix.Translation((a + b) / 2) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4()); caps.append(o)
    for c, rr in ((a, r), (b, r2)):
        bm_ = bmesh.new(); bmesh.ops.create_uvsphere(bm_, u_segments=20, v_segments=12, radius=rr)
        for v in bm_.verts: v.co += c
        caps.append(obj_from_bm('S', bm_))
capsule((0, 0.1, -1.5), (0, 0.05, 2.2), 1.05, 0.95)                    # antebrazo (sale de la arena)
capsule((0, 0.0, 2.2), (0, 0.0, 2.6), 0.95, 1.0)                        # muñeca
pb = box('Palm', (-1.2, -0.45, 2.5), (1.2, 0.45, 5.0), 0.4, 4, 1); caps.append(pb)
fingers = [(-0.9, (1.1, 0.7, 0.5), 0.285, -0.17), (-0.3, (1.25, 0.8, 0.55), 0.305, -0.05), (0.3, (1.2, 0.76, 0.52), 0.295, 0.06), (0.88, (0.9, 0.58, 0.42), 0.26, 0.2)]
for fx, segs, r0, spread in fingers:
    capsule((fx * 0.95, 0, 3.2), (fx, -0.02, 5.05), r0 * 1.12, r0 * 1.12)             # metacarpo
    p = Vector((fx, -0.02, 5.05)); d = Vector((spread, 0, 1)).normalized(); r = r0
    for k, L_ in enumerate(segs):
        ang = math.radians((12, 18, 14)[k]); d = (Matrix.Rotation(ang, 3, 'X') @ d).normalized()   # se curva hacia la palma (-Y)
        q = p + d * L_
        capsule(p, q, r * 1.08, r * 0.95)                                               # falange
        bm_ = bmesh.new(); bmesh.ops.create_uvsphere(bm_, u_segments=16, v_segments=10, radius=r * 1.14)
        for v in bm_.verts: v.co += p
        caps.append(obj_from_bm('K', bm_))                                              # nudillo
        p = q; r *= 0.9
# pulgar
tb = Vector((-1.15, -0.2, 3.0)); td = Vector((-0.7, -0.45, 0.6)).normalized(); r = 0.42
capsule((-0.6, -0.1, 2.6), tb, 0.55, 0.45)
for k, L_ in enumerate((0.95, 0.7)):
    td = (Matrix.Rotation(math.radians(16), 3, 'Y') @ td).normalized(); q = tb + td * L_
    capsule(tb, q, r, r * 0.92); tb = q; r *= 0.9
hand = join(caps, 'Hand')
m = hand.modifiers.new('rm', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = 0.06
s = hand.modifiers.new('sm', 'SMOOTH'); s.iterations = 6; s.factor = 0.6
apply_mods(hand)
for v in hand.data.vertices:
    v.co += v.normal * (0.06 * mnoise.fractal(v.co * 0.6 + Vector((5, 0, 0)), 0.6, 2.0, 4) + 0.02 * mnoise.fractal(v.co * 3 + Vector((0, 5, 0)), 0.6, 2.0, 3))
    # pliegues de los nudillos (surcos horizontales en el dorso)
for o in (hand,):
    bm_ = bmesh.new(); bm_.from_mesh(o.data)
    bmesh.ops.bisect_plane(bm_, geom=bm_.verts[:] + bm_.edges[:] + bm_.faces[:], plane_co=(0, 0, -1.0), plane_no=(0, 0, -1), clear_outer=True)
    bm_.to_mesh(o.data); bm_.free()
# ---------------- 3) otra cabeza colosal (6,5 m) ladeada y enterrada hasta los labios
face = head_mesh('FaceUp', 6.5, 1.8, 1.6, 4.2, parted=0.012)
face.data.transform(Matrix.Rotation(math.radians(-9), 4, 'X'))    # mira un poco hacia arriba
face.data.transform(Matrix.Rotation(math.radians(16), 4, 'Y'))    # ladeada, hundida
face.data.transform(Matrix.Translation((0, 0, -1.7)))            # enterrada hasta el labio
bm_ = bmesh.new(); bm_.from_mesh(face.data)
bmesh.ops.bisect_plane(bm_, geom=bm_.verts[:] + bm_.edges[:] + bm_.faces[:], plane_co=(0, 0, -0.1), plane_no=(0, 0, -1), clear_outer=True)
bm_.to_mesh(face.data); bm_.free()
objs = [head, hand, face]
for o in objs: smooth(o)
for o in objs: L('faces', o.name, len(o.data.polygons), 'dims', tuple(round(x, 2) for x in o.dimensions))

# ---------------- material: piedra tallada (toba gris), marcas de cincel, grietas finas, algas en lo que mira arriba, percebes
off = {'Head': 0.0, 'Hand': 0.4, 'FaceUp': 0.8}
for o in objs:
    a = o.data.color_attributes.new('Off', 'FLOAT_COLOR', 'POINT')
    for i in range(len(o.data.vertices)): a.data[i].color = (off[o.name], 0, 0, 1)
mat = bpy.data.materials.new('SeaStone'); t = NT(mat)
for o in objs: o.data.materials.append(mat)
ofs, _, _ = t.attr('Off')
P = t.vm('ADD', t.geo.outputs['Position'], t.vec(t.m('MULTIPLY', ofs, 100.0), 0.0, 0.0))
nrm = t.N('ShaderNodeSeparateXYZ'); t.Lk(t.geo.outputs['Normal'], nrm.inputs[0]); NZ_ = nrm.outputs[2]
H_ = t.PZ
stoneV = t.noise(0.7, 5, 0.6, 0.0, P)
stone = t.mix(stoneV, (0.20, 0.20, 0.19), (0.36, 0.35, 0.32))
stone = t.mix(t.mr(t.noise(5, 4, 0.6, 0.0, P), 0.55, 0.8), stone, (0.44, 0.42, 0.38))
pores = t.mr(t.voro(30, 'F1', 'Distance', P), 0.14, 0.05)
c = t.mix(t.m('MULTIPLY', pores, 0.55), stone, (0.12, 0.12, 0.11))
chisel = t.noise(1.0, 2, 0.5, 0.0, t.vec(t.m('MULTIPLY', t.PX, 6.0), t.m('MULTIPLY', t.PY, 6.0), t.m('MULTIPLY', H_, 1.2)))
cr = t.voro(0.35, 'DISTANCE_TO_EDGE', 'Distance', P)
crack = t.m('MULTIPLY', t.mr(cr, 0.02, 0.0), t.mr(t.noise(0.5, 2, 0.5, 0.0, P), 0.55, 0.65))
c = t.mix(t.m('MULTIPLY', crack, 0.9), c, (0.06, 0.06, 0.055))
up = t.mr(NZ_, 0.35, 0.8, 0, 1, True)
alg = t.m('MULTIPLY', up, t.mr(t.noise(1.8, 5, 0.65, 0.3, P), 0.45, 0.6))
algc = t.mix(t.noise(14, 3, 0.5, 0.0, P), (0.07, 0.10, 0.04), (0.14, 0.17, 0.07))
c = t.mix(t.m('MULTIPLY', alg, 0.9), c, algc)
silt = t.mr(H_, 1.4, 0.0)
c = t.mix(t.m('MULTIPLY', silt, 0.7), c, (0.22, 0.20, 0.15))
barn = t.m('MULTIPLY', t.mr(t.voro(40, 'F1', 'Distance', P), 0.14, 0.07), t.mr(t.noise(2.5, 3, 0.5, 0.0, P), 0.52, 0.64))
c = t.mix(t.m('MULTIPLY', barn, 0.85), c, (0.55, 0.53, 0.47))
streak = t.noise(1.0, 3, 0.5, 0.0, t.vec(t.m('MULTIPLY', t.PX, 7.0), t.m('MULTIPLY', t.PY, 7.0), t.m('MULTIPLY', H_, 0.6)))
c = t.mulc(c, t.mr(streak, 0.3, 0.75, 0.82, 1.08))
t.Lk(c, t.bsdf.inputs['Base Color'])
t.Lk(t.m('SUBTRACT', 0.9, t.m('MULTIPLY', alg, 0.3)), t.bsdf.inputs['Roughness'])
h = t.m('ADD', t.m('MULTIPLY', crack, -1.0), t.m('MULTIPLY', pores, -0.6))
h = t.m('ADD', h, t.m('ADD', t.m('MULTIPLY', chisel, 0.5), t.m('MULTIPLY', barn, 1.0)))
h = t.m('ADD', h, t.m('MULTIPLY', t.noise(16, 4, 0.6, 0.0, P), 0.35))
t.bump(h, 0.7, 0.012)

gpu()
uv(objs, 0.002)
if not SKIP: bake(objs, t, TEX, OUT, 'SeaStone', ao_samples=128)
try:
    hand.location = (9, 0, 0); face.location = (-9, 0, 0)
    preview(OUT, [('all', (0, -26, 7), (0, 0, 3.5)), ('head', (4.5, -11, 5), (0, 0, 4.4)), ('hand', (13, -9, 4), (9, 0, 4.5)), ('face', (-6, -12, 3), (-9, 0, 2.2))],
            samples=int(os.environ.get('PROP_PREV', '48')), world=(0.35, 0.4, 0.42), lights=(('K', (8, -14, 16), 9000), ('R', (-10, 8, 8), 3000)))
    hand.location = (0, 0, 0); face.location = (0, 0, 0)
    L('preview ok')
except Exception as e: L('preview fail', e)
export(OUT, 'Sculptures.fbx', objs)
pc.LOG.close()
