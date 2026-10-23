# Texturas originales (procedurales) del casco de buzo de latón envejecido para Backrooms: Escapa v7.3.0.
# Mapa UV del torno: u = ángulo/2π (u=0 delante, +z; u=0.25 derecha), v = 0..0.30 peto, 0.30..0.40 collar, 0.40..1 cúpula.
import numpy as np
from PIL import Image
W, H = 2048, 1024
rng = np.random.default_rng(7301)
u = (np.arange(W) + 0.5) / W
v = (np.arange(H) + 0.5) / H
U, V = np.meshgrid(u, v)          # fila = v (0 abajo tras volcar al guardar)
MM_U = 1260.0 / W                  # mm por píxel en u (circunferencia de la cúpula ~1,26 m)
MM_V = 760.0 / H                   # mm por píxel en v (longitud del perfil ~0,76 m)

def fbm(scale, octaves=5, seed=0):
    r = np.random.default_rng(seed); out = np.zeros((H, W))
    amp = 1.0; tot = 0
    for o in range(octaves):
        n = max(2, int(scale * 2 ** o))
        g = r.random((n + 1, n * 2 + 1))
        g[:, -1] = g[:, 0]                     # periódico en u
        yi = np.linspace(0, n, H); xi = np.linspace(0, n * 2, W)
        y0 = np.floor(yi).astype(int).clip(0, n - 1); x0 = np.floor(xi).astype(int).clip(0, n * 2 - 1)
        fy = (yi - y0)[:, None]; fx = (xi - x0)[None, :]
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        a = g[y0][:, x0]; b = g[y0][:, x0 + 1]; c = g[y0 + 1][:, x0]; d = g[y0 + 1][:, x0 + 1]
        out += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy)
        tot += amp; amp *= 0.5
    return out / tot

def du_mm(u0):                       # distancia en mm en u (periódica)
    d = np.abs(U - u0); d = np.minimum(d, 1 - d); return d * W * MM_U
def dv_mm(v0): return np.abs(V - v0) * H * MM_V

h = np.zeros((H, W))                 # altura en mm
feature = np.zeros((H, W))           # 1 = relieve (remaches, pernos, cordones): bordes pulidos

def bump(du, dv, r_mm, hgt):         # cabeza de remache (casquete)
    d2 = (du ** 2 + dv ** 2) / (r_mm ** 2)
    m = d2 < 1
    return np.where(m, hgt * np.sqrt(np.clip(1 - d2, 0, 1)), 0.0), m

def rivet_row_u(v0, n, r=2.2, hgt=1.3, phase=0.0):     # fila de remaches a lo largo de u
    global h, feature
    dv = (V - v0) * H * MM_V
    for k in range(n):
        uk = (k + 0.5 + phase) / n
        dd = U - uk; dd = (dd + 0.5) % 1 - 0.5
        b, m = bump(dd * W * MM_U, dv, r, hgt); h = np.maximum(h, b); feature = np.maximum(feature, m * 1.0)

def rivet_row_v(u0, v0, v1, spacing_mm=14, r=2.0, hgt=1.2):   # fila vertical (costura de la cúpula)
    global h, feature
    dd = U - u0; dd = (dd + 0.5) % 1 - 0.5
    duu = dd * W * MM_U
    n = int((v1 - v0) * H * MM_V / spacing_mm)
    for k in range(n + 1):
        vk = v0 + (v1 - v0) * k / max(1, n)
        b, m = bump(duu, (V - vk) * H * MM_V, r, hgt); h = np.maximum(h, b); feature = np.maximum(feature, m * 1.0)

def seam_u(v0, w=1.6, hgt=0.6):      # cordón de soldadura horizontal
    global h, feature
    d = dv_mm(v0); s = np.clip(1 - (d / w) ** 2, 0, 1)
    h = np.maximum(h, s * hgt * (0.85 + 0.15 * fbm(40, 2, 11))); feature = np.maximum(feature, (s > 0.3) * 0.6)
def seam_v(u0, v0, v1, w=1.6, hgt=0.6):
    global h, feature
    d = du_mm(u0); s = np.clip(1 - (d / w) ** 2, 0, 1) * ((V >= v0) & (V <= v1))
    h = np.maximum(h, s * hgt * (0.85 + 0.15 * fbm(40, 2, 12))); feature = np.maximum(feature, (s > 0.3) * 0.6)

# ---- peto (0..0.30): borde enrollado, chapa martillada, 12 pernos del peto
roll = np.clip(1 - np.abs(V - 0.03) / 0.018, 0, 1); h += roll * 1.8; feature = np.maximum(feature, (roll > 0.5) * 0.8)
plate = (V > 0.045) & (V < 0.29)
h += plate * (fbm(18, 4, 3) - 0.5) * 0.9          # chapa martillada (abolladuras suaves)
for k in range(12):                                # pernos hexagonales del peto
    uk = (k + 0.5) / 12; dd = (U - uk + 0.5) % 1 - 0.5
    x = dd * W * MM_U; y = (V - 0.235) * H * MM_V
    ang = np.arctan2(y, x); rr = np.sqrt(x * x + y * y)
    hexr = 6.0 / np.cos(((ang + np.pi / 6) % (np.pi / 3)) - np.pi / 6)
    m = rr < hexr
    washer = (rr < 8.5) & ~m
    h = np.maximum(h, np.where(m, 3.0 + 0.4 * np.clip(1 - rr / 6, 0, 1), np.where(washer, 1.2, 0)))
    feature = np.maximum(feature, m * 1.0 + washer * 0.5)
