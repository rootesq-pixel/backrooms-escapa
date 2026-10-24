# v7.5.0: muebles del hotel (nivel 3) modelados por script en Blender 4.2: mesas de banquete redondas (pie central de columna
# torneada con cuatro patas curvas), mesas cuadradas de comedor (patas torneadas, faldón), manteles caídos con pliegues (largo,
# torcido, deslizado), sillas de comedor (respaldo curvo de madera con tablilla, asiento tapizado) y de banquete (tipo Chiavari
# de latón con cojín rojo), columna clásica (plinto, basa con toros, fuste estriado con éntasis, capitel con equino y ábaco) y
# mostrador de recepción con paneles en relieve, zócalo, cornisa y encimera de mármol con canto moldurado.
# Todo se escribe en coordenadas de Unity (x, y arriba, z adelante) y se pasa a Blender como (-x, z, y); en el juego se usan las
# mallas tal cual (HotelFurniture.cs), relativas a la raíz del FBX. Nombre de cada objeto: <Modelo>__<Material>.
# UV en metros (1 repetición = 1 m). Uso: blender -b -P build_hotel_furn.py -- <salida.fbx>
import bpy, bmesh, math, sys, random
from mathutils import Vector
OUT = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else '/workspace/v750/furn/HotelFurniture750.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True)
TAU = math.pi * 2

class M:   # malla en construcción (coordenadas de Unity)
    def __init__(s): s.v = []; s.f = []; s.uv = []   # uv por cara (lista de tuplas) o None (caja)
    def add(s, verts, faces, uvs=None):
        o = len(s.v); s.v += verts
        for i, fc in enumerate(faces): s.f.append([o + k for k in fc]); s.uv.append(uvs[i] if uvs else None)
    def ext(s, o): s.v += o.v; off = len(s.v) - len(o.v); [s.f.append([k + off for k in fc]) for fc in o.f]; s.uv += o.uv

PARTS = {}
def part(model, mat): return PARTS.setdefault((model, mat), M())

def box(m, c, sz, rot_y=0.0, rot_x=0.0, rot_z=0.0):
    hx, hy, hz = sz[0] / 2, sz[1] / 2, sz[2] / 2
    P = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz), (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
    out = []
    for p in P:
        v = Vector(p)
        if rot_z: v.rotate(__import__('mathutils').Euler((0, 0, rot_z)))
        if rot_x: v.rotate(__import__('mathutils').Euler((rot_x, 0, 0)))
        if rot_y: v.rotate(__import__('mathutils').Euler((0, rot_y, 0)))
        out.append((v.x + c[0], v.y + c[1], v.z + c[2]))
    F = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (0, 4, 7, 3)]
    m.add(out, F)

def lathe(m, prof, seg, x=0.0, z=0.0, rmod=None, cap_top=True, cap_bot=False, phase=0.0):
    # prof: [(r, y)] de abajo arriba; rmod(theta, y, r) -> r (estrías)
    o = len(m.v); n = len(prof)
    for i in range(seg):
        t = phase + TAU * i / seg; c, s_ = math.cos(t), math.sin(t)
        for (r, y) in prof:
            rr = rmod(t, y, r) if rmod else r
            m.v.append((x + rr * c, y, z + rr * s_))
    for i in range(seg):
        a, b = i, (i + 1) % seg
        u0, u1 = i / seg, (i + 1) / seg
        for j in range(n - 1):
            circ = TAU * max(prof[j][0], 0.02)
            m.f.append([o + a * n + j, o + a * n + j + 1, o + b * n + j + 1, o + b * n + j])
            m.uv.append([(u0 * circ, prof[j][1]), (u0 * circ, prof[j + 1][1]), (u1 * circ, prof[j + 1][1]), (u1 * circ, prof[j][1])])
    if cap_top and prof[-1][0] > 0.001:
        m.f.append([o + i * n + n - 1 for i in range(seg)][::-1]); m.uv.append(None)
    if cap_bot and prof[0][0] > 0.001:
        m.f.append([o + i * n for i in range(seg)]); m.uv.append(None)

