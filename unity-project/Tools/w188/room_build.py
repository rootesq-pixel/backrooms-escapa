# v7.12.1 Nivel 188: muebles de las habitaciones modelados por script en Blender 4.2 (CC0, propios). Sustituyen a las cajas de
# W188World.BuildFurniture: cama (somier de madera con patas, cabecero tapizado, colchón con sábana bajera, colcha que cae por los
# lados —simulación de tela—, dos almohadas mullidas), mesilla con cajón y pomo, lamparita (pie de cerámica, cuello de latón,
# bombilla y pantalla de tela), armario de dos puertas con plafones, escritorio con cajonera, silla con asiento tapizado, alfombra
# con cenefa, esquinas redondeadas y flecos, radiador de columnas, lámpara de techo con pantalla de tambor, marco del cuadro y
# barra de cortina con anillas.
# Coordenadas del cuarto (marco del lado en W188World): x = u - a (a = medianera de la cama), y arriba, z = -d (d hacia fuera del
# patio). A Blender se pasa como (-x, z, y) igual que build_hotel_furn.py; en el juego W188Furniture copia los vértices en las
# mallas por planta (sin objetos ni draw calls nuevos). Nombre de cada objeto: <Modelo>__<Material>. UV en metros.
#   blender -b -P Tools/w188/room_build.py -- <salida.fbx> [preview.png]
import bpy, bmesh, math, sys, random
from mathutils import Vector, Matrix
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '/workspace/v7121/blend/W188Room.fbx'
PREVIEW = argv[1] if len(argv) > 1 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
random.seed(1881)
TAU = math.pi * 2
scn = bpy.context.scene
OBJS = []   # (obj, model, mat, uvmode)

def B(x, y, z):   # coordenadas del cuarto (u', alto, d) -> Blender
    return Vector((-x, -z, y))

def mkobj(name, verts, faces, uvs=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.update()
    if uvs is not None:
        ul = me.uv_layers.new(name='UVMap')
        k = 0
        for p in me.polygons:
            for li in p.loop_indices:
                ul.data[li].uv = uvs[k]; k += 1
    ob = bpy.data.objects.new(name, me); scn.collection.objects.link(ob)
    return ob

def add(model, mat, ob, uv='box', bevel=None, sub=0, smooth=True, solid=None):
    if bevel:
        m = ob.modifiers.new('bev', 'BEVEL'); m.width = bevel[0]; m.segments = bevel[1]; m.limit_method = 'ANGLE'; m.angle_limit = math.radians(40)
        m.profile = 0.5 if len(bevel) < 3 else bevel[2]
    if solid:
        m = ob.modifiers.new('sol', 'SOLIDIFY'); m.thickness = solid; m.offset = 0
    if sub:
        m = ob.modifiers.new('sub', 'SUBSURF'); m.levels = sub; m.render_levels = sub
    OBJS.append((ob, model, mat, uv, smooth))
    return ob

def box(model, mat, x0, x1, y0, y1, d0, d1, bevel=None, uv='box', sub=0):
    a, b = B(x0, y0, d0), B(x1, y1, d1)
    mn = Vector((min(a.x, b.x), min(a.y, b.y), min(a.z, b.z))); mx = Vector((max(a.x, b.x), max(a.y, b.y), max(a.z, b.z)))
    V = [(mn.x, mn.y, mn.z), (mx.x, mn.y, mn.z), (mx.x, mx.y, mn.z), (mn.x, mx.y, mn.z), (mn.x, mn.y, mx.z), (mx.x, mn.y, mx.z), (mx.x, mx.y, mx.z), (mn.x, mx.y, mx.z)]
    F = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (0, 4, 7, 3)]
    return add(model, mat, mkobj(model + mat, V, F), uv, bevel, sub)

