# v7.1.0 Parque de bolas: sala de juegos fija (bloque 5x4 celdas de 4 m) construida en Blender, horneada con Cycles.
# Blender: x = Unity x, y = Unity z, z = Unity y; suelo del pasillo z=0; origen = centro del bloque.
# Uso: blender -b -t 8 -P build_hall.py -- <spp> <res> [render]   (render = además una imagen de referencia Cycles)
import bpy, bmesh, math, sys, json, struct, os, random
import numpy as np
from mathutils import Vector, Matrix
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
SPP = int(argv[0]) if argv else 256; RES = int(argv[1]) if len(argv) > 1 else 2048
MODE = argv[2] if len(argv) > 2 else 'bake'
OUT = '/workspace/ballpit/bake/'; TEX = '/workspace/ballpit/tex/'; RTEX = '/workspace/BackroomsEscape/Assets/Resources/Textures/'
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
random.seed(7)
CS = 4.0; T = 0.2; NX, NY = 5, 4
HW = NX * CS / 2 - T / 2 - 0.004; HD = NY * CS / 2 - T / 2 - 0.004; H = 4.6
DW = 2.0; DH = 2.5
# puertas: (lado, posición a lo largo)  S col 3, N col 0, O fila 1, E fila 1 (mismo criterio que BackroomsGenerator.HallDoor)
def ccol(i): return -NX * CS / 2 + (i + 0.5) * CS
def crow(j): return -NY * CS / 2 + (j + 0.5) * CS
DOORS = {'S': ccol(3), 'N': ccol(0), 'W': crow(1), 'E': crow(1)}
# materiales (índice = submalla en Unity)
M_WALL, M_FLOOR, M_CEIL, M_RED, M_YEL, M_BLU, M_GRN, M_PLY, M_PLB, M_NET, M_EMIT, M_BALL, M_METAL, M_ATLAS = range(14)
NMAT = 14
ATL = json.load(open('/workspace/ballpit/tex/atlas.json'))
AREC = []   # atlas: ('q', origen, u, v, región) proyección plana | ('f', min, max, región) relleno con una muestra
TILE = {M_WALL: 4.8, M_FLOOR: 2.4, M_CEIL: 2.4, M_RED: 1.2, M_YEL: 1.2, M_BLU: 1.2, M_GRN: 1.2, M_PLY: 1.5, M_PLB: 1.5, M_NET: 1.0, M_EMIT: 1.0, M_BALL: 1.0, M_METAL: 1.0, M_ATLAS: 1.0}
PADS = [M_RED, M_YEL, M_BLU, M_GRN]

bm = bmesh.new()
uvwall = {}   # cara -> (u,v) explícito para las paredes (altura absoluta)

def quad(v, m):
    f = bm.faces.new([bm.verts.new(p) for p in v]); f.material_index = m; return f

_tmpme = None
def box(x0, x1, y0, y1, z0, z1, m, bevel=0.0, skip=()):
    """caja con normales hacia fuera; bevel>0 -> esquinas acolchadas (se monta aparte y se añade a bm)"""
    global _tmpme
    if x1 - x0 < 1e-4 or y1 - y0 < 1e-4 or z1 - z0 < 1e-4: return
    tb = bmesh.new()
    vs = [tb.verts.new(p) for p in ((x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1))]
    F = ((3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (3, 0, 4, 7), (1, 2, 6, 5))
    for idx in F: tb.faces.new([vs[i] for i in idx])
    bev = min(bevel, (x1 - x0) * 0.45, (y1 - y0) * 0.45, (z1 - z0) * 0.45)
    if bev > 0.002:
        bmesh.ops.bevel(tb, geom=list(tb.edges), offset=bev, segments=2, profile=0.6, affect='EDGES', clamp_overlap=True)
    tb.normal_update()
    for f in list(tb.faces):
        f.material_index = m; f.smooth = bev > 0.002
        if 'b' in skip and f.normal.z < -0.9 and abs(f.calc_center_median().z - z0) < 1e-3: tb.faces.remove(f)
    if _tmpme is None: _tmpme = bpy.data.meshes.new('tmpbox')
    tb.to_mesh(_tmpme); tb.free()
    bm.from_mesh(_tmpme)

def cyl(cx, cy, r, z0, z1, m, seg=16, caps=True, rtop=None):
    top = []; bot = []; rt = r if rtop is None else rtop
    for i in range(seg):
        a = 2 * math.pi * i / seg
        bot.append(bm.verts.new((cx + r * math.cos(a), cy + r * math.sin(a), z0))); top.append(bm.verts.new((cx + rt * math.cos(a), cy + rt * math.sin(a), z1)))
    for i in range(seg):
        j = (i + 1) % seg
        f = bm.faces.new((bot[i], bot[j], top[j], top[i])); f.material_index = m; f.smooth = True
    if caps:
        f = bm.faces.new(top); f.material_index = m
        f = bm.faces.new(bot[::-1]); f.material_index = m

def rect_x(y, x0, x1, z0, z1, m, inward):
    v = [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)]
    return quad(v[::-1] if inward > 0 else v, m)
def rect_y(x, y0, y1, z0, z1, m, inward):
    v = [(x, y1, z0), (x, y0, z0), (x, y0, z1), (x, y1, z1)]
    return quad(v[::-1] if inward > 0 else v, m)
def rect_z(z, x0, x1, y0, y1, m, up):
    v = [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)]
    return quad(v if up > 0 else v[::-1], m)

# ---------------- paredes con huecos de puerta, techo ----------------
def wall_x(y, inward, dx):
    rect_x(y, -HW, dx - DW / 2, 0, H, M_WALL, inward); rect_x(y, dx + DW / 2, HW, 0, H, M_WALL, inward); rect_x(y, dx - DW / 2, dx + DW / 2, DH, H, M_WALL, inward)
    yo = y - inward * (T + 0.001); a, b = sorted((y, yo))
    rect_y(dx - DW / 2 + 0.002, a, b, 0, DH, M_WALL, 1); rect_y(dx + DW / 2 - 0.002, a, b, 0, DH, M_WALL, -1)
    rect_z(DH - 0.002, dx - DW / 2, dx + DW / 2, a, b, M_WALL, -1)
    rect_z(0.0, dx - DW / 2, dx + DW / 2, a, b, M_FLOOR, 1)