def tube(m, pts, r, seg=8):
    # tubo por una polilínea (Unity), radio r
    o = len(m.v); n = len(pts)
    for k, p in enumerate(pts):
        p = Vector(p); d = (Vector(pts[min(k + 1, n - 1)]) - Vector(pts[max(k - 1, 0)])).normalized()
        a = Vector((0, 1, 0)) if abs(d.y) < 0.9 else Vector((1, 0, 0))
        u = d.cross(a).normalized(); w = d.cross(u).normalized()
        for i in range(seg):
            t = TAU * i / seg; q = p + (u * math.cos(t) + w * math.sin(t)) * (r(k / (n - 1)) if callable(r) else r)
            m.v.append((q.x, q.y, q.z))
    for k in range(n - 1):
        for i in range(seg):
            a, b = i, (i + 1) % seg
            m.f.append([o + k * seg + a, o + k * seg + b, o + (k + 1) * seg + b, o + (k + 1) * seg + a]); m.uv.append(None)
    m.f.append([o + i for i in range(seg)][::-1]); m.uv.append(None)
    m.f.append([o + (n - 1) * seg + i for i in range(seg)]); m.uv.append(None)

def grid_surface(m, fn, nu, nv, uvscale=1.0):
    # superficie paramétrica fn(u, v) -> (x, y, z) Unity, u, v en [0, 1]; UV por la distancia de la tela sin deformar
    o = len(m.v)
    for j in range(nv + 1):
        for i in range(nu + 1): m.v.append(fn(i / nu, j / nv))
    for j in range(nv):
        for i in range(nu):
            a = o + j * (nu + 1) + i
            m.f.append([a, a + 1, a + nu + 2, a + nu + 1])
            m.uv.append([(i / nu * uvscale, j / nv * uvscale), ((i + 1) / nu * uvscale, j / nv * uvscale), ((i + 1) / nu * uvscale, (j + 1) / nv * uvscale), (i / nu * uvscale, (j + 1) / nv * uvscale)])

def folds(t, seed, n=6, lo=5, hi=17):
    rnd = random.Random(seed); acc = 0.0
    for k in range(n):
        fr = rnd.randint(lo, hi); ph = rnd.random() * TAU; amp = rnd.uniform(0.4, 1.0) / (1 + k * 0.4)
        acc += amp * math.sin(fr * t + ph)
    return acc / 2.2

# ====================================================================== mesas redondas de banquete (Ø 1,55, alto 0,76)
def round_base(name):
    w = part(name, 'Wood')
    lathe(w, [(0.76, 0.722), (0.765, 0.728), (0.765, 0.752), (0.755, 0.76)], 48, cap_top=True, cap_bot=True)    # tablero con canto
    lathe(w, [(0.05, 0.04), (0.075, 0.08), (0.06, 0.16), (0.045, 0.3), (0.05, 0.42), (0.07, 0.5), (0.05, 0.56), (0.04, 0.66), (0.09, 0.70), (0.09, 0.722)], 16, cap_top=True)
    for k in range(4):   # cuatro patas curvas
        a = k * TAU / 4 + TAU / 8; d = Vector((math.cos(a), 0, math.sin(a)))
        pts = [d * (0.04 + 0.36 * t) + Vector((0, 0.11 * (1 - t) ** 1.6 + 0.02, 0)) for t in [i / 8 for i in range(9)]]
        tube(w, [tuple(p) for p in pts], lambda t: 0.032 - 0.014 * t, 8)
def round_cloth(name, seed, hem0, hem_amp, side, rim=0.765):
    c = part(name, 'TCloth'); R = 0.775
    # disco de arriba
    def top(u, v):
        r = R * v; t = TAU * u; return (r * math.cos(t), rim + 0.004 * v, r * math.sin(t))
    grid_surface(c, top, 48, 4, 1.0)
    # faldón: v 0..1 = canto .. bajo; bajo a hem(t); pliegues que crecen hacia abajo (máx Ø 0,825)
    def skirt(u, v):
        t = TAU * u; hem = hem0 + hem_amp * (0.5 + 0.5 * math.cos(t - side)) + 0.03 * folds(t, seed + 7, 3) - 0.025 * max(0.0, folds(t, seed, 7, 9, 26))
        y = rim + 0.004 - (rim + 0.004 - hem) * v
        d = (rim - y)
        roll = 0.012 * math.sin(min(v * 12, 1) * math.pi / 2)
        k = max(0.0, min(d, 0.65)) / 0.65; fo = folds(t, seed, 7, 9, 26) + 0.45 * folds(t, seed + 3, 4, 3, 7)
        r = R + roll + 0.022 * k + 0.034 * k ** 0.8 * max(-1.0, min(1.0, fo))   # pliegues hacia fuera y hacia dentro (máx ~0,84)
        return (r * math.cos(t), y, r * math.sin(t))
    grid_surface(c, skirt, 96, 10, 1.0)