def lathe(model, mat, x, d, prof, seg=16, y=0.0, cap_top=True, cap_bot=False, uv='lathe', bevel=None, smooth=True):
    # prof: [(r, h)] de abajo arriba, alrededor del eje vertical en (x, d)
    V = []; F = []; UV = []; n = len(prof)
    for i in range(seg):
        t = TAU * i / seg
        for (r, h) in prof: V.append(B(x + r * math.cos(t), y + h, d + r * math.sin(t)))
    for i in range(seg):
        a, b = i, (i + 1) % seg
        for j in range(n - 1):
            F.append((a * n + j, b * n + j, b * n + j + 1, a * n + j + 1))
            circ = TAU * max(prof[j][0], 0.01)
            u0, u1 = i / seg * circ, (i + 1) / seg * circ
            UV += [(u0, prof[j][1]), (u1, prof[j][1]), (u1, prof[j + 1][1]), (u0, prof[j + 1][1])]
    if cap_top and prof[-1][0] > 0.001:
        F.append(tuple(i * n + n - 1 for i in range(seg))); UV += [(0.5 + 0.5 * math.cos(TAU * i / seg) * prof[-1][0], 0.5 + 0.5 * math.sin(TAU * i / seg) * prof[-1][0]) for i in range(seg)]
    if cap_bot and prof[0][0] > 0.001:
        F.append(tuple(i * n for i in range(seg))[::-1]); UV += [(0.5 + 0.5 * math.cos(TAU * i / seg) * prof[0][0], 0.5 + 0.5 * math.sin(TAU * i / seg) * prof[0][0]) for i in range(seg)][::-1]
    ob = mkobj(model + mat, V, F, UV)
    return add(model, mat, ob, 'keep', bevel, 0, smooth)

def tube(model, mat, pts, r, seg=8, caps=True):
    V = []; F = []; n = len(pts)
    P = [Vector(p) for p in pts]
    for k, p in enumerate(P):
        dvec = (P[min(k + 1, n - 1)] - P[max(k - 1, 0)]).normalized()
        a = Vector((0, 0, 1)) if abs(dvec.z) < 0.9 else Vector((1, 0, 0))
        u = dvec.cross(a).normalized(); w = dvec.cross(u).normalized()
        for i in range(seg):
            t = TAU * i / seg; V.append(p + (u * math.cos(t) + w * math.sin(t)) * r)
    for k in range(n - 1):
        for i in range(seg):
            a, b = i, (i + 1) % seg
            F.append((k * seg + a, k * seg + b, (k + 1) * seg + b, (k + 1) * seg + a))
    if caps:
        F.append(tuple(range(seg))[::-1]); F.append(tuple((n - 1) * seg + i for i in range(seg)))
    return add(model, mat, mkobj(model + mat, V, F), 'box', None, 0, True)

def grid(model, mat, fn, nu, nv, su, sv, uv_scale=(1, 1), closed_u=False):
    # superficie paramétrica fn(s, t) -> Blender Vector, s, t en [0, 1]; UV = (s * su, t * sv) metros
    V = []; F = []; UV = []
    cu = nu if closed_u else nu + 1
    for j in range(nv + 1):
        for i in range(cu): V.append(fn(i / nu, j / nv))
    for j in range(nv):
        for i in range(nu):
            a = j * cu + i; b = j * cu + (i + 1) % cu if closed_u else j * cu + i + 1
            F.append((a, b, b + cu, a + cu))
            UV += [(i / nu * su, j / nv * sv), ((i + 1) / nu * su, j / nv * sv), ((i + 1) / nu * su, (j + 1) / nv * sv), (i / nu * su, (j + 1) / nv * sv)]
    return mkobj(model + mat, V, F, UV)

# =============================================================== cama (cabecero contra la medianera, x = 0)
BED = 'Bed'
L0, L1, W0, W1 = 0.07, 2.0, 1.0, 2.46          # largo (x) y ancho (d)
# somier de madera: faldón perimetral y patas
for (x0, x1, d0, d1) in [(L0, L1, W0, W0 + 0.035), (L0, L1, W1 - 0.035, W1), (L1 - 0.035, L1, W0, W1)]:
    box(BED, 'Wood', x0, x1, 0.12, 0.33, d0, d1, bevel=(0.008, 2))
box(BED, 'Wood', L0, L1 - 0.035, 0.16, 0.19, W0 + 0.035, W1 - 0.035)     # tablero bajo el colchón (casi no se ve)
for lx in (L0 + 0.06, L1 - 0.05):
    for ld in (W0 + 0.05, W1 - 0.05):
        lathe(BED, 'Wood', lx, ld, [(0.028, 0.0), (0.024, 0.02), (0.032, 0.12)], seg=10, cap_top=False, cap_bot=True)