def wall_y(x, inward, dy):
    rect_y(x, -HD, dy - DW / 2, 0, H, M_WALL, inward); rect_y(x, dy + DW / 2, HD, 0, H, M_WALL, inward); rect_y(x, dy - DW / 2, dy + DW / 2, DH, H, M_WALL, inward)
    xo = x - inward * (T + 0.001); a, b = sorted((x, xo))
    rect_x(dy - DW / 2 + 0.002, a, b, 0, DH, M_WALL, 1); rect_x(dy + DW / 2 - 0.002, a, b, 0, DH, M_WALL, -1)
    rect_z(DH - 0.002, a, b, dy - DW / 2, dy + DW / 2, M_WALL, -1)
    rect_z(0.0, a, b, dy - DW / 2, dy + DW / 2, M_FLOOR, 1)
wall_x(-HD, 1, DOORS['S']); wall_x(HD, -1, DOORS['N']); wall_y(-HW, 1, DOORS['W']); wall_y(HW, -1, DOORS['E'])
rect_z(H - 0.002, -HW, HW, -HD, HD, M_CEIL, -1)

# ---------------- piscinas de bolas (hundidas) ----------------
PITS = [  # x0,x1,y0,y1, fondo, nivel de bolas, borde, abertura (lado, a, b)
    dict(r=(-6.6, 0.4, 0.4, 7.4), floor=-0.55, ball=0.14, rim=0.48, open=('s', -3.6, -2.4), col=M_RED),
    dict(r=(-8.8, -5.4, -6.8, -4.0), floor=-0.35, ball=0.12, rim=0.36, open=('e', -5.9, -4.9), col=M_BLU),
]
RIM = 0.3
holes = [p['r'] for p in PITS]
# suelo: rejilla con huecos de las piscinas
xs = sorted(set([-HW, HW] + [v for p in PITS for v in p['r'][:2]] + [x * 0.5 for x in range(-19, 20)]))
ys = sorted(set([-HD, HD] + [v for p in PITS for v in p['r'][2:]] + [y * 0.5 for y in range(-15, 16)]))
xs = [x for x in xs if -HW <= x <= HW]; ys = [y for y in ys if -HD <= y <= HD]
for i in range(len(xs) - 1):
    for j in range(len(ys) - 1):
        cx, cy = (xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2
        if any(h[0] < cx < h[1] and h[2] < cy < h[3] for h in holes): continue
        rect_z(0.0, xs[i], xs[i + 1], ys[j], ys[j + 1], M_FLOOR, 1)
for p in PITS:
    x0, x1, y0, y1 = p['r']; fz = p['floor']; rz = p['rim']; side, a, b = p['open']; c = p['col']
    rect_z(fz, x0 + RIM, x1 - RIM, y0 + RIM, y1 - RIM, M_FLOOR, 1)
    # paredes interiores (vinilo) desde el fondo hasta el borde
    rect_x(y0 + RIM, x0 + RIM, x1 - RIM, fz, rz, c, 1); rect_x(y1 - RIM, x0 + RIM, x1 - RIM, fz, rz, c, -1)
    rect_y(x0 + RIM, y0 + RIM, y1 - RIM, fz, rz, c, 1); rect_y(x1 - RIM, y0 + RIM, y1 - RIM, fz, rz, c, -1)
    # borde acolchado (4 tramos, con abertura)
    segs = [('s', x0, x1, y0, y0 + RIM), ('n', x0, x1, y1 - RIM, y1), ('w', x0, x0 + RIM, y0 + RIM, y1 - RIM), ('e', x1 - RIM, x1, y0 + RIM, y1 - RIM)]
    for k, (sd, a0, a1, b0, b1) in enumerate(segs):
        cc = PADS[(PADS.index(c) + k) % 4]
        if sd == side:
            if sd in 'sn':
                box(a0, a, b0, b1, -0.0, rz, cc, 0.04, skip=('b',)); box(b, a1, b0, b1, -0.0, rz, cc, 0.04, skip=('b',))
                box(a, b, b0, b1, 0.0, 0.08, M_YEL, 0.03, skip=('b',))       # umbral bajo (v7.1.0: 8 cm, se sale sin saltar) (sujeta las bolas)
                # escalones dentro de la piscina
                yy = b1 if sd == 's' else b0; dirn = 1 if sd == 's' else -1
                for s in range(2):
                    zt = fz + (fz * -1) * (2 - s) / 3.0
                    ya, yb = sorted((yy + dirn * 0.4 * s, yy + dirn * 0.4 * (s + 1)))
                    box(a, b, ya, yb, fz, zt, M_GRN, 0.03, skip=('b',))
            else:
                box(a0, a1, b0, a, -0.0, rz, cc, 0.04, skip=('b',)); box(a0, a1, b, b1, -0.0, rz, cc, 0.04, skip=('b',))
                box(a0, a1, a, b, 0.0, 0.08, M_YEL, 0.03, skip=('b',))
                xx = a0 if sd == 'e' else a1; dirn = -1 if sd == 'e' else 1
                for s in range(2):
                    zt = fz + (fz * -1) * (2 - s) / 3.0
                    xa, xb = sorted((xx + dirn * 0.4 * s, xx + dirn * 0.4 * (s + 1)))
                    box(xa, xb, a, b, fz, zt, M_GRN, 0.03, skip=('b',))
        else:
            box(a0, a1, b0, b1, -0.0, rz, cc, 0.04, skip=('b',))
    # superficie de bolas (solo horneado / relleno lejano): un poco por debajo de la capa de bolas instanciadas
    rect_z(p['ball'] - 0.03, x0 + RIM, x1 - RIM, y0 + RIM, y1 - RIM, M_BALL, 1)

# ---------------- estructura de juego (2 alturas) ----------------
PX = [2.0, 4.4, 6.8, 9.2]; PY = [0.6, 3.6, 6.6]; PTOP = 3.9; PS = 0.24
for i, x in enumerate(PX):
    for j, y in enumerate(PY):
        c = PADS[(i + j) % 4]
        box(x - PS / 2, x + PS / 2, y - PS / 2, y + PS / 2, 0, PTOP, c, 0.05, skip=('b',))
        box(x - 0.17, x + 0.17, y - 0.17, y + 0.17, PTOP, PTOP + 0.16, M_YEL if c != M_YEL else M_RED, 0.06, skip=('b',))   # remate
# vigas del techo de la estructura
for j, y in enumerate(PY):
    box(PX[0], PX[-1], y - 0.09, y + 0.09, PTOP - 0.18, PTOP, PADS[(j + 1) % 4], 0.04)
for i, x in enumerate(PX):
    box(x - 0.09, x + 0.09, PY[0], PY[-1], PTOP - 0.36, PTOP - 0.18, PADS[(i + 2) % 4], 0.04)
D1 = 1.3; D2 = 2.6; DT = 0.16
box(PX[0], PX[2], PY[1], PY[2], D1 - DT, D1, M_BLU, 0.04)          # planta 1 (oeste, mitad norte)
box(PX[2], PX[3], PY[0], PY[2], D2 - DT, D2, M_RED, 0.04)          # planta 2 (este)
# escalera acolchada al nivel 1: x 2.0-3.2, de y 0.6 a 3.6
NST = 6
for s in range(NST):
    z1 = D1 * (s + 1) / (NST + 0.0)
    y0 = PY[0] + (PY[1] - PY[0]) * s / NST
    box(PX[0] + 0.02, PX[0] + 1.2, y0, PY[1] if s == NST - 1 else y0 + (PY[1] - PY[0]) / NST, 0, z1, PADS[s % 4], 0.03, skip=('b',))
# escalera 1 -> 2 sobre la planta 1: y 3.6-4.8, de x 4.4 a 6.8
for s in range(NST):
    z1 = D1 + (D2 - D1) * (s + 1) / NST
    x0 = PX[1] + (PX[2] - PX[1]) * s / NST
    box(x0, x0 + (PX[2] - PX[1]) / NST, PY[1] + 0.02, PY[1] + 1.2, D1, z1, PADS[(s + 1) % 4], 0.03, skip=('b',))
# redes de seguridad (planos con alfa), barandillas acolchadas
def net_x(y, x0, x1, z0, z1):
    rect_x(y, x0, x1, z0, z1, M_NET, 1); rect_x(y + 0.002, x0, x1, z0, z1, M_NET, -1)
def net_y(x, y0, y1, z0, z1):
    rect_y(x, y0, y1, z0, z1, M_NET, 1); rect_y(x + 0.002, y0, y1, z0, z1, M_NET, -1)
net_x(PY[1], PX[0] + 1.2, PX[1], D1, D1 + 1.15)                    # planta 1, borde sur (deja paso a la escalera)
net_x(PY[1], PX[1] + 1.25, PX[2], D1, D1 + 1.15)
net_y(PX[0], PY[1], PY[2], D1, D1 + 1.15)                          # planta 1, borde oeste (el tobogán sale por un hueco)
net_x(PY[0], PX[2], PX[3], D2, D2 + 1.15)                          # planta 2 sur
net_y(PX[2], PY[0], PY[1], D2, D2 + 1.15)                          # planta 2 oeste
net_y(PX[2], PY[1] + 1.2, PY[2], D2, D2 + 1.15)
net_x(PY[2], PX[0], PX[3], 0.0, PTOP - 0.36)                       # fondo (contra la pared norte)
net_y(PX[3], PY[0], PY[2], 0.0, PTOP - 0.36)                       # fondo (pared este)
net_x(PY[0], PX[0] + 1.2, PX[2], 0.0, D1 - DT)                     # bajo la planta 1: jaula de red (zona de gateo)
for (xa, xb, ya, yb, z) in ((PX[0], PX[2], PY[1], PY[1], D1), (PX[2], PX[3], PY[0], PY[0], D2)):
    if ya == yb: box(xa, xb, ya - 0.06, ya + 0.06, z + 1.15, z + 1.27, M_YEL, 0.04)
# tobogán abierto de planta 1 a la piscina grande: sale del borde oeste (x=2.0) hacia -x, y 4.5-5.4
def trough(x0, z0, x1, z1, yc, w, m):
    N = 16; prof = [(-w / 2, 0.32), (-w / 2, 0.0), (-w / 2 + 0.08, -0.05), (0, -0.07), (w / 2 - 0.08, -0.05), (w / 2, 0.0), (w / 2, 0.32)]
    rows = []
    for i in range(N + 1):
        t = i / N; x = x0 + (x1 - x0) * t
        z = z0 + (z1 - z0) * (t - 0.06 * math.sin(2 * math.pi * t * 2))   # ondulado
        if t > 0.85: z = z1 + (z - z1) * 0.3
        rows.append(([bm.verts.new((x, yc + a, z + b)) for (a, b) in prof], [bm.verts.new((x, yc + a * 1.03, z + b - 0.03)) for (a, b) in prof]))
    for i in range(N):
        for k in range(len(prof) - 1):
            A, A2 = rows[i]; B, B2 = rows[i + 1]
            f = bm.faces.new((A[k], B[k], B[k + 1], A[k + 1])); f.material_index = m; f.smooth = True
            f2 = bm.faces.new((A2[k + 1], B2[k + 1], B2[k], A2[k])); f2.material_index = m; f2.smooth = True
trough(PX[0], D1, 0.4 - 0.9, PITS[0]['ball'] + 0.18, 4.95, 0.9, M_PLY)
# tobogán de tubo en espiral desde la planta 2 (sur) hasta el suelo, en la zona sureste
def tube_spiral(cx, cy, r, z0, z1, turns, rad, m, a0):
    N = int(48 * turns); S = 14; rings = []
    for i in range(N + 1):
        t = i / N; a = a0 - 2 * math.pi * turns * t
        p = Vector((cx + r * math.cos(a), cy + r * math.sin(a), z0 + (z1 - z0) * t))
        tang = Vector((-math.sin(a) * -1, math.cos(a) * -1, 0)) * (2 * math.pi * turns * r) + Vector((0, 0, z1 - z0)); tang.normalize()
        nrm = Vector((math.cos(a), math.sin(a), 0)); bn = tang.cross(nrm)
        rings.append([(p + (nrm * math.cos(2 * math.pi * k / S) + bn * math.sin(2 * math.pi * k / S)) * 1.0) for k in range(S)])
    for outer in (True, False):
        rr = rad if outer else rad - 0.025
        vs = []
        for i, ring in enumerate(rings):
            t = i / N; a = a0 - 2 * math.pi * turns * t
            c = Vector((cx + r * math.cos(a), cy + r * math.sin(a), z0 + (z1 - z0) * t))
            vs.append([bm.verts.new(c + (q - c).normalized() * rr) for q in ring])
        for i in range(N):
            for k in range(S):
                k2 = (k + 1) % S
                ff = (vs[i][k], vs[i][k2], vs[i + 1][k2], vs[i + 1][k]) if outer else (vs[i + 1][k], vs[i + 1][k2], vs[i][k2], vs[i][k])
                f = bm.faces.new(ff); f.material_index = m; f.smooth = True
tube_spiral(8.0, -1.9, 1.15, D2 - 0.25, 0.45, 1.15, 0.42, M_PLB, math.radians(90))
box(6.6, 9.4, -4.4, -3.0, 0, 0.1, M_GRN, 0.03, skip=('b',))           # colchoneta de llegada
net_x(PY[0] - 0.01, PX[2] + 0.4, PX[3] - 0.4, D2 + 0.02, D2 + 0.9)   # (la entrada al tubo, cerrada con red: «fuera de servicio»)

# ---------------- pilares acolchados de la sala ----------------
for (x, y) in ((-3.0, -3.4), (3.6, -3.4)):
    cyl(x, y, 0.32, 0, H, M_WALL, 20, caps=False)
    for k in range(4):
        cyl(x, y, 0.36, 0.3 * k, 0.3 * (k + 1), PADS[k % 4], 20, caps=False)
    for k in range(4, 6):
        cyl(x, y, 0.36, 0.3 * k, 0.3 * (k + 1) - 0.02, PADS[(k + 2) % 4], 20, caps=False)
    cyl(x, y, 0.37, 1.78, 1.84, M_METAL, 20, caps=True)
# ---------------- bloques de goma espuma (sueltos) ----------------
rnd = random.Random(11)
for k in range(16):
    for tries in range(40):
        x = rnd.uniform(-9.0, 9.0); y = rnd.uniform(-7.4, -0.8)
        if (-9.2 < x < -5.0 and -7.2 < y < -3.6) or (6.2 < x < 9.6 and -4.6 < y < 0.4) or abs(x - DOORS['S']) < 1.6 and y < -6 or abs(x + 3.0) < 1 and abs(y + 3.4) < 1 or abs(x - 3.6) < 1 and abs(y + 3.4) < 1: continue
        if (x < -8.6 or x > 8.6) and abs(y - DOORS['W']) < 1.4: continue
        if (-5.4 < x < 1.2 and y < -5.2) or (5.0 < x and y < -4.3): continue
        break
    s = rnd.choice([0.4, 0.5, 0.6]); hgt = rnd.choice([0.3, 0.4, 0.6]); c = rnd.choice(PADS)
    kind = rnd.random()
    if kind < 0.6: box(x - s / 2, x + s / 2, y - s / 2, y + s / 2, 0, hgt, c, 0.05, skip=('b',))
    else: cyl(x, y, s * 0.45, 0, hgt * 1.3, c, 18)

# ---------------- v7.1.0: atrezo con atlas (carteles, sala de fiestas, mostrador de meriendas) ----------------
def aquad(o, u, v, reg, n):
    o = Vector(o); u = Vector(u); v = Vector(v); pts = [o, o + u, o + u + v, o + v]
    if u.cross(v).dot(Vector(n)) < 0: pts = pts[::-1]
    quad([tuple(p) for p in pts], M_ATLAS); AREC.append(('q', o, u, v, ATL[reg]))
def afill(x0, x1, y0, y1, z0, z1, reg):
    AREC.append(('f', Vector((x0 - 0.002, y0 - 0.002, z0 - 0.002)), Vector((x1 + 0.002, y1 + 0.002, z1 + 0.002)), ATL['sw_' + reg]))
def abox(x0, x1, y0, y1, z0, z1, reg, bev=0.0):
    box(x0, x1, y0, y1, z0, z1, M_ATLAS, bev, skip=('b',) if z0 < 0.001 else ()); afill(x0, x1, y0, y1, z0, z1, reg)
def acyl(cx, cy, r, z0, z1, reg, seg=14, rtop=None):
    cyl(cx, cy, r, z0, z1, M_ATLAS, seg, True, rtop); rr = max(r, rtop or 0); afill(cx - rr, cx + rr, cy - rr, cy + rr, z0, z1, reg)
def asphere(cx, cy, cz, rx, rz, reg, seg=10):
    rings = []
    for j in range(1, seg):
        th = math.pi * j / seg
        rings.append([bm.verts.new((cx + rx * math.sin(th) * math.cos(2 * math.pi * i / seg), cy + rx * math.sin(th) * math.sin(2 * math.pi * i / seg), cz + rz * math.cos(th))) for i in range(seg)])
    tp = bm.verts.new((cx, cy, cz + rz)); bt = bm.verts.new((cx, cy, cz - rz))
    for i in range(seg):
        k = (i + 1) % seg
        f = bm.faces.new((tp, rings[0][i], rings[0][k])); f.material_index = M_ATLAS; f.smooth = True
        f = bm.faces.new((bt, rings[-1][k], rings[-1][i])); f.material_index = M_ATLAS; f.smooth = True
        for j in range(len(rings) - 1):
            f = bm.faces.new((rings[j][i], rings[j + 1][i], rings[j + 1][k], rings[j][k])); f.material_index = M_ATLAS; f.smooth = True
    afill(cx - rx, cx + rx, cy - rx, cy + rx, cz - rz, cz + rz, reg)

# rótulo de la sala (pared norte, sobre la piscina grande) y normas (pared oeste)
aquad((-5.5, HD - 0.006, 3.2), (4.0, 0, 0), (0, 0, 1.0), 'sign', (0, -1, 0))
aquad((-HW + 0.006, 1.0, 1.5), (0, 1.2, 0), (0, 0, 1.6), 'rules', (1, 0, 0))

# sala de fiestas (esquina sureste): tabiques de 2,4 m con puerta al oeste; su fluorescente está fundido
PR = (5.5, HW, -HD, -4.8); PH = 2.4
box(5.5, 5.62, -HD, -6.9, 0, PH, M_WALL, 0.0, skip=('b',)); box(5.5, 5.62, -5.9, -4.8, 0, PH, M_WALL, 0.0, skip=('b',))
box(5.5, 5.62, -6.9, -5.9, 2.1, PH, M_WALL, 0.0)
box(5.5, HW, -4.92, -4.8, 0, PH, M_WALL, 0.0, skip=('b',))
aquad((5.494, -5.15, 1.25), (0, -0.6, 0), (0, 0, 0.8), 'poster', (-1, 0, 0))
aquad((7.0, -4.926, 1.0), (0.4, 0, 0), (0, 0, 0.8), 'drawing', (0, -1, 0))
# mesa infantil con mantel, sillitas (una volcada), platos, tarta, gorros y globos
box(6.5, 9.1, -6.85, -5.95, 0.54, 0.58, M_PLY, 0.01)
aquad((6.42, -6.93, 0.582), (2.76, 0, 0), (0, 1.06, 0), 'cloth', (0, 0, 1))
aquad((6.42, -6.93, 0.582), (2.76, 0, 0), (0, 0, -0.16), 'cloth', (0, -1, 0))
aquad((6.42, -5.87, 0.582), (2.76, 0, 0), (0, 0, -0.16), 'cloth', (0, 1, 0))
for (lx, ly) in ((6.6, -6.75), (9.0, -6.75), (6.6, -6.05), (9.0, -6.05)): cyl(lx, ly, 0.025, 0, 0.54, M_METAL, 8, False)
CH = [(6.9, -7.25, 0), (7.8, -7.25, 0), (8.7, -7.25, 0), (6.9, -5.55, 1), (8.7, -5.55, 1)]
for k, (cx, cy, side) in enumerate(CH):
    m = (M_PLY, M_PLB)[k % 2]
    box(cx - 0.16, cx + 0.16, cy - 0.16, cy + 0.16, 0.28, 0.32, m, 0.02)
    by = cy - 0.15 if side == 0 else cy + 0.12
    box(cx - 0.16, cx + 0.16, by, by + 0.03, 0.32, 0.62, m, 0.01)
    for (dx, dy) in ((-0.13, -0.13), (0.13, -0.13), (-0.13, 0.13), (0.13, 0.13)): cyl(cx + dx, cy + dy, 0.015, 0, 0.28, M_METAL, 6, False)
box(7.6, 8.0, -5.2, -5.17, 0, 0.32, M_PLB, 0.01); box(7.6, 8.0, -5.5, -5.18, 0.0, 0.04, M_PLB, 0.01)   # sillita volcada
for k in range(6):
    px = 6.9 + k * 0.42; py = -6.7 if k % 2 == 0 else -6.1
    acyl(px, py, 0.1, 0.583, 0.592, 'plate', 14)
acyl(7.8, -6.4, 0.16, 0.583, 0.70, 'frosting', 18); acyl(7.8, -6.4, 0.161, 0.583, 0.62, 'sponge', 18)
for k in range(5): acyl(7.8 + 0.09 * math.cos(k * 1.26), -6.4 + 0.09 * math.sin(k * 1.26), 0.006, 0.70, 0.76, 'hatB', 5)
for (hx, hy, hr) in ((7.1, -6.15, 'hatA'), (8.5, -6.65, 'hatB'), (9.4, -7.5, 'hatA')):
    hz = 0.592 if hr != 'hatA' or hx < 9 else 0.0
    acyl(hx, hy, 0.065, hz, hz + 0.16, hr, 10, 0.004)
for (bx, by, c) in ((9.3, -5.3, 'balloonR'), (6.2, -7.5, 'balloonB'), (8.9, -7.6, 'balloonY')):
    asphere(bx, by, 0.06, 0.15, 0.06, c)                                      # globos desinflados en el suelo
for (bx, by, bz, c) in ((9.55, -7.55, 1.9, 'balloonG'), (9.3, -7.6, 1.6, 'balloonR')):
    asphere(bx, by, bz, 0.13, 0.16, c); box(bx - 0.004, bx + 0.004, by - 0.004, by + 0.004, 0.0, bz - 0.16, M_METAL)
abox(9.2, 9.7, -6.6, -6.1, 0, 0.32, 'cardboard'); abox(9.25, 9.6, -6.55, -6.2, 0.32, 0.52, 'hatB')   # regalos
# banderines "FELIZ CUMPLE" de lado a lado
for (xs_, word, z_) in ((7.0, [0, 1, 2, 3, 4], 2.05), (8.6, [6, 7, 8, 9, 10, 11], 2.15)):
    box(xs_ - 0.002, xs_ + 0.002, -HD, -4.92, z_ + 0.298, z_ + 0.302, M_METAL)
    seq = [13] + word + [14] if len(word) == 5 else word
    y0_ = -4.92 - (2.976 - len(seq) * 0.33) / 2 - 0.02
    for k, idx in enumerate(seq):
        aquad((xs_, y0_ - k * 0.33, z_), (0, -0.3, 0), (0, 0, 0.3), 'pen%d' % idx, (-1, 0, 0))
# mostrador de meriendas (pared sur, oeste de la puerta)
box(-4.8, 0.6, -6.95, -6.35, 0, 1.0, M_RED, 0.03, skip=('b',))
box(-4.9, 0.7, -7.0, -6.25, 1.0, 1.05, M_METAL, 0.01)
box(-4.6, 0.4, -HD, -7.55, 1.25, 1.29, M_METAL, 0.0); box(-4.6, 0.4, -HD, -7.55, 1.75, 1.79, M_METAL, 0.0)
aquad((-0.4, -HD + 0.006, 2.0), (-4.0, 0, 0), (0, 0, 2.0), 'menu', (0, 1, 0))
for k in range(9): acyl(-4.3 + k * 0.5, -7.7, 0.045, 1.29, 1.41, 'cupred', 10, 0.055)
for k in range(6): acyl(-4.1 + k * 0.7, -7.7, 0.05, 1.79, 1.95, 'napkin', 10)
box(-4.4, -3.8, -6.85, -6.45, 1.05, 1.25, M_METAL, 0.01); abox(-4.38, -3.82, -6.83, -6.47, 1.25, 1.72, 'popcorn'); aquad((-3.83, -6.465, 1.26), (-0.54, 0, 0), (0, 0, 0.45), 'sw_popcorn', (0, 1, 0)); aquad((-4.37, -6.835, 1.26), (0.54, 0, 0), (0, 0, 0.45), 'sw_popcorn', (0, -1, 0)); box(-4.42, -3.78, -6.87, -6.43, 1.72, 1.84, M_RED, 0.02)
abox(-1.25, -0.75, -6.85, -6.45, 1.05, 1.22, 'register', 0.01); abox(-1.2, -0.8, -6.85, -6.6, 1.22, 1.32, 'register', 0.01)
abox(-2.6, -2.45, -6.6, -6.5, 1.05, 1.2, 'steel')
for k, sx in enumerate((-3.6, -2.2, -0.8)):
    cyl(sx, -5.95, 0.035, 0, 0.72, M_METAL, 8, False); cyl(sx, -5.95, 0.2, 0, 0.03, M_METAL, 14, True); acyl(sx, -5.95, 0.19, 0.72, 0.8, 'stool', 16)
# zona infantil: vallado bajo acolchado alrededor de la piscina pequeña
box(-9.6, -4.6, -7.6, -7.5, 0, 0.0, M_GRN, 0.0)

# ---------------- fluorescentes ----------------
LIGHTS = []
for i, x in enumerate((-6.0, 0.0, 6.0)):
    for j, y in enumerate((-5.0, -1.0, 3.0, 6.5)):
        dead = (i, j) in ((2, 0), (0, 3))
        box(x - 0.62, x + 0.62, y - 0.31, y + 0.31, H - 0.06, H - 0.002, M_METAL, 0.0)
        rect_z(H - 0.065, x - 0.58, x + 0.58, y - 0.27, y + 0.27, M_EMIT if not dead else M_METAL, -1)
        LIGHTS.append((x, y, dead))

me = bpy.data.meshes.new('BallpitHall')
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0004)
bm.normal_update()
bm.to_mesh(me); bm.free()
ob = bpy.data.objects.new('BallpitHall', me); bpy.context.scene.collection.objects.link(ob)
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
try: bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
except Exception as e: print('smooth', e)

