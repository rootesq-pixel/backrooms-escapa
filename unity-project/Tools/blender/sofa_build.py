# v7.3.0: sofá empapado de la casa inundada (diseño propio). Cojines, brazos y respaldo modelados con caída,
# tela gastada con costuras, pliegues, manchas y la marca oscura del agua; texturas procedurales horneadas en la GPU.
# Blender: X ancho, -Y = atrás (Unity +Z), +Z arriba. Línea de flotación a z = 0.28 (draft del OceanFloat).
import bpy, sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from props_common import *
import props_common as pc
from mathutils import Vector, noise as mnoise
argv = sys.argv[sys.argv.index('--') + 1:]; OUT = argv[0]
TEX = int(os.environ.get('PROP_TEX', '2048')); SKIP = os.environ.get('PROP_SKIP_BAKE') == '1'
init(OUT, 'sofa_build.log'); rnd = random.Random(7311)
WL = 0.28
parts = []
def part(name, lo, hi, bevel, seg, subd, region, fold=0.0, sag=0.0, crown=0.0, tilt=0.0):
    o = box(name, lo, hi, bevel, seg, subd)
    c = (Vector(lo) + Vector(hi)) * 0.5; h = (Vector(hi) - Vector(lo)) * 0.5; rr = rnd.random()
    me = o.data
    for v in me.vertices:
        p = v.co - c
        fx = max(0.0, 1 - (p.x / h.x) ** 2); fy = max(0.0, 1 - (p.y / h.y) ** 2); fz = max(0.0, 1 - (p.z / h.z) ** 2)
        if sag: v.co.z -= sag * fx * fy * (0.5 + 0.5 * p.z / h.z)                       # hundido en el centro (empapado)
        if crown: v.co += v.normal * crown * fx * fy * fz                                # relleno abombado
        if fold:
            q = v.co * Vector((5.5, 5.5, 16.0)) + Vector((rr * 9, 0, 0))
            v.co += v.normal * fold * (mnoise.noise(q) + 0.5 * mnoise.noise(q * 2.3))
        if tilt: v.co.y -= tilt * (v.co.z - lo[2])
    def edge(co, n, i):
        p = co - c; d = sorted(max(0.0, h[k] - abs(p[k])) for k in range(3))
        return (region, min(1.0, d[1] / 0.1), rr, 1.0)
    set_attr(o, 'Mask', edge); parts.append(o); return o
# armazón y brazos
part('Base', (-0.95, -0.45, 0.06), (0.95, 0.42, 0.37), 0.035, 3, 1, 0.0, fold=0.002)
for s in (-1, 1):
    a = part('Arm', (min(s * 0.93, s * 1.14), -0.45, 0.06), (max(s * 0.93, s * 1.14), 0.45, 0.6), 0.075, 4, 2, 0.0, fold=0.003, crown=0.012)
    for v in a.data.vertices:
        if v.co.z > 0.48: v.co.x += s * 0.03 * min(1.0, (v.co.z - 0.48) / 0.12)    # brazo enrollado
        v.co.z -= 0.012 * max(0.0, 1 - (v.co.y / 0.45) ** 2) * (v.co.z > 0.5)        # cede en el centro
part('Back', (-0.95, -0.47, 0.36), (0.95, -0.25, 0.9), 0.06, 4, 2, 0.0, fold=0.003, sag=0.03, crown=0.01)
# cojines del asiento (el derecho más hundido) y del respaldo
part('SeatL', (-0.93, -0.25, 0.36), (-0.012, 0.44, 0.535), 0.05, 4, 2, 0.33, fold=0.0055, sag=0.045, crown=0.022)
part('SeatR', (0.012, -0.25, 0.355), (0.93, 0.44, 0.525), 0.05, 4, 2, 0.33, fold=0.0065, sag=0.065, crown=0.018)
part('BackL', (-0.92, -0.27, 0.53), (-0.012, -0.07, 0.88), 0.06, 4, 2, 0.33, fold=0.005, sag=0.02, crown=0.025, tilt=0.16)
part('BackR', (0.012, -0.27, 0.53), (0.92, -0.07, 0.87), 0.06, 4, 2, 0.33, fold=0.006, sag=0.03, crown=0.022, tilt=0.19)
for x in (-0.86, 0.86):
    for y in (-0.39, 0.36):
        f = cyl('Foot', (x, y, 0.035), 0.032, 0.07, seg=14, r2=0.024); set_attr(f, 'Mask', lambda co, n, i: (1.0, 1.0, 0.5, 1.0)); parts.append(f)
for o in parts:
    if o.name.startswith('Foot'): continue
    smooth(o)
sofa = join(parts, 'Sofa'); smooth(sofa, 50)
L('faces', len(sofa.data.polygons), 'verts', len(sofa.data.vertices))