# cabecero: marco de madera con canto redondeado y panel tapizado acolchado delante
box(BED, 'Wood', 0.0, 0.07, 0.0, 1.12, W0 - 0.04, W0 + 0.03, bevel=(0.012, 3))
box(BED, 'Wood', 0.0, 0.07, 0.0, 1.12, W1 - 0.03, W1 + 0.04, bevel=(0.012, 3))
box(BED, 'Wood', 0.0, 0.07, 1.04, 1.13, W0 - 0.04, W1 + 0.04, bevel=(0.015, 3))
box(BED, 'Wood', 0.01, 0.05, 0.33, 1.04, W0 + 0.03, W1 - 0.03)
def headpanel(s, t):   # panel acolchado: abombado en el centro, capitoné suave
    d = W0 + 0.06 + s * (W1 - W0 - 0.12); y = 0.42 + t * 0.58
    es, et = math.sin(math.pi * s), math.sin(math.pi * t)
    tuft = 0.006 * (math.cos(TAU * 6 * s) * math.cos(TAU * 3 * t))
    x = 0.05 + 0.045 * (es * et) ** 0.35 + tuft * es * et
    return B(x, y, d)
add(BED, 'Uphol', grid(BED, 'Uphol', headpanel, 32, 16, W1 - W0 - 0.12, 0.58), 'keep')
# colchón con sábana bajera (cantos muy redondeados)
MT = 0.545
box(BED, 'Sheet', L0 + 0.02, L1 - 0.03, 0.30, MT, W0 + 0.03, W1 - 0.03, bevel=(0.05, 5))
# almohadas: cojín relleno (perfil de superelipse, costura en el canto, esquinas pellizcadas), apoyadas en el cabecero
def pillow(cx, cd, lx, wd, th, tilt, seed):
    rnd = random.Random(seed)
    nu, nv = 20, 14
    V = []; F = []; UV = []
    def pt(s, t, side):
        u = s * 2 - 1; v = t * 2 - 1
        f = max(0.0, (1 - abs(u) ** 3.0) * (1 - abs(v) ** 3.0)) ** 0.55
        pinch = 0.06 * (abs(u) * abs(v)) ** 2
        x = u * lx * 0.5 * (1 - 0.05 * (1 - v * v)) ; dd = v * wd * 0.5 * (1 - 0.07 * (1 - u * u))
        x *= (1 - pinch); dd *= (1 - pinch)
        z = side * th * 0.5 * f * (1 + 0.04 * math.sin(3 * u + seed) * math.cos(2 * v))
        return x, z, dd
    for side in (1, -1):
        o = len(V)
        for j in range(nv + 1):
            for i in range(nu + 1):
                x, z, dd = pt(i / nu, j / nv, side)
                # inclinación: el canto de arriba hacia el cabecero
                ca, sa = math.cos(tilt), math.sin(tilt)
                xr = x * ca - z * sa; zr = x * sa + z * ca
                V.append(B(cx + xr, MT + th * 0.5 * ca + max(0, lx * 0.5 * sa) * 0.9 + zr, cd + dd))
        for j in range(nv):
            for i in range(nu):
                a = o + j * (nu + 1) + i
                q = (a, a + 1, a + nu + 2, a + nu + 1)
                F.append(q if side > 0 else q[::-1])
                UV += [(i / nu * lx, j / nv * wd), ((i + 1) / nu * lx, j / nv * wd), ((i + 1) / nu * lx, (j + 1) / nv * wd), (i / nu * lx, (j + 1) / nv * wd)][:: 1 if side > 0 else -1]
    ob = mkobj(BED + 'Pillow', V, F, UV)
    me = ob.data; bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005); bm.to_mesh(me); bm.free()
    return add(BED, 'Pillow', ob, 'keep')
pillow(0.33, W0 + 0.39, 0.46, 0.68, 0.17, math.radians(-24), 1)
pillow(0.34, W1 - 0.39, 0.46, 0.68, 0.16, math.radians(-21), 2)

