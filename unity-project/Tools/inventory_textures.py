# Texturas del inventario (maletín metálico con espuma, etiqueta de papel, cinta adhesiva). Uso: python3 Tools/inventory_textures.py
import numpy as np, os
from PIL import Image, ImageFilter, ImageDraw
OUT = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Resources', 'UI')
rng = np.random.default_rng(7)
def noise(h, w, scale, oct=4):
    acc = np.zeros((h, w)); amp = 1; tot = 0
    for o in range(oct):
        s = max(1, int(scale / 2 ** o))
        small = rng.random((h // s + 2, w // s + 2))
        im = Image.fromarray((small * 255).astype(np.uint8)).resize((w + 2 * s, h + 2 * s), Image.BICUBIC)
        acc += np.asarray(im, float)[s:s + h, s:s + w] / 255 * amp; tot += amp; amp *= 0.5
    return acc / tot
def save(a, name):
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(os.path.join(OUT, name)); print(name)

# Maletín: chapa verde oliva oscura cepillada, desgaste en bordes, remaches
W, H = 2048, 1024
base = np.array([46, 50, 38], float)
brush = np.repeat(rng.normal(0, 1, (H, 1)), W, 1) * 0.6 + rng.normal(0, 1, (H, W)) * 0.5
brush = np.asarray(Image.fromarray(((brush + 3) * 40).clip(0, 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(1)), float) / 40 - 3
grime = noise(H, W, 220)
img = base[None, None, :] * (0.78 + 0.35 * grime[..., None]) + brush[..., None] * 3
y, x = np.mgrid[0:H, 0:W]
edge = np.minimum.reduce([x, y, W - 1 - x, H - 1 - y]).astype(float)
wear = (edge < 12 + noise(H, W, 40) * 14) & (noise(H, W, 18) > 0.48)
img[wear] = img[wear] * 0.4 + np.array([120, 118, 108]) * 0.6          # metal desnudo en bordes
scr = Image.new('L', (W, H), 0); d = ImageDraw.Draw(scr)
for _ in range(260):
    x0, y0 = rng.integers(0, W), rng.integers(0, H); L = rng.integers(10, 90); a = rng.uniform(0, np.pi)
    d.line([(x0, y0), (x0 + L * np.cos(a), y0 + L * np.sin(a))], fill=int(rng.integers(40, 110)), width=1)
img += np.asarray(scr, float)[..., None] * 0.22
# borde biselado
bev = np.clip(1 - edge / 14, 0, 1)
img *= (1 - 0.45 * bev[..., None])
inner = (edge > 14) & (edge < 18); img[inner] += 18
for (rx, ry) in [(40, 40), (W - 40, 40), (40, H - 40), (W - 40, H - 40), (W // 2, 40), (W // 2, H - 40)]:
    rr = (x - rx) ** 2 + (y - ry) ** 2
    m = rr < 13 ** 2; img[m] = 95 + 70 * np.clip(1 - ((x[m] - rx + 4) ** 2 + (y[m] - ry + 4) ** 2) / 160, 0, 1)[..., None]
    img[(rr >= 13 ** 2) & (rr < 16 ** 2)] *= 0.5
# vignette suave
v = 1 - 0.35 * (((x / W - 0.5) * 2) ** 2 + ((y / H - 0.5) * 2) ** 2) / 2
img *= v[..., None]
save(np.dstack([img, np.full((H, W), 255)]), 'case_metal.png')

# Espuma (recorte de una casilla): gris carbón con poros + sombra interior
S = 256
f = 26 + noise(S, S, 3, 2) * 22 + rng.normal(0, 4, (S, S))
pores = rng.random((S, S)) > 0.985; f[pores] -= 18
f = np.asarray(Image.fromarray(f.clip(0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6)), float)
yy, xx = np.mgrid[0:S, 0:S]
e = np.minimum.reduce([xx, yy, S - 1 - xx, S - 1 - yy]).astype(float)
shade = np.clip(e / 34, 0, 1) ** 0.7
top = np.clip((yy) / 60, 0, 1) * 0.25 + 0.75     # sombra más fuerte arriba (luz desde arriba)
f = f * (0.35 + 0.65 * shade) * top
f[e < 3] = 14
foam = np.dstack([f * 0.98, f, f * 1.02, np.full((S, S), 255)])
save(foam, 'foam_slot.png')

# Panel de espuma grande (fondo de la rejilla)
S2 = 512
g = 34 + noise(S2, S2, 4, 2) * 20 + rng.normal(0, 4, (S2, S2))
save(np.dstack([g * 0.98, g, g * 1.02, np.full((S2, S2), 255)]), 'foam_panel.png')

# Etiqueta de papel envejecido
P = 1024
pap = np.array([214, 202, 168], float)[None, None, :] * (0.90 + 0.07 * noise(P, P, 120)[..., None]) + rng.normal(0, 3, (P, P, 1))
stain = noise(P, P, 260) > 0.62; pap[stain] *= 0.965
py, px = np.mgrid[0:P, 0:P]
pe = np.minimum.reduce([px, py, P - 1 - px, P - 1 - py]).astype(float)
pap *= (0.8 + 0.2 * np.clip(pe / 40, 0, 1))[..., None]
# renglones tenues de formulario
for ry in range(150, P, 64): pap[ry:ry + 2, 40:P - 40] *= 0.9
a = np.where(pe < 2 + noise(P, P, 8) * 4, 0, 255)
save(np.dstack([pap, a]), 'paper_tag.png')

# Cinta adhesiva
T = 256
t = np.array([196, 186, 150], float)[None, None, :] * (0.92 + 0.1 * noise(64, T, 12)[..., None])
ta = np.full((64, T), 200.0); tx = np.arange(T)
for col in range(T):
    jag = int(4 + 4 * np.sin(col * 1.7) + rng.integers(0, 4))
    ta[:jag // 2, col] = 0; ta[64 - jag // 2:, col] = 0
ta[:, :3] = 0; ta[:, -3:] = 0
save(np.dstack([t, ta]), 'tape.png')