for i, (hem0, amp, side) in enumerate([(0.08, 0.04, 0.0), (0.05, 0.40, 1.1)]):
    n = 'RoundTable%d' % i; round_base(n); round_cloth(n, 100 + i, hem0, amp, side)
round_base('RoundTable2')
c = part('RoundTable2', 'TCloth')   # mantel arrugado caído junto al pie (bajo, dentro de la huella)
def heap(u, v):
    t = TAU * u; r = 0.42 * v * (1 + 0.12 * folds(t, 300, 3)); h = 0.06 * (1 - v ** 1.5) * (1 + 0.35 * folds(t * 2 + v * 3, 301, 2))
    return (0.18 + r * math.cos(t), 0.012 + max(h, 0), -0.12 + r * 0.8 * math.sin(t))
grid_surface(c, heap, 32, 6, 1.0)

# ====================================================================== mesas cuadradas de comedor (0,95 x 0,95, alto 0,76)
def square_base(name):
    w = part(name, 'Wood'); H = 0.95 / 2
    box(w, (0, 0.743, 0), (0.95, 0.034, 0.95))
    box(w, (0, 0.716, 0), (0.93, 0.02, 0.93))
    for sx in (-1, 1):
        box(w, (sx * 0.405, 0.66, 0), (0.024, 0.09, 0.78)); box(w, (0, 0.66, sx * 0.405), (0.78, 0.09, 0.024))
    for sx in (-1, 1):
        for sz in (-1, 1):
            lathe(w, [(0.018, 0.0), (0.024, 0.03), (0.02, 0.1), (0.03, 0.3), (0.034, 0.42), (0.026, 0.5), (0.03, 0.56), (0.03, 0.71)], 10, x=sx * 0.40, z=sz * 0.40, cap_top=True)
def square_cloth(name, seed, shift, rot, rim=0.762):
    c = part(name, 'TCloth'); T = 0.48; L = 0.74
    ca, sa = math.cos(rot), math.sin(rot)
    def f(u, v):
        x0 = -L + 2 * L * u; z0 = -L + 2 * L * v
        x = x0 * ca - z0 * sa + shift[0]; z = x0 * sa + z0 * ca + shift[1]
        dx = max(abs(x) - T, 0); dz = max(abs(z) - T, 0); d = math.hypot(dx, dz)
        if d <= 1e-6: return (x, rim + 0.003, z)
        ex = math.copysign(min(abs(x), T), x); ez = math.copysign(min(abs(z), T), z)
        dirx, dirz = (math.copysign(dx, x) / d, math.copysign(dz, z) / d)
        t = math.atan2(z, x); drop = min(d, 0.70)
        fl = 0.012 * math.sin(min(d * 14, 1.0) * math.pi / 2) + drop * (0.035 + 0.02 * folds(t * 3, seed))
        fl = min(fl, 0.046)
        return (ex + dirx * fl, rim + 0.003 - drop, ez + dirz * fl)
    grid_surface(c, f, 32, 32, 1.48)
for i, (sh, rot) in enumerate([((0.0, 0.0), 0.03), ((0.17, -0.09), 0.16)]):
    n = 'SquareTable%d' % i; square_base(n); square_cloth(n, 200 + i, sh, rot)
square_base('SquareTable2')
c = part('SquareTable2', 'TCloth')   # camino de mesa
def runner(u, v):
    x = -0.62 + 1.24 * u; z = -0.16 + 0.32 * v
    y = 0.762 + 0.002 if abs(x) <= 0.475 else 0.762 - min(abs(x) - 0.475, 0.2)
    xx = math.copysign(min(abs(x), 0.475) + 0.01 * (abs(x) > 0.475), x)
    return (xx, y, z)