# colcha: tela que cubre el colchón, se enrolla en el canto y cuelga con pliegues que crecen hacia abajo; esquinas de los pies
# en abanico y embozo doblado en la cabecera (superficie paramétrica: determinista y ligera)
def duvet_pos(cx, cd):
    X0, Xf, Dl, Dh, top, r = 0.64, 1.985, 1.015, 2.445, MT + 0.028, 0.045
    if cx < X0:   # embozo: la tela da la vuelta y vuelve sobre sí misma hacia los pies (solo encima del colchón)
        f = X0 - cx; rf = 0.02
        if f < math.pi * rf:
            a = f / rf; fx, fy = -rf * math.sin(a), rf - rf * math.cos(a)
        else: fx, fy = f - math.pi * rf, 2 * rf + 0.002 * math.sin(cd * 31)
        P = duvet_pos(X0 + 1e-5, cd)
        outside = max(0.0, Dl - cd, cd - Dh); k = max(0.0, 1.0 - outside / 0.07)
        k = k * k * (3 - 2 * k)
        return P + Vector((-fx * k, 0.0, fy * k + 0.001 * k))
    ox = max(0.0, cx - Xf); dlo = max(0.0, Dl - cd); dhi = max(0.0, cd - Dh); od = dlo if dlo > 0 else -dhi
    puff = 0.006 * math.sin(cx * 7.3 + 0.4) * math.sin(cd * 5.1 + 1.1) + 0.003 * math.sin(cx * 19 + cd * 13)
    if ox == 0 and od == 0:
        edge = min(Xf - cx, cd - Dl, Dh - cd); k = min(1.0, edge / 0.06)
        return B(cx, top - 0.012 * (1 - k) ** 2 + puff * k, cd)
    e = math.hypot(ox, od); ux, ud = ox / e, -od / e
    ex, ed = min(cx, Xf), min(max(cd, Dl), Dh)
    if e < r * math.pi / 2:
        a = e / r; out = r * math.sin(a); y = top - r + r * math.cos(a); hang = 0.0
    else:
        hang = e - r * math.pi / 2; out = r + 0.06 * hang; y = top - r - hang
    corner = ox > 0 and od != 0
    ph = (cd * 21.0 + 0.7 * math.sin(cd * 7)) if ox > 0 and not corner else (cx * 17.0 + 0.9 * math.sin(cx * 5)) if not corner else math.atan2(od, ox) * 5.0
    amp = (0.016 + (0.03 if corner else 0.0)) * min(1.0, hang / 0.18)
    fold = amp * math.sin(ph) + 0.4 * amp * math.sin(ph * 2.3 + 1.0)
    return B(ex + ux * (out + fold), y, ed + ud * (out + fold))
cx0, cx1, cd0, cd1 = 0.36, 1.985 + 0.33, 1.015 - 0.33, 2.445 + 0.33
dv = grid(BED, 'Duvet', lambda s, t: duvet_pos(cx0 + s * (cx1 - cx0), cd0 + t * (cd1 - cd0)), 46, 40, cx1 - cx0, cd1 - cd0)
add(BED, 'Duvet', dv, 'keep', None, 0, True, solid=0.012)

# =============================================================== mesilla con cajón y lamparita
NS = 'Nightstand'
box(NS, 'Wood', 0.02, 0.40, 0.06, 0.52, 0.47, 0.90, bevel=(0.006, 2))
box(NS, 'Wood', 0.0, 0.42, 0.52, 0.55, 0.45, 0.92, bevel=(0.008, 3))      # tablero con canto redondeado
box(NS, 'Wood', 0.40, 0.415, 0.33, 0.49, 0.50, 0.87, bevel=(0.004, 2))   # frente del cajón
box(NS, 'Wood', 0.40, 0.413, 0.09, 0.30, 0.50, 0.87, bevel=(0.004, 2))   # puerta
for (ld) in (0.49, 0.88):
    for lx in (0.04, 0.38):
        lathe(NS, 'Wood', lx, ld, [(0.018, 0.0), (0.022, 0.06)], seg=8, cap_top=False, cap_bot=True)