# UV0: mapeo de caja en metros; paredes: u a lo largo, v = altura/4,8 (el zócalo de la textura cae en 0-1,2 m)
def atlas_rec(p):
    cen = p.center; n = p.normal
    for r in AREC:
        if r[0] != 'q': continue
        o, u, v = r[1], r[2], r[3]; nn = u.cross(v).normalized()
        if abs((cen - o).dot(nn)) > 0.003 or abs(n.dot(nn)) < 0.99: continue
        a = (cen - o).dot(u) / u.length_squared; b = (cen - o).dot(v) / v.length_squared
        if -0.01 <= a <= 1.01 and -0.01 <= b <= 1.01: return r
    for r in AREC:
        if r[0] == 'f' and all(r[1][i] <= cen[i] <= r[2][i] for i in range(3)): return r
    return None
def atlas_uv(r, c):
    if r is None: return (0.99, 0.01)
    u0, v0, u1, v1 = r[-1]
    if r[0] == 'f': return ((u0 + u1) / 2, (v0 + v1) / 2)
    o, u, v = r[1], r[2], r[3]
    a = min(1, max(0, (c - o).dot(u) / u.length_squared)); b = min(1, max(0, (c - o).dot(v) / v.length_squared))
    return (u0 + (u1 - u0) * a, v0 + (v1 - v0) * b)