grid_surface(c, runner, 24, 2, 1.0)

# ====================================================================== sillas (huella 0,48: todo dentro de |x|,|z| <= 0,235)
def dining_chair(name):
    w = part(name, 'Wood'); u = part(name, 'Uph')
    for sx in (-1, 1):
        lathe(w, [(0.014, 0.0), (0.019, 0.04), (0.017, 0.2), (0.021, 0.3), (0.019, 0.40), (0.019, 0.44)], 8, x=sx * 0.195, z=0.19, cap_top=True)
        # pata trasera que sube al respaldo con inclinación hacia atrás
        pts = [(sx * 0.195, 0.0, -0.20), (sx * 0.195, 0.44, -0.195), (sx * 0.19, 0.62, -0.21), (sx * 0.185, 0.82, -0.225), (sx * 0.18, 0.97, -0.232)]
        tube(w, pts, 0.017, 8)
    for z in (0.19, -0.2):
        box(w, (0, 0.40, z), (0.37, 0.05, 0.022))
    for sx in (-1, 1): box(w, (sx * 0.195, 0.40, 0), (0.022, 0.05, 0.37))
    for sx in (-1, 1): box(w, (sx * 0.195, 0.14, 0), (0.016, 0.016, 0.37))
    box(w, (0, 0.14, -0.005), (0.37, 0.016, 0.016))
    # travesaño superior curvo (arqueado hacia atrás) y tablilla central con cintura
    pts = [(-0.185 + 0.37 * t, 0.94 + 0.02 * math.sin(t * math.pi), -0.232 - 0.0 * t + 0.0) for t in [i / 10 for i in range(11)]]
    pts = [(x, y, z - 0.0 + 0.0) for (x, y, z) in pts]
    for k in range(10):
        t0, t1 = k / 10, (k + 1) / 10
        x0, x1 = -0.185 + 0.37 * t0, -0.185 + 0.37 * t1
        bow = lambda t: 0.018 * math.sin(t * math.pi)
        box(w, ((x0 + x1) / 2, 0.935 + 0.02 * math.sin((t0 + t1) / 2 * math.pi), -0.226 + bow((t0 + t1) / 2) * 0.0 - 0.004), (abs(x1 - x0) + 0.004, 0.07, 0.024))
    o = len(w.v)
    prof = [(0.05, 0.47), (0.07, 0.55), (0.045, 0.66), (0.055, 0.78), (0.08, 0.88)]
    for (hw, y) in prof:
        zz = -0.215 - (y - 0.47) * 0.035
        for xx in (-hw, hw):
            for dz in (-0.009, 0.009): w.v.append((xx, y, zz + dz))
    for j in range(len(prof) - 1):
        a = o + j * 4; b = a + 4
        # caras: delante (dz -0.009 -> z menor? el respaldo mira a +z: la cara delantera es dz +0.009)
        w.f.append([a + 1, a + 3, b + 3, b + 1]); w.uv.append(None)
        w.f.append([a + 0, b + 0, b + 2, a + 2]); w.uv.append(None)
        w.f.append([a + 0, a + 1, b + 1, b + 0]); w.uv.append(None)
        w.f.append([a + 2, b + 2, b + 3, a + 3]); w.uv.append(None)
    # asiento tapizado (cojín abombado)
    def seat(uu, vv):
        x = -0.215 + 0.43 * uu; z = -0.205 + 0.42 * vv
        e = min(uu, 1 - uu, vv, 1 - vv); dome = 0.035 * math.sin(min(e * 6, 1) * math.pi / 2)
        return (x * (1 - 0.0), 0.425 + dome + 0.01 * (vv > 0.5) * 0, z)
    grid_surface(u, seat, 10, 10, 0.45)
    box(u, (0, 0.425, 0.0075), (0.43, 0.03, 0.42))
