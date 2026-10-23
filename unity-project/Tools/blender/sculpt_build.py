# v7.3.0: esculturas colosales del fondo (diseño propio): cabeza de piedra medio enterrada, mano gigante que sale de la arena
# y un rostro tumbado boca arriba. Piedra erosionada con grietas, algas y percebes; texturas procedurales horneadas en la GPU.
# Blender: Z arriba, la cara mira a -Y (Unity +Z). Origen de cada pieza en su base (se entierra en el juego).
import bpy, bmesh, sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from props_common import *
import props_common as pc
from mathutils import Vector, Matrix, noise as mnoise
argv = sys.argv[sys.argv.index('--') + 1:]; OUT = argv[0]
TEX = int(os.environ.get('PROP_TEX', '2048')); SKIP = os.environ.get('PROP_SKIP_BAKE') == '1'
init(OUT, 'sculpt_build.log'); rnd = random.Random(7313)

def g(d2, s): return math.exp(-d2 / (2 * s * s))
def face_disp(p, R, eyes_open=False, seed=0.0):
    """desplazamiento (m) del rostro sobre un elipsoide de radios R; la cara mira a -Y"""
    x, y, z = p.x / R.x, p.y / R.y, p.z / R.z
    front = max(0.0, -y)                      # 1 delante, 0 a los lados
    d = 0.0
    K = R.x / 2.3 * 1.55                      # rasgos más marcados (escala con el tamaño)
    d += 0.22 * g((z - 0.30) ** 2 * 9 + (x * 1.1) ** 2 * 0.6, 0.35) * front                      # arco de las cejas
    for sx in (-1, 1):
        e2 = (x - sx * 0.36) ** 2 + ((z - 0.13) * 1.6) ** 2
        d -= 0.42 * g(e2, 0.14) * front                                                          # cuencas
        if not eyes_open: d += 0.30 * g((x - sx * 0.36) ** 2 + ((z - 0.10) * 2.2) ** 2, 0.1) * front   # párpados cerrados
        else: d -= 0.18 * g(e2, 0.06) * front                                                    # ojos vacíos
        d += 0.20 * g((x - sx * 0.55) ** 2 + (z + 0.08) ** 2 * 2, 0.16) * front                 # pómulos
        d += 0.25 * g((x - sx * 0.98) ** 2 * 8 + (z - 0.02) ** 2 * 3 + (y + 0.0) ** 2 * 6, 0.12)   # orejas
    nz = (z + 0.18)                                                                              # nariz: de la frente a la punta
    if -0.32 < z < 0.25: d += 0.85 * g(x * x * 18, 0.25) * (1 - abs(nz - 0.0) / 0.55) ** 0.7 * front * min(1.0, (0.25 - z) / 0.5 + 0.35)
    d -= 0.10 * g((x * 4) ** 2 + ((z + 0.33) * 10) ** 2, 0.5) * front                            # bajo la nariz
    d += 0.26 * g((x * 1.5) ** 2 + ((z + 0.45) * 7) ** 2, 0.35) * front                          # labio superior
    d -= 0.14 * g((x * 1.2) ** 2 + ((z + 0.52) * 22) ** 2, 0.4) * front                          # comisura
    d += 0.22 * g((x * 1.6) ** 2 + ((z + 0.6) * 7) ** 2, 0.35) * front                           # labio inferior
    d += 0.40 * g((x * 1.4) ** 2 + ((z + 0.85) * 4) ** 2, 0.3) * front                           # barbilla
    d -= 0.25 * max(0.0, -z - 0.35) * abs(x) * 2                                                 # mandíbula más estrecha
    d *= K
    # erosión: grande, media y picado
    q = p * 0.35 + Vector((seed, 0, 0)); d += 0.12 * mnoise.fractal(q, 0.6, 2.0, 4)
    d += 0.035 * mnoise.fractal(p * 2.2 + Vector((seed, 3, 1)), 0.6, 2.0, 3)
    return d
def head_mesh(name, R, seg, eyes_open, seed, crack=True):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=seg // 2, radius=1.0)
    for v in bm.verts:
        p = Vector((v.co.x * R.x, v.co.y * R.y, v.co.z * R.z)); n = Vector((v.co.x / R.x, v.co.y / R.y, v.co.z / R.z)).normalized()
        dd = face_disp(p, R, eyes_open, seed)
        if crack:   # grieta que baja en zigzag desde la coronilla
            cx = 0.35 * R.x + 0.25 * mnoise.noise(Vector((p.z * 0.6, seed, 0))) * R.x
            dd -= 0.16 * g((p.x - cx) ** 2, 0.05) * (1 if p.z > -0.2 * R.z else 0)
        v.co = p + n * dd
    o = obj_from_bm(name, bm); return o