uv0 = me.uv_layers.new(name='UV0'); UNMATCHED = 0
for p in me.polygons:
    n = p.normal; ax = max(range(3), key=lambda i: abs(n[i])); ts = TILE[p.material_index]
    if p.material_index == M_ATLAS:
        rr = atlas_rec(p)
        if rr is None: UNMATCHED += 1
        for li in p.loop_indices: uv0.data[li].uv = atlas_uv(rr, me.vertices[me.loops[li].vertex_index].co)
        continue
    for li in p.loop_indices:
        c = me.vertices[me.loops[li].vertex_index].co
        a, b = {0: (c.y, c.z), 1: (c.x, c.z), 2: (c.x, c.y)}[ax]
        if p.material_index == M_WALL and ax != 2: uv0.data[li].uv = (a / ts, c.z / 4.8)
        elif p.material_index == M_PLB or p.material_index == M_PLY: uv0.data[li].uv = ((c.x + c.y) / ts, c.z / ts)
        else: uv0.data[li].uv = (a / ts, b / ts)
# UV1: lightmap
uv1 = me.uv_layers.new(name='UV1'); me.uv_layers.active = uv1
for p in me.polygons: p.select = p.material_index != M_EMIT
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_mode(type='FACE')
bpy.ops.uv.smart_project(angle_limit=math.radians(50), island_margin=0.0, area_weight=0.0, scale_to_bounds=False)
bpy.ops.uv.pack_islands(rotate=True, margin=4.0 / RES * 1.5)
bpy.ops.object.mode_set(mode='OBJECT')
for p in me.polygons:
    if p.material_index == M_EMIT:
        for li in p.loop_indices: uv1.data[li].uv = (0.0005, 0.0005)