# ---------------- material procedural de la tela
mat = bpy.data.materials.new('SofaFabric'); sofa.data.materials.append(mat); t = NT(mat)
reg, edgeN, prnd = t.attr('Mask')
edge = t.m('MULTIPLY', edgeN, 0.1)
P = t.geo.outputs['Position']
feet = t.mr(reg, 0.8, 0.9)
cush = t.mr(reg, 0.2, 0.4)
# tono base: verde oliva pardo, más desvaído en los cojines (sol) y con variación
var = t.noise(2.2, 4, 0.55)
base = t.mix(var, (0.17, 0.15, 0.085), (0.26, 0.22, 0.12))
base = t.mix(t.m('MULTIPLY', cush, 0.35), base, (0.30, 0.27, 0.17))
# trama fina (relieve y color)
# (v7.3.0b) sin trama periódica: daba moaré a cuadros al hornear; pelusa isótropa fina
weave = t.noise(120, 2, 0.5)
base = t.mulc(base, t.mr(weave, 0.3, 0.7, 0.95, 1.03))
# desgaste en cantos (pelusa, más claro)
rub = t.m('MULTIPLY', t.mr(edge, 0.03, 0.0), t.mr(t.noise(18, 3), 0.35, 0.7))
base = t.mix(t.m('MULTIPLY', rub, 0.55), base, (0.46, 0.43, 0.36))
# agua: empapado bajo la línea, humedad que sube por la tela, marca de marea y anillos
zw = t.m('ADD', t.PZ, t.m('ADD', t.m('MULTIPLY', t.m('SUBTRACT', t.noise(3.5, 4), 0.5), 0.06), t.m('MULTIPLY', t.m('SUBTRACT', t.noise(14, 3), 0.5), 0.02)))
wet = t.mr(zw, WL + 0.06, WL - 0.02, 0, 1, True)
damp = t.mr(zw, 0.7, WL + 0.04, 0, 1, True)
tide = t.mr(t.m('ABSOLUTE', t.m('SUBTRACT', zw, WL + 0.065)), 0.014, 0.0)
tide2 = t.m('MULTIPLY', t.mr(t.m('ABSOLUTE', t.m('SUBTRACT', zw, 0.47)), 0.008, 0.0), 0.55)
blot = t.noise(1.8, 5, 0.62, 0.3)
stain = t.mr(blot, 0.56, 0.66)
ring = t.m('MULTIPLY', t.mr(t.m('ABSOLUTE', t.m('SUBTRACT', blot, 0.57)), 0.007, 0.0), 0.3)
c = t.mix(t.m('MULTIPLY', stain, 0.3), base, (0.13, 0.09, 0.05))
c = t.mulc(c, t.m('SUBTRACT', 1.0, t.m('ADD', t.m('MULTIPLY', wet, 0.5), t.m('MULTIPLY', damp, 0.22))))
c = t.mix(t.m('MULTIPLY', t.m('MAXIMUM', t.m('MAXIMUM', tide, tide2), ring), 0.75), c, (0.11, 0.075, 0.045))
algae = t.m('MULTIPLY', wet, t.mr(t.noise(6, 4), 0.4, 0.7))
c = t.mix(t.m('MULTIPLY', algae, 0.45), c, (0.09, 0.11, 0.05))
mold = t.m('MULTIPLY', t.mr(t.voro(70), 0.16, 0.06), t.m('MULTIPLY', t.mr(zw, 0.55, 0.32), t.mr(t.noise(5, 3), 0.45, 0.7)))
c = t.mix(t.m('MULTIPLY', mold, 0.85), c, (0.035, 0.04, 0.03))
wood = t.mix(t.noise(40, 4), (0.07, 0.04, 0.025), (0.13, 0.08, 0.045))
c = t.mix(feet, c, wood)
t.Lk(c, t.bsdf.inputs['Base Color'])
rough = t.m('SUBTRACT', 0.93, t.m('ADD', t.m('MULTIPLY', wet, 0.4), t.m('MULTIPLY', damp, 0.1)))
rough = t.mix(feet, rough, 0.45) if False else t.m('ADD', t.m('MULTIPLY', rough, t.m('SUBTRACT', 1.0, feet)), t.m('MULTIPLY', feet, 0.42))
t.Lk(rough, t.bsdf.inputs['Roughness'])
# relieve: ribete en el canto, costura hundida al lado, pliegues verticales y trama
pipe = t.mr(edge, 0.009, 0.0, 0, 1, True)
groove = t.mr(t.m('ABSOLUTE', t.m('SUBTRACT', edge, 0.014)), 0.004, 0.0)
folds = t.noise(1.0, 3, 0.5, 0.0, t.vec(t.m('MULTIPLY', t.PX, 9.0), t.m('MULTIPLY', t.PY, 9.0), t.m('MULTIPLY', t.PZ, 30.0)))
h = t.m('ADD', t.m('ADD', t.m('MULTIPLY', pipe, 1.2), t.m('MULTIPLY', groove, -1.0)), t.m('ADD', t.m('MULTIPLY', folds, 1.1), t.m('MULTIPLY', weave, 0.12)))
h = t.m('MULTIPLY', h, t.m('SUBTRACT', 1.0, feet))
t.bump(h, 0.55, 0.004)

gpu()
uv([sofa])
if not SKIP: bake([sofa], t, TEX, OUT, 'SofaFabric')
try:
    preview(OUT, [('front', (1.6, 2.8, 1.25), (0, 0, 0.42)), ('close', (0.75, 1.15, 0.75), (0.25, 0, 0.4))], samples=int(os.environ.get('PROP_PREV', '48')),
            world=(0.35, 0.38, 0.4), lights=(('K', (2.5, 3, 3), 400), ('R', (-2.5, -2, 2), 200)))
    L('preview ok')
except Exception as e: L('preview fail', e)
export(OUT, 'Sofa.fbx', [sofa])
pc.LOG.close()