def banquet_chair(name):
    b = part(name, 'Brass'); cu = part(name, 'Bed')
    for sx in (-1, 1):
        tube(b, [(sx * 0.2, 0.0, 0.2), (sx * 0.195, 0.2, 0.193), (sx * 0.19, 0.44, 0.185)], 0.012, 8)
        tube(b, [(sx * 0.2, 0.0, -0.205), (sx * 0.195, 0.44, -0.2), (sx * 0.19, 0.7, -0.215), (sx * 0.185, 0.96, -0.228)], 0.012, 8)
        for y in (0.12, 0.3): tube(b, [(sx * 0.197, y, 0.19), (sx * 0.197, y, -0.2)], 0.007, 6)
    for y in (0.12, 0.3): tube(b, [(-0.195, y, 0.19), (0.195, y, 0.19)], 0.007, 6)
    tube(b, [(-0.19, 0.44, 0.185), (0.19, 0.44, 0.185)], 0.011, 8); tube(b, [(-0.195, 0.44, -0.2), (0.195, 0.44, -0.2)], 0.011, 8)
    for sx in (-1, 1): tube(b, [(sx * 0.192, 0.44, 0.185), (sx * 0.195, 0.44, -0.2)], 0.011, 8)
    for y in (0.58, 0.68, 0.78, 0.87):   # travesaños del respaldo
        zz = -0.2 - (y - 0.44) * 0.055
        tube(b, [(-0.19, y, zz), (-0.06, y + 0.006, zz - 0.004), (0.06, y + 0.006, zz - 0.004), (0.19, y, zz)], 0.008, 6)
    tube(b, [(-0.185, 0.955, -0.228), (0.0, 0.965, -0.233), (0.185, 0.955, -0.228)], 0.012, 8)
    def seat(uu, vv):
        x = -0.2 + 0.40 * uu; z = -0.195 + 0.39 * vv
        e = min(uu, 1 - uu, vv, 1 - vv); dome = 0.04 * math.sin(min(e * 5, 1) * math.pi / 2)
        return (x, 0.45 + dome, z)
    grid_surface(cu, seat, 10, 10, 0.4)
    box(cu, (0, 0.452, -0.0), (0.40, 0.016, 0.39))
dining_chair('Chair0'); banquet_chair('Chair1')

# ====================================================================== columna: basa, fuste (alto 1: se escala) y capitel
c = part('ColumnBase', 'Column')
box(c, (0, 0.06, 0), (0.80, 0.12, 0.80))
lathe(c, [(0.37, 0.12), (0.375, 0.135), (0.37, 0.15), (0.35, 0.165), (0.335, 0.17), (0.31, 0.18), (0.30, 0.19), (0.315, 0.20), (0.32, 0.21), (0.31, 0.225), (0.285, 0.235), (0.28, 0.25)], 32, cap_top=True)
c = part('ColumnShaft', 'Column')
def flute(t, y, r): return r - 0.016 * max(0.0, math.cos(t * 20)) ** 0.6
lathe(c, [(0.272 - 0.03 * (y ** 1.6), y) for y in [i / 6 for i in range(7)]], 120, rmod=flute, cap_top=False)
c = part('ColumnCap', 'Column')   # y = 0 en el arranque del capitel (fin del fuste)
lathe(c, [(0.25, 0.0), (0.27, 0.012), (0.27, 0.03), (0.25, 0.04), (0.25, 0.06), (0.29, 0.09), (0.34, 0.13), (0.37, 0.16)], 40, cap_top=True)
box(c, (0, 0.20, 0), (0.86, 0.08, 0.86)); box(c, (0, 0.255, 0), (0.80, 0.03, 0.80)); box(c, (0, 0.285, 0), (0.90, 0.03, 0.90))

# ====================================================================== mostrador de recepción (4,6 x 1,1 x 0,75; +z = cliente)
L = 4.6; w = part('Reception', 'Wood'); mb = part('Reception', 'Marble'); br = part('Reception', 'Brass')
box(w, (0, 0.55, -0.02), (L - 0.04, 1.10, 0.66))
box(w, (0, 0.05, 0.33), (L, 0.10, 0.05))                       # zócalo
box(w, (0, 1.07, 0.33), (L + 0.02, 0.06, 0.06))                 # cornisa
box(w, (0, 1.025, 0.355), (L, 0.02, 0.03))
npnl = 6; pw = (L - 0.2) / npnl
for i in range(npnl):
    x = -L / 2 + 0.1 + pw * (i + 0.5)
    box(w, (x, 0.58, 0.31), (pw - 0.12, 0.74, 0.012))          # fondo del panel
    box(w, (x, 0.58, 0.318), (pw - 0.22, 0.62, 0.018))         # panel en relieve
    for (dx, dy, sx, sy) in ((0, 0.335, pw - 0.1, 0.03), (0, -0.335, pw - 0.1, 0.03), (-(pw - 0.13) / 2, 0, 0.03, 0.7), ((pw - 0.13) / 2, 0, 0.03, 0.7)):
        box(w, (x + dx, 0.58 + dy, 0.322), (sx, sy, 0.026))     # moldura