# ---------------- materiales con texturas reales (rebote de color) ----------------
def img(path, non_color=False):
    im = bpy.data.images.load(path, check_existing=True)
    if non_color: im.colorspace_settings.name = 'Non-Color'
    return im
def mat(name, tex, tint=(1, 1, 1), rough=0.6, emit=0.0, alpha=False, normal=None):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'UV0'
    if tex:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = img(tex); nt.links.new(uvn.outputs[0], t.inputs[0])
        mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs['Factor'].default_value = 1.0
        nt.links.new(t.outputs['Color'], mix.inputs[6]); mix.inputs[7].default_value = (*tint, 1)
        nt.links.new(mix.outputs[2], b.inputs['Base Color'])
        if alpha: nt.links.new(t.outputs['Alpha'], b.inputs['Alpha'])
    else: b.inputs['Base Color'].default_value = (*tint, 1)
    if normal:
        tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img(normal, True); nt.links.new(uvn.outputs[0], tn.inputs[0])
        nm = nt.nodes.new('ShaderNodeNormalMap'); nm.uv_map = 'UV0'; nm.inputs['Strength'].default_value = 0.8
        nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs[0], b.inputs['Normal'])
    b.inputs['Roughness'].default_value = rough
    if emit: b.inputs['Emission Color'].default_value = (0.95, 0.97, 1.0, 1); b.inputs['Emission Strength'].default_value = emit
    if alpha: m.blend_method = 'HASHED' if hasattr(m, 'blend_method') else None
    return m