def bisect(o, co, no, fill=True):
    bm = bmesh.new(); bm.from_mesh(o.data)
    r = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=co, plane_no=no, clear_outer=True)
    if fill:
        edges = [e for e in r['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
        f = bmesh.ops.holes_fill(bm, edges=edges, sides=0)
        for fc in f['faces']:
            for v in fc.verts: pass
        bmesh.ops.triangulate(bm, faces=f['faces'])
        cut = set(v for fc in f['faces'] for v in fc.verts)
        # superficie de rotura rugosa
        nn = Vector(no).normalized()
        for v in cut: v.co -= nn * (0.18 * abs(mnoise.fractal(v.co * 0.9, 0.7, 2.0, 4)))
    bm.to_mesh(o.data); bm.free()
def remesh(o, vox):
    m = o.modifiers.new('rm', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = vox; m.use_smooth_shade = True
    s = o.modifiers.new('sm', 'SMOOTH'); s.iterations = 4; s.factor = 0.5
    apply_mods(o)
def erode(o, amp, sc, seed):
    for v in o.data.vertices:
        v.co += v.normal * (amp * mnoise.fractal(v.co * sc + Vector((seed, 0, 0)), 0.6, 2.0, 4) + amp * 0.35 * mnoise.fractal(v.co * sc * 5 + Vector((0, seed, 0)), 0.6, 2.0, 3))

# ---------------- 1) cabeza colosal (7 m), ojos cerrados, coronilla rota, ladeada
R = Vector((2.3, 2.6, 3.3))
head = head_mesh('Head', R, 220, False, 1.7)
bisect(head, (0, 0, 2.55), (0.35, 0.15, 1.0))           # coronilla partida
bisect(head, (0, 0, -2.6), (0, 0, -1.0))                # base (queda bajo la arena)
for v in head.data.vertices: v.co.z += 2.6
remesh(head, 0.045); erode(head, 0.03, 1.6, 2.0)
# ---------------- 2) mano gigante que sale del fondo (dedos abiertos, algo curvados)
mb = bpy.data.metaballs.new('HandMB'); mb.resolution = 0.07; mb.render_resolution = 0.07; mb.threshold = 0.6
ho = bpy.data.objects.new('HandMB', mb); bpy.context.scene.collection.objects.link(ho)
def ball(c, r):
    e = mb.elements.new(); e.type = 'BALL'; e.co = c; e.radius = r; e.stiffness = 2.0
for i in range(14):                                    # antebrazo y muñeca
    zz = -1.0 + i * 0.25; ball((0.05 * math.sin(i * 0.4), 0, zz), 0.62 - 0.012 * i)
for i in range(9):                                     # palma (ligeramente inclinada)
    for j in range(3):
        ball(((j - 1) * 0.42, 0.05 * i * 0.2, 2.6 + i * 0.12), 0.5)
fingers = [(-0.62, 1.55, 0.95, -0.12), (-0.2, 1.75, 1.0, -0.03), (0.22, 1.7, 0.98, 0.05), (0.6, 1.35, 0.9, 0.14)]
for k, (fx, flen, fr, spread) in enumerate(fingers):
    base = Vector((fx, 0.0, 3.55)); d = Vector((spread, -0.12, 1.0)).normalized(); r0 = 0.33 * fr
    p = base.copy(); n = 22
    for i in range(n + 1):
        f = i / n; bend = Vector((0, -0.55 * f * f, 0))
        q = base + d * flen * f + bend * flen
        ball(q, r0 * (1 - 0.32 * f) + 0.02 * math.sin(f * math.pi * 3) ** 2)  # nudillos
tb = Vector((-0.8, -0.1, 2.75)); td = Vector((-0.75, -0.5, 0.55)).normalized()
for i in range(18):
    f = i / 17; ball(tb + td * 1.15 * f + Vector((0, -0.3 * f * f, 0.25 * f * f)), 0.32 * (1 - 0.3 * f))
bpy.context.view_layer.update(); activate(ho); bpy.ops.object.convert(target='MESH')
hand = bpy.context.view_layer.objects.active; hand.name = 'Hand'
for v in hand.data.vertices: v.co.z += 1.0
remesh(hand, 0.04); erode(hand, 0.035, 1.8, 5.0)
bisect(hand, (0, 0, 0.02), (0, 0, -1.0))
# ---------------- 3) rostro tumbado boca arriba (5 m), ojos vacíos, medio hundido y roto por un lado
R2 = Vector((1.7, 1.9, 2.5))
face = head_mesh('FaceUp', R2, 180, True, 4.2, crack=False)
bisect(face, (0.9, 0, 0), (1.0, 0.25, 0.1))              # le falta un trozo del lado derecho
face.data.transform(Matrix.Rotation(math.radians(-90), 4, 'X'))   # mira hacia arriba (+Z)
bisect(face, (0, 0, -0.55), (0, 0, -1.0))
for v in face.data.vertices: v.co.z += 0.55
remesh(face, 0.04); erode(face, 0.03, 1.7, 7.0)
objs = [head, hand, face]
for o in objs: smooth(o)
for o in objs: L('faces', o.name, len(o.data.polygons))

# ---------------- material de piedra (coordenadas de objeto: cada pieza tiene las suyas; se separan con un desfase)
off = {'Head': Vector((0, 0, 0)), 'Hand': Vector((40, 0, 0)), 'FaceUp': Vector((80, 0, 0))}
for o in objs:
    a = o.data.color_attributes.new('Off', 'FLOAT_COLOR', 'POINT')
    for i in range(len(o.data.vertices)): a.data[i].color = (off[o.name].x / 100, 0, 0, 1)
mat = bpy.data.materials.new('SeaStone'); t = NT(mat)
for o in objs: o.data.materials.append(mat)
ofs, _, _ = t.attr('Off')
P = t.vm('ADD', t.geo.outputs['Position'], t.vec(t.m('MULTIPLY', ofs, 100.0), 0.0, 0.0))
nrm = t.N('ShaderNodeSeparateXYZ'); t.Lk(t.geo.outputs['Normal'], nrm.inputs[0]); NZ = nrm.outputs[2]
H = t.PZ
stoneV = t.noise(0.9, 5, 0.6, 0.0, P)
stone = t.mix(stoneV, (0.11, 0.115, 0.10), (0.27, 0.27, 0.24))
stone = t.mix(t.mr(t.noise(6, 4, 0.6, 0.0, P), 0.55, 0.8), stone, (0.40, 0.39, 0.35))
cr = t.voro(0.55, 'DISTANCE_TO_EDGE', 'Distance', P)
crack = t.m('MULTIPLY', t.mr(t.m('ADD', cr, t.m('MULTIPLY', t.noise(8, 3, 0.5, 0.0, P), 0.03)), 0.025, 0.0), t.mr(t.noise(0.7, 2, 0.5, 0.0, P), 0.5, 0.6))
pit = t.mr(t.voro(26, 'F1', 'Distance', P), 0.16, 0.05)
c = t.mix(t.m('MULTIPLY', crack, 0.9), stone, (0.05, 0.055, 0.05))
c = t.mix(t.m('MULTIPLY', pit, 0.5), c, (0.10, 0.11, 0.10))
up = t.mr(NZ, 0.0, 0.6, 0, 1, True)
algN = t.noise(2.5, 5, 0.65, 0.3, P)
alg = t.m('MULTIPLY', up, t.mr(algN, 0.36, 0.52))
algc = t.mix(t.noise(14, 3, 0.5, 0.0, P), (0.05, 0.09, 0.03), (0.13, 0.17, 0.06))
c = t.mix(t.m('MULTIPLY', alg, 0.92), c, algc)
moss2 = t.m('MULTIPLY', t.mr(H, 1.2, 0.0), t.mr(t.noise(4, 4, 0.6, 0.0, P), 0.45, 0.65))
c = t.mix(t.m('MULTIPLY', moss2, 0.8), c, (0.06, 0.07, 0.04))
silt = t.mr(H, 0.9, 0.0)
c = t.mix(t.m('MULTIPLY', silt, 0.6), c, (0.20, 0.18, 0.13))
barn = t.m('MULTIPLY', t.mr(t.voro(45, 'F1', 'Distance', P), 0.14, 0.07), t.mr(t.noise(3, 3, 0.5, 0.0, P), 0.5, 0.62))
c = t.mix(t.m('MULTIPLY', barn, 0.85), c, (0.52, 0.50, 0.44))
streak = t.noise(1.0, 3, 0.5, 0.0, t.vec(t.m('MULTIPLY', t.vm('DOT_PRODUCT', P, (1, 0, 0)), 9.0), t.m('MULTIPLY', t.vm('DOT_PRODUCT', P, (0, 1, 0)), 9.0), t.m('MULTIPLY', H, 0.8)))
c = t.mulc(c, t.mr(streak, 0.3, 0.75, 0.8, 1.08))
t.Lk(c, t.bsdf.inputs['Base Color'])
rough = t.m('SUBTRACT', 0.9, t.m('MULTIPLY', alg, 0.35))
t.Lk(rough, t.bsdf.inputs['Roughness'])
h = t.m('ADD', t.m('MULTIPLY', crack, -1.0), t.m('MULTIPLY', pit, -0.5))
h = t.m('ADD', h, t.m('ADD', t.m('MULTIPLY', alg, t.m('MULTIPLY', t.noise(60, 3, 0.6, 0.0, P), 0.8)), t.m('MULTIPLY', barn, 1.0)))
h = t.m('ADD', h, t.m('MULTIPLY', t.noise(18, 4, 0.6, 0.0, P), 0.4))
t.bump(h, 0.7, 0.01)

gpu()
uv(objs, 0.002)
if not SKIP: bake(objs, t, TEX, OUT, 'SeaStone', ao_samples=128)
try:
    hand.location = (8, 0, 0); face.location = (-8, 0, 0)
    preview(OUT, [('all', (0, -22, 6), (0, 0, 2.5)), ('head', (2.5, -9, 3), (0, 0, 2.6)), ('hand', (10, -6, 3), (8, 0, 3)), ('face', (-6, -6, 5), (-8, 0, 0.4))],
            samples=int(os.environ.get('PROP_PREV', '48')), world=(0.35, 0.4, 0.42), lights=(('K', (6, -10, 12), 4000), ('R', (-8, 6, 6), 1500)))
    hand.location = (0, 0, 0); face.location = (0, 0, 0)
    L('preview ok')
except Exception as e: L('preview fail', e)
export(OUT, 'Sculptures.fbx', objs)
pc.LOG.close()