lathe(NS, 'Brass', 0.43, 0.685, [(0.004, 0.0), (0.004, 0.012), (0.012, 0.014), (0.013, 0.022), (0.0, 0.026)], seg=10, y=0.41, cap_top=False)
# (el pomo está tumbado: se gira en el script de abajo)
LAMPX, LAMPD, LY = 0.21, 0.69, 0.55
lathe('TableLamp', 'Ceramic', LAMPX, LAMPD, [(0.0, 0.0), (0.075, 0.0), (0.078, 0.01), (0.072, 0.02), (0.09, 0.07), (0.095, 0.12), (0.08, 0.17), (0.045, 0.21), (0.022, 0.23), (0.018, 0.245)], seg=24, y=LY)
lathe('TableLamp', 'Brass', LAMPX, LAMPD, [(0.012, 0.24), (0.008, 0.25), (0.008, 0.36), (0.014, 0.37), (0.014, 0.40)], seg=12, y=LY)
lathe('TableLamp', 'Bulb', LAMPX, LAMPD, [(0.012, 0.40), (0.028, 0.42), (0.032, 0.45), (0.025, 0.48), (0.0, 0.495)], seg=12, y=LY, cap_top=False)
shade = [(0.165, 0.315), (0.162, 0.33), (0.135, 0.47), (0.105, 0.535), (0.102, 0.545)]
lathe('TableLamp', 'Shade', LAMPX, LAMPD, shade, seg=32, y=LY, cap_top=False, cap_bot=False)
# cara interior de la pantalla (luz cálida dentro)
lathe('TableLamp', 'ShadeIn', LAMPX, LAMPD, [(r - 0.003, h) for (r, h) in shade][::-1], seg=32, y=LY, cap_top=False, cap_bot=False)
# cantos ribeteados de la pantalla
for (r, h) in (shade[0], shade[-1]):
    pts = [B(LAMPX + r * math.cos(TAU * i / 32), LY + h, LAMPD + r * math.sin(TAU * i / 32)) for i in range(33)]
    tube('TableLamp', 'Shade', pts, 0.004, 6, caps=False)

# =============================================================== armario
WD = 'Wardrobe'
box(WD, 'Wood', 0.0, 0.58, 0.07, 1.99, 3.30, 3.98, bevel=(0.006, 2))
box(WD, 'Wood', 0.0, 0.62, 1.99, 2.05, 3.27, 4.01, bevel=(0.01, 3, 0.3))    # cornisa
box(WD, 'Wood', 0.0, 0.60, 0.0, 0.07, 3.31, 3.97, bevel=(0.004, 2))          # zócalo
for (d0, d1) in ((3.31, 3.638), (3.642, 3.97)):
    box(WD, 'Wood', 0.58, 0.60, 0.09, 1.97, d0, d1, bevel=(0.004, 2))                     # hoja
    box(WD, 'Wood', 0.60, 0.608, 0.20, 1.02, d0 + 0.05, d1 - 0.05, bevel=(0.006, 3, 0.25))  # plafón bajo
    box(WD, 'Wood', 0.60, 0.608, 1.12, 1.86, d0 + 0.05, d1 - 0.05, bevel=(0.006, 3, 0.25))  # plafón alto
for hd in (3.615, 3.665):
    tube(WD, 'Brass', [B(0.615, 0.98, hd), B(0.63, 0.99, hd), B(0.63, 1.11, hd), B(0.615, 1.12, hd)], 0.006, 8)

# =============================================================== escritorio y silla (solo en las habitaciones)
DK = 'Desk'; DX1 = 3.08
box(DK, 'Wood', DX1 - 0.38, DX1, 0.725, 0.76, 0.43, 1.27, bevel=(0.008, 3))
for ld in (0.47, 1.23):
    for lx in (DX1 - 0.34, DX1 - 0.04):
        lathe(DK, 'Wood', lx, ld, [(0.014, 0.0), (0.02, 0.5), (0.022, 0.725)], seg=10, cap_bot=True)
box(DK, 'Wood', DX1 - 0.36, DX1 - 0.03, 0.52, 0.725, 0.46, 0.86, bevel=(0.005, 2))        # cajonera
box(DK, 'Wood', DX1 - 0.375, DX1 - 0.36, 0.54, 0.705, 0.48, 0.84, bevel=(0.003, 2))      # frente del cajón
box(DK, 'Wood', DX1 - 0.37, DX1 - 0.03, 0.70, 0.725, 0.86, 1.25)                           # faldón
for k in range(5):   # papeles sueltos
    o = k * 0.004; sh = random.uniform(-0.01, 0.01)
    box(DK, 'Paper', DX1 - 0.30 + sh, DX1 - 0.09 + sh, 0.76 + o, 0.7625 + o, 0.93 + sh, 1.21 + sh)