# tintes del vinilo (lineal); el mismo valor se usa en Unity (BallpitSetPiece.PadTint)
PAD_TINT = {M_RED: (0.62, 0.16, 0.13), M_YEL: (0.80, 0.58, 0.16), M_BLU: (0.15, 0.30, 0.58), M_GRN: (0.20, 0.44, 0.22)}
mats = [None] * NMAT
mats[M_WALL] = mat('PlayWall', TEX + 'PlayWall_a.jpg', rough=0.85, normal=TEX + 'PlayWall_n.png')
mats[M_FLOOR] = mat('PlayFloor', TEX + 'PlayFloor_a.jpg', rough=0.75, normal=TEX + 'PlayFloor_n.png')
mats[M_CEIL] = mat('Ceil', RTEX + 'CeilingBeige_a.jpg', tint=(0.92, 0.92, 0.95), rough=0.9)
for k in PADS: mats[k] = mat('Pad%d' % k, TEX + 'PadVinyl_a.jpg', tint=PAD_TINT[k], rough=0.45, normal=TEX + 'PadVinyl_n.png')
mats[M_PLY] = mat('PlasticY', TEX + 'PlayPlastic_a.jpg', tint=(0.85, 0.62, 0.14), rough=0.35)
mats[M_PLB] = mat('PlasticB', TEX + 'PlayPlastic_a.jpg', tint=(0.20, 0.36, 0.66), rough=0.35)
mats[M_NET] = mat('Net', TEX + 'PlayNet_a.png', rough=0.8, alpha=True)
mats[M_EMIT] = mat('Emit', None, tint=(0.95, 0.97, 1.0), emit=6.0)
mats[M_BALL] = mat('Balls', TEX + 'BallSurface_a.jpg', rough=0.3)
mats[M_METAL] = mat('Metal', None, tint=(0.55, 0.56, 0.57), rough=0.4)
mats[M_ATLAS] = mat('Atlas', TEX + 'PartyAtlas_a.png', rough=0.6, alpha=True)
for m in mats: me.materials.append(m)