for i in range(npnl + 1):
    box(w, (-L / 2 + 0.1 + pw * i, 0.58, 0.325), (0.05, 0.86, 0.035))   # pilastras
box(mb, (0, 1.1325, 0.06), (L + 0.1, 0.035, 0.86)); box(mb, (0, 1.108, 0.06), (L + 0.06, 0.016, 0.83))
box(br, (0, 0.105, 0.358), (L, 0.012, 0.008)); box(br, (0, 1.0, 0.374), (L, 0.01, 0.008))
box(w, (0, 0.78, -0.58), (L - 0.2, 0.035, 0.5))                 # repisa de trabajo detrás

# ====================================================================== a Blender
mats = {}
def mat(n):
    if n not in mats: mats[n] = bpy.data.materials.new(n)
    return mats[n]
total = 0
for (model, mname), m in PARTS.items():
    me = bpy.data.meshes.new(model + '__' + mname)
    bm = bmesh.new()
    vs = [bm.verts.new((-x, z, y)) for (x, y, z) in m.v]
    bm.verts.ensure_lookup_table()
    uvl = bm.loops.layers.uv.new('UVMap')
    for fc, uvs in zip(m.f, m.uv):
        try: f = bm.faces.new([vs[k] for k in fc])
        except ValueError: continue
        if uvs:
            for lp, uv in zip(f.loops, uvs): lp[uvl].uv = uv
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0003)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=0.00005)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bm.normal_update()
    for f in bm.faces:
        has = any(lp[uvl].uv.length > 0 for lp in f.loops)
        if not has:   # caja: proyección por la normal dominante (metros)
            n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
            for lp in f.loops:
                p = lp.vert.co; lp[uvl].uv = (p[1], p[2]) if ax == 0 else ((p[0], p[2]) if ax == 1 else (p[0], p[1]))
    if mname == 'TCloth':   # tela de una sola cara: que mire hacia fuera/arriba (desde el centro del mueble, a media altura)
        Cc = Vector((0, 0, 0.4)) if model.startswith('SquareTable') or model in ('RoundTable0', 'RoundTable1') else Vector((0.12, 0.18, -1.0))   # el montón del suelo: desde abajo
        acc = sum(f.normal.dot(f.calc_center_median() - Cc) * f.calc_area() for f in bm.faces)
        if acc < 0: bmesh.ops.reverse_faces(bm, faces=bm.faces[:]); bm.normal_update()
        print('CLOTH', model, 'flip' if acc < 0 else 'ok')
    for f in bm.faces: f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(42): e.smooth = False
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(model + '__' + mname, me); bpy.context.scene.collection.objects.link(ob)
    me.materials.append(mat(mname))
    total += len(me.polygons)
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
for ob in list(bpy.context.scene.collection.objects):
    if ob.modifiers:
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg)); ob.modifiers.clear(); old = ob.data; ob.data = me
for ob in bpy.context.scene.collection.objects:
    me = ob.data; me.calc_loop_triangles() if hasattr(me, 'calc_loop_triangles') else None
    bad = 0
    for poly in me.polygons:
        ln = [me.corner_normals[li].vector for li in poly.loop_indices] if hasattr(me, 'corner_normals') else [me.loops[li].normal for li in poly.loop_indices]
        if all(poly.normal.dot(n) <= 0 for n in ln): bad += 1
    print('OBJ', ob.name, len(me.polygons), 'bad', bad)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         mesh_smooth_type='OFF', add_leaf_bones=False, bake_space_transform=False, object_types={'MESH'}, use_mesh_modifiers=True)
print('FURN total faces', sum(len(o.data.polygons) for o in bpy.context.scene.collection.objects))