box(DK, 'Uphol', DX1 - 0.32, DX1 - 0.10, 0.76, 0.795, 0.52, 0.80, bevel=(0.006, 2))      # libro
box(DK, 'Paper', DX1 - 0.315, DX1 - 0.105, 0.764, 0.791, 0.523, 0.797)
CH = 'Chair'; CX, CD = 2.46, 0.85
for (lx, ld) in ((-0.18, -0.18), (0.18, -0.18), (-0.18, 0.18), (0.18, 0.18)):
    h = 0.92 if lx < 0 else 0.44
    lathe(CH, 'Wood', CX + lx, CD + ld, [(0.014, 0.0), (0.018, 0.2), (0.016, h)], seg=8, cap_bot=True)
box(CH, 'Wood', CX - 0.21, CX + 0.21, 0.40, 0.44, CD - 0.21, CD + 0.21, bevel=(0.008, 2))
box(CH, 'Uphol', CX - 0.19, CX + 0.19, 0.44, 0.48, CD - 0.19, CD + 0.19, bevel=(0.018, 4))
def backrail(s, t):
    d = CD - 0.2 + 0.4 * s; y = 0.80 + 0.1 * t
    return B(CX - 0.19 - 0.025 * math.sin(math.pi * s) - 0.0 * t, y, d)
r = grid(CH, 'Wood', backrail, 12, 2, 0.4, 0.1); add(CH, 'Wood', r, 'keep', None, 0, True, solid=0.022)
for sd in (-0.08, 0.0, 0.08):
    tube(CH, 'Wood', [B(CX - 0.19, 0.48, CD + sd), B(CX - 0.197, 0.64, CD + sd), B(CX - 0.205, 0.80, CD + sd)], 0.009, 6)
for (a, b) in (((-0.18, -0.18), (0.18, -0.18)), ((-0.18, 0.18), (0.18, 0.18))):
    tube(CH, 'Wood', [B(CX + a[0], 0.16, CD + a[1]), B(CX + b[0], 0.16, CD + b[1])], 0.008, 6)

# =============================================================== alfombra: campo, cenefa, canto redondeado y flecos
RG = 'Rug'; RX0, RX1, RD0, RD1 = 0.72, 2.58, 1.25, 3.0; RT = 0.011
def rrect(x0, x1, d0, d1, rad, n=6):
    pts = []
    for (cx, cd, a0) in ((x1 - rad, d1 - rad, 0), (x0 + rad, d1 - rad, 90), (x0 + rad, d0 + rad, 180), (x1 - rad, d0 + rad, 270)):
        for k in range(n + 1):
            a = math.radians(a0 + 90 * k / n); pts.append((cx + rad * math.cos(a), cd + rad * math.sin(a)))
    return pts