# ---------------- luces: un área por fluorescente vivo (4000 K, algo verdoso) ----------------
for (x, y, dead) in LIGHTS:
    if dead: continue
    L = bpy.data.lights.new('Tube', 'AREA'); L.shape = 'RECTANGLE'; L.size = 1.16; L.size_y = 0.54
    L.energy = float(os.environ.get('LAMPW', '95')); L.color = (1.0, 0.97, 0.90)
    o = bpy.data.objects.new('Tube', L); o.location = (x, y, H - 0.08); bpy.context.scene.collection.objects.link(o)
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; cy = sc.cycles; cy.device = 'CPU'; cy.samples = SPP
cy.max_bounces = 6; cy.diffuse_bounces = 4; cy.transparent_max_bounces = 8; cy.use_denoising = False
sc.world = bpy.data.worlds.new('W'); sc.world.use_nodes = True; sc.world.node_tree.nodes['Background'].inputs[1].default_value = 0.0

# ---------------- bolas reales solo para la imagen de referencia ----------------
def add_balls():
    bm2 = bmesh.new(); bmesh.ops.create_icosphere(bm2, subdivisions=2, radius=0.037)
    sm = bpy.data.meshes.new('Ball'); bm2.to_mesh(sm); bm2.free()
    for pg in sm.polygons: pg.use_smooth = True
    cols = [(0.62, 0.08, 0.06), (0.82, 0.62, 0.06), (0.06, 0.22, 0.60), (0.08, 0.42, 0.14), (0.70, 0.70, 0.66)]
    rr = random.Random(3)
    for ci, c in enumerate(cols):
        bm3 = bmesh.new()
        for p in PITS:
            x0, x1, y0, y1 = p['r']; x0 += RIM + 0.04; x1 -= RIM + 0.04; y0 += RIM + 0.04; y1 -= RIM + 0.04
            for layer in range(3):
                z = p['ball'] - 0.065 * layer
                nx = int((x1 - x0) / 0.074); ny = int((y1 - y0) / 0.064)
                for i in range(nx):
                    for j in range(ny):
                        if rr.randrange(5) != ci: continue
                        bm3.verts.new((x0 + (i + 0.5 * (j % 2) + rr.uniform(-0.12, 0.12)) * 0.074, y0 + (j + rr.uniform(-0.1, 0.1)) * 0.064, z + rr.uniform(-0.018, 0.018)))
        pm = bpy.data.meshes.new('BallPts%d' % ci); bm3.to_mesh(pm); bm3.free()
        po = bpy.data.objects.new('BallPts%d' % ci, pm); sc.collection.objects.link(po)
        bo = bpy.data.objects.new('Ball%d' % ci, sm.copy()); sc.collection.objects.link(bo); bo.parent = po
        bm_ = bpy.data.materials.new('B%d' % ci); bm_.use_nodes = True; bb = bm_.node_tree.nodes['Principled BSDF']
        bb.inputs['Base Color'].default_value = (*c, 1); bb.inputs['Roughness'].default_value = 0.28
        try: bb.inputs['Coat Weight'].default_value = 0.3
        except Exception: pass
        bo.data.materials.append(bm_)
        po.instance_type = 'VERTS'
    print('balls ok', flush=True)