seam_u(0.285, 1.8, 0.8)
# ---- collar (0.30..0.40): banda en relieve con 8 pernos grandes y remaches finos
band = (V > 0.305) & (V < 0.395)
h += band * 1.4
edge = np.clip(1 - np.minimum(np.abs(V - 0.305), np.abs(V - 0.395)) * H * MM_V / 1.5, 0, 1)
feature = np.maximum(feature, edge * 0.8)
for k in range(8):
    uk = k / 8 + 1 / 16; dd = (U - uk + 0.5) % 1 - 0.5
    b, m = bump(dd * W * MM_U, (V - 0.35) * H * MM_V, 7.5, 3.6)
    h = np.maximum(h, b + band * 1.4); feature = np.maximum(feature, m * 1.0)
rivet_row_u(0.318, 48, 1.6, 0.8, 0.25); rivet_row_u(0.382, 48, 1.6, 0.8, 0.25)
# ---- cúpula (0.40..1): costuras soldadas con remaches entre los ojos de buey, anillo inferior y corona
dome = V >= 0.40
h += dome * (fbm(22, 4, 5) - 0.5) * 0.5
seam_u(0.44, 1.8, 0.7); rivet_row_u(0.452, 64, 1.9, 1.1)
seam_u(0.86, 1.8, 0.7); rivet_row_u(0.872, 40, 1.9, 1.1, 0.5)
for uk in (0.125, 0.375, 0.625, 0.875):
    seam_v(uk, 0.44, 0.86); rivet_row_v(uk + 0.006, 0.46, 0.85)
# arañazos finos y desgaste
scr = (fbm(220, 2, 21) > 0.82) * 0.08
h -= scr
# ---- máscaras
blur = h.copy()
for s in (2, 4, 8, 16):              # desenfoque barato (cajas sucesivas) para la cavidad
    blur = (np.roll(blur, s, 0) + np.roll(blur, -s, 0) + np.roll(blur, s, 1) + np.roll(blur, -s, 1) + blur) / 5
cav = np.clip((blur - h) * 1.6, 0, 1)             # hendiduras alrededor de remaches y cordones
low = fbm(6, 4, 31)
verd = np.clip(cav * 1.4 + (low - 0.55) * 1.6 + np.clip(0.30 - V, 0, 1) * 0.9 * (low > 0.45), 0, 1)
verd = np.clip(verd * (0.6 + 0.8 * fbm(60, 3, 41)) - 0.15, 0, 1) ** 1.2
polish = np.clip(feature * 0.9 + np.clip((h - blur) * 0.9, 0, 1), 0, 1) * (1 - verd)
grime = np.clip(cav * 0.8 + (fbm(12, 4, 51) - 0.5) * 0.5, 0, 1)
# ---- albedo (latón envejecido: base cálida apagada, cardenillo verde azulado, aristas pulidas más claras)
base = np.array([0.52, 0.38, 0.17])
oxid = np.array([0.34, 0.25, 0.12])
shiny = np.array([0.86, 0.68, 0.36])
verdc = np.array([0.30, 0.50, 0.42])
tone = fbm(10, 4, 61)[..., None]
alb = base * (0.85 + 0.3 * tone) * (1 - 0.35 * grime[..., None]) + oxid * 0.0
alb = alb * (1 - polish[..., None] * 0.6) + shiny * polish[..., None] * 0.6
alb = alb * (1 - verd[..., None]) + verdc * (0.8 + 0.4 * fbm(90, 2, 71))[..., None] * verd[..., None]
alb = np.clip(alb, 0, 1)
metal = np.clip((0.58 + polish * 0.35) * (1 - verd * 1.1), 0, 1)   # latón empañado: parte difusa para que se vea con luz ambiente
smooth = np.clip(0.42 + polish * 0.4 - grime * 0.18 - verd * 0.3 + (fbm(30, 3, 81) - 0.5) * 0.12, 0.08, 0.9)
ao = np.clip(1 - cav * 1.2 - (1 - np.clip(feature + 0.7, 0, 1)) * 0.0, 0.25, 1)
# ---- mapa de normales (tangente u, bitangente v; la imagen se guarda con v hacia arriba)
gy, gx = np.gradient(h)
gx = gx / MM_U; gy = gy / MM_V
k = 1.4
n = np.dstack([-gx * k, -gy * k, np.ones_like(h)]); n /= np.linalg.norm(n, axis=2, keepdims=True)
def save(a, name, mode='RGB'):
    img = Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), mode)
    img.transpose(Image.FLIP_TOP_BOTTOM).save(name)
save(alb, 'HelmetBrass_a.png')
save(n * 0.5 + 0.5, 'HelmetBrass_n.png')
save(np.dstack([metal, ao, np.zeros_like(ao), smooth]), 'HelmetBrass_m.png', 'RGBA')
print('ok', h.min(), h.max(), verd.mean(), polish.mean())