def ring(model, mat, outer, inner, y):
    V = [B(x, y, d) for (x, d) in outer] + [B(x, y, d) for (x, d) in inner]; n = len(outer)
    F = [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    UV = []
    for f in F: UV += [(outer[k][0] if k < n else inner[k - n][0], outer[k][1] if k < n else inner[k - n][1]) for k in f]
    return add(model, mat, mkobj(model + mat, V, F, UV), 'keep', None, 0, False)
outer = rrect(RX0, RX1, RD0, RD1, 0.03); inner = rrect(RX0 + 0.13, RX1 - 0.13, RD0 + 0.13, RD1 - 0.13, 0.02)
ring(RG, 'RugBorder', outer, inner, RT)
Vf = [B(x, RT, d) for (x, d) in inner]; ob = mkobj('RugField', Vf, [tuple(range(len(inner)))], [(x, d) for (x, d) in inner])
add(RG, 'Rug', ob, 'keep', None, 0, False)
# canto: tira vertical alrededor
n = len(outer); V = [B(x, 0.001, d) for (x, d) in outer] + [B(x, RT, d) for (x, d) in outer]
F = [(i, (i + 1) % n, n + (i + 1) % n, n + i)[::-1] for i in range(n)]
add(RG, 'RugBorder', mkobj('RugEdge', V, F), 'box', None, 0, True)
for xend, sgn in ((RX0, -1), (RX1, 1)):   # flecos en los lados cortos
    k = 0; d = RD0 + 0.05
    while d < RD1 - 0.05:
        L = 0.055 + random.uniform(-0.01, 0.012); w = 0.004; tw = random.uniform(-0.012, 0.012)
        p0 = B(xend, 0.004, d); p1 = B(xend + sgn * L, 0.002, d + tw)
        side = Vector((0, 0, 0)); dirv = (p1 - p0).normalized(); pv = dirv.cross(Vector((0, 0, 1))).normalized() * w
        V = [p0 - pv, p0 + pv, p1 + pv, p1 - pv]
        add(RG, 'Fringe', mkobj('Fringe', V, [(0, 1, 2, 3)], [(0, 0), (0.01, 0), (0.01, L), (0, L)]), 'keep', None, 0, False)
        d += 0.022; k += 1

# =============================================================== radiador de columnas bajo la ventana (centrado en x = 1.54)
RA = 'Radiator'; RCX = 1.54; NSEC = 10; PITCH = 0.105
for k in range(NSEC):
    sx = RCX + (k - (NSEC - 1) / 2) * PITCH
    for dd in (0.315, 0.385):
        # columna redondeada (tubo vertical con cuello arriba y abajo)
        lathe(RA, 'Paint', sx, dd, [(0.0, 0.14), (0.016, 0.142), (0.022, 0.16), (0.022, 0.64), (0.016, 0.66), (0.0, 0.662)], seg=10, cap_top=False)
    box(RA, 'Paint', sx - 0.018, sx + 0.018, 0.17, 0.63, 0.33, 0.37, bevel=(0.006, 2))   # alma entre columnas
x0r, x1r = RCX - NSEC / 2 * PITCH, RCX + NSEC / 2 * PITCH
tube(RA, 'Paint', [B(x0r, 0.175, 0.35), B(x1r, 0.175, 0.35)], 0.018, 10)
tube(RA, 'Paint', [B(x0r, 0.625, 0.35), B(x1r, 0.625, 0.35)], 0.018, 10)
for fx in (x0r + 0.08, x1r - 0.08):
    box(RA, 'Paint', fx - 0.02, fx + 0.02, 0.0, 0.16, 0.30, 0.40, bevel=(0.005, 2))
tube(RA, 'Brass', [B(x1r, 0.175, 0.35), B(x1r + 0.06, 0.175, 0.35), B(x1r + 0.07, 0.15, 0.35), B(x1r + 0.07, 0.0, 0.35)], 0.011, 8)
lathe(RA, 'Brass', x1r + 0.07, 0.35, [(0.0, 0.2), (0.022, 0.2), (0.022, 0.24), (0.0, 0.24)], seg=10, y=0.0, cap_top=False)

# =============================================================== lámpara de techo (pantalla de tambor; el techo está a 2.75)
CL = 'CeilLamp'; CLX, CLD, YC = 1.54, 2.0, 2.75
lathe(CL, 'Brass', CLX, CLD, [(0.0, YC - 0.03), (0.06, YC - 0.03), (0.065, YC - 0.01), (0.065, YC)], seg=16, cap_top=False)
lathe(CL, 'Brass', CLX, CLD, [(0.004, YC - 0.36), (0.004, YC - 0.03)], seg=6, cap_top=False)
lathe(CL, 'Bulb', CLX, CLD, [(0.0, YC - 0.47), (0.03, YC - 0.45), (0.035, YC - 0.41), (0.012, YC - 0.37), (0.012, YC - 0.36)], seg=12, cap_top=False)
drum = [(0.21, YC - 0.56), (0.21, YC - 0.33)]
lathe(CL, 'Shade', CLX, CLD, drum, seg=40, cap_top=False)
lathe(CL, 'ShadeIn', CLX, CLD, [(0.207, YC - 0.33), (0.207, YC - 0.56)], seg=40, cap_top=False)
lathe(CL, 'Bulb', CLX, CLD, [(0.0, YC - 0.545), (0.207, YC - 0.545)], seg=40, cap_top=False)   # difusor de abajo
for h in drum:
    pts = [B(CLX + 0.212 * math.cos(TAU * i / 32), h[1], CLD + 0.212 * math.sin(TAU * i / 32)) for i in range(33)]
    tube(CL, 'Brass', pts, 0.004, 4, caps=False)

# =============================================================== marco del cuadro (sobre la cama, en la medianera x = 0)
PF = 'PicFrame'; PY0, PY1, PD0, PD1 = 1.25, 1.85, 1.35, 2.15
for (y0, y1, d0, d1) in ((PY0, PY0 + 0.05, PD0, PD1), (PY1 - 0.05, PY1, PD0, PD1), (PY0, PY1, PD0, PD0 + 0.05), (PY0, PY1, PD1 - 0.05, PD1)):
    box(PF, 'Frame', 0.0, 0.055, y0, y1, d0, d1, bevel=(0.008, 2))

# =============================================================== barra de cortina (centrada en la ventana: x = 0 en uc; z = -d)
CR = 'CurtainRod'; RL = 0.87; RY = 2.41; RDD = 0.34   # (en el juego se coloca en u = uc, y0, d = 0)
tube(CR, 'Brass', [B(-RL, RY, RDD), B(RL, RY, RDD)], 0.011, 8)
for sx in (-1, 1):
    pts = [B(sx * (RL + 0.005), RY, RDD), B(sx * (RL + 0.03), RY, RDD)]
    tube(CR, 'Brass', pts, 0.018, 10)
    tube(CR, 'Brass', [B(sx * (RL - 0.12), RY, RDD), B(sx * (RL - 0.12), RY, 0.26)], 0.006, 6)   # soporte a la pared
for k in range(12):
    rx = -RL + 0.06 + k * (2 * RL - 0.12) / 11
    pts = [B(rx, RY + 0.017 * math.cos(TAU * i / 8), RDD + 0.017 * math.sin(TAU * i / 8)) for i in range(9)]
    tube(CR, 'Brass', pts, 0.0035, 4, caps=False)

# =============================================================== a FBX: aplicar modificadores, UV de caja, unir por modelo y material
dg = bpy.context.evaluated_depsgraph_get()
groups = {}
for ob, model, mat, uv, sm in OBJS:
    if len(ob.data.vertices) < 3: continue
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.transform(bm, matrix=ob.matrix_world, verts=bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.00005)
    bm.normal_update()
    ul = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
    if uv == 'box' or not ob.data.uv_layers:
        for f in bm.faces:
            n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
            for lp in f.loops:
                p = lp.vert.co; lp[ul].uv = (p.y, p.z) if ax == 0 else ((p.x, p.z) if ax == 1 else (p.x, p.y))
    for f in bm.faces: f.smooth = sm
    if sm:
        for e in bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(50): e.smooth = False
    groups.setdefault((model, mat), []).append(bm)
for ob, *_ in OBJS:
    if ob.name in bpy.data.objects: bpy.data.objects.remove(ob, do_unlink=True)
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
mats = {}
total = 0
for (model, mat), bms in groups.items():
    me = bpy.data.meshes.new(model + '__' + mat)
    acc = bmesh.new()
    for bm in bms:
        tmp = bpy.data.meshes.new('t'); bm.to_mesh(tmp); acc.from_mesh(tmp); bpy.data.meshes.remove(tmp); bm.free()
    bmesh.ops.triangulate(acc, faces=[f for f in acc.faces if len(f.verts) > 4])
    acc.to_mesh(me); acc.free()
    ob = bpy.data.objects.new(model + '__' + mat, me); scn.collection.objects.link(ob)
    if mat not in mats: mats[mat] = bpy.data.materials.new(mat)
    me.materials.append(mats[mat])
    tris = sum(len(p.vertices) - 2 for p in me.polygons); total += tris
    print('OBJ', ob.name, len(me.vertices), 'v', tris, 't')
print('ROOM total tris', total)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         mesh_smooth_type='OFF', add_leaf_bones=False, bake_space_transform=False, object_types={'MESH'}, use_mesh_modifiers=True)
if '/Assets/' not in OUT: bpy.ops.wm.save_as_mainfile(filepath=OUT.replace('.fbx', '.blend'))   # (para las vistas previas)