if MODE == 'render':
    add_balls()
    mats[M_BALL].node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.02, 0.02, 0.02, 1)
    cam = bpy.data.cameras.new('Cam'); cam.lens = float(os.environ.get('LENS', '18')); co = bpy.data.objects.new('Cam', cam); sc.collection.objects.link(co); sc.camera = co
    shots = [((2.8, -6.6, 1.62), (-2.5, 4.0, 0.6), 'a'), ((-8.6, -1.2, 1.62), (2.0, 4.2, 1.2), 'b'), ((8.6, -6.9, 1.62), (2.0, 3.0, 1.6), 'c')]
    shots += [((4.6, -6.4, 1.5), (8.5, -6.4, 0.7), 'p'), ((-1.0, -3.4, 1.6), (-2.2, -7.6, 1.5), 'q'), ((9.5, -5.2, 1.5), (6.0, -7.6, 0.5), 'r')]
    if os.environ.get('SHOTS'): shots = [x for x in shots if x[2] in os.environ['SHOTS']]
    print('atlas unmatched', UNMATCHED, flush=True)
    sc.render.resolution_x = 1600; sc.render.resolution_y = 900; cy.samples = SPP; cy.use_denoising = True
    try: cy.denoiser = 'OPENIMAGEDENOISE'
    except Exception: pass
    sc.view_settings.view_transform = 'AgX'; sc.view_settings.look = 'None'; sc.view_settings.exposure = float(os.environ.get('EXPO', '0.0'))
    for (p, tgt, nm) in shots:
        co.location = p; d = Vector(tgt) - Vector(p); co.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + 'ref_%s.png' % nm; sc.render.image_settings.file_format = 'PNG'
        bpy.ops.render.render(write_still=True); print('render', nm, flush=True)
    sys.exit(0)

# ---------------- horneado de irradiancia ----------------
img_lm = bpy.data.images.new('LM', RES, RES, float_buffer=True, alpha=False)
for m in me.materials:
    nt = m.node_tree; t = nt.nodes.new('ShaderNodeTexImage'); t.image = img_lm; nt.nodes.active = t
b = sc.render.bake; b.use_pass_direct = True; b.use_pass_indirect = True; b.use_pass_color = False; b.margin = 6; b.margin_type = 'EXTEND'; b.target = 'IMAGE_TEXTURES'
bpy.ops.object.bake(type='DIFFUSE', uv_layer='UV1')
px = np.array(img_lm.pixels[:], dtype=np.float32).reshape(RES, RES, 4)
np.save(OUT + 'lm_raw.npy', px[:, :, :3])
print('bake ok', flush=True)
# sondas (irradiancia direccional para jugador/monstruos), 6 puntos a 1,2 m
probes = []
cam = bpy.data.cameras.new('C'); cam.angle = math.radians(90); co = bpy.data.objects.new('C', cam); sc.collection.objects.link(co); sc.camera = co
sc.render.resolution_x = sc.render.resolution_y = 8; sc.render.resolution_percentage = 100; cy.samples = 64
sc.render.image_settings.file_format = 'OPEN_EXR'
faces = [(Vector((1, 0, 0)), Vector((0, 0, 1))), (Vector((-1, 0, 0)), Vector((0, 0, 1))), (Vector((0, 1, 0)), Vector((0, 0, 1))), (Vector((0, -1, 0)), Vector((0, 0, 1))), (Vector((0, 0, 1)), Vector((0, 1, 0))), (Vector((0, 0, -1)), Vector((0, 1, 0)))]
for (pxp, pyp) in ((-7.5, -5.5), (0.0, -5.5), (7.5, -5.5), (-7.5, 0.0), (1.2, 2.0), (5.0, -1.0), (-3.0, 3.8), (5.5, 5.0)):
    samples = []
    for fwd, up in faces:
        co.location = (pxp, pyp, 1.2); co.rotation_euler = fwd.to_track_quat('-Z', 'Y' if up.y else 'Z').to_euler()
        sc.render.filepath = OUT + 'probe_tmp.exr'; bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(OUT + 'probe_tmp.exr'); a = np.array(im.pixels[:]).reshape(8, 8, 4); bpy.data.images.remove(im)
        R = co.matrix_world.to_3x3()
        for j in range(8):
            for i in range(8):
                u = (i + 0.5) / 8 * 2 - 1; v = (j + 0.5) / 8 * 2 - 1
                d = R @ Vector((u, v, -1)); dw = 4 / 64 / (d.length ** 3); d.normalize()
                c = a[j, i, :3] * dw / math.pi
                samples.append([d.x, d.z, d.y, float(c[0]), float(c[1]), float(c[2])])
    probes.append({'p': [pxp, 1.2, pyp], 's': samples})
json.dump({'probes': probes, 'pits': [dict(r=p['r'], floor=p['floor'], ball=p['ball'], rim=p['rim'], open=list(p['open'])) for p in PITS], 'rim': RIM,
           'lights': [[x, y, d] for (x, y, d) in LIGHTS], 'H': H, 'doors': DOORS, 'party': list(PR), 'party_h': PH, 'atlas_unmatched': UNMATCHED}, open(OUT + 'hall.json', 'w'))
# exportar malla (Unity: intercambia y/z -> invierte el giro); normales por esquina (suavizado por ángulo)
me.calc_loop_triangles()
try: cn = me.corner_normals; getn = lambda li: cn[li].vector
except Exception:
    me.calc_normals_split(); getn = lambda li: me.loops[li].normal
uv0 = me.uv_layers['UV0']; uv1 = me.uv_layers['UV1']
verts = []; uvA = []; uvB = []; nrm = []; subs = {k: [] for k in range(NMAT)}; index = {}
for tri in me.loop_triangles:
    m = me.polygons[tri.polygon_index].material_index; idx = []
    for li in tri.loops:
        vco = me.vertices[me.loops[li].vertex_index].co; n = getn(li)
        key = (tuple(round(c, 5) for c in vco), tuple(round(c, 3) for c in n), tuple(round(c, 5) for c in uv0.data[li].uv), tuple(round(c, 6) for c in uv1.data[li].uv))
        if key not in index:
            index[key] = len(verts); verts.append((vco.x, vco.z, vco.y)); nrm.append((n.x, n.z, n.y)); uvA.append(tuple(uv0.data[li].uv)); uvB.append(tuple(uv1.data[li].uv))
        idx.append(index[key])
    subs[m] += [idx[0], idx[2], idx[1]]
with open(OUT + 'BallpitHall.bytes', 'wb') as f:
    f.write(struct.pack('<ii', len(verts), NMAT))
    for i in range(len(verts)): f.write(struct.pack('<10f', *verts[i], *nrm[i], *uvA[i], *uvB[i]))
    for k in range(NMAT): f.write(struct.pack('<i', len(subs[k]))); f.write(struct.pack('<%di' % len(subs[k]), *subs[k]))
bpy.ops.wm.save_as_mainfile(filepath=OUT + 'ballpit_hall.blend')
print('DONE verts', len(verts), 'tris', sum(len(s) for s in subs.values()) // 3, flush=True)
