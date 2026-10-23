# v7.3.0: texturas procedurales propias del muelle (semilla 7302): tablas envejecidas y mojadas (4 variantes),
# pilotes con algas/musgo/percebes junto a la línea de flotación y cuerda. Albedo, normales y máscara (R metal, G AO, A suavidad).
import numpy as np, os
from PIL import Image
R = np.random.default_rng(7302); OUT = os.path.dirname(os.path.abspath(__file__))
def up(a, H, W):
    from PIL import Image as I
    return np.asarray(I.fromarray(a.astype(np.float32), 'F').resize((W, H), I.BICUBIC))
def fbm(H, W, sy, sx, oct=5, seed=0, wrapx=True):
    g = np.random.default_rng(seed); acc = np.zeros((H, W), np.float32); amp = 1; tot = 0
    for o in range(oct):
        h, w = max(2, int(sy * 2 ** o)), max(2, int(sx * 2 ** o))
        n = g.random((h + 1, w + 1)).astype(np.float32)
        if wrapx: n[:, -1] = n[:, 0]
        acc += up(n, H, W) * amp; tot += amp; amp *= 0.5
    return acc / tot
def norm_from_h(h, mm_u, mm_v, k):
    gy, gx = np.gradient(h); n = np.dstack([-gx / mm_u * k, -gy / mm_v * k, np.ones_like(h)])
    n /= np.linalg.norm(n, axis=2, keepdims=True); return ((n * 0.5 + 0.5) * 255).astype(np.uint8)
def save(a, n, m, name):
    Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)[::-1], 'RGB').save(os.path.join(OUT, name + '_a.png'))
    Image.fromarray(n[::-1], 'RGB').save(os.path.join(OUT, name + '_n.png'))
    Image.fromarray((np.clip(m, 0, 1) * 255).astype(np.uint8)[::-1], 'RGBA').save(os.path.join(OUT, name + '_m.png'))

# ---------- tablas: 2048 (u, 2,4 m a lo largo) x 1024 (v, 4 filas de 0,22 m)
W, H = 2048, 1024; RH = H // 4
alb = np.zeros((H, W, 3), np.float32); hgt = np.zeros((H, W), np.float32); sm = np.zeros((H, W), np.float32); ao = np.ones((H, W), np.float32)
u = np.linspace(0, 1, W, endpoint=False)[None, :]
for k in range(4):
    v = np.linspace(0, 1, RH, endpoint=False)[:, None]
    s = 100 + k * 17
    warp = fbm(RH, W, 2, 6, 4, s) * 0.35
    grain = np.sin((v * 26 + warp * 22 + fbm(RH, W, 3, 40, 3, s + 1) * 1.6) * np.pi * 2) * 0.6 + np.sin((v * 71 + warp * 30) * np.pi * 2) * 0.4
    grain = 0.5 + 0.5 * grain
    fine = fbm(RH, W, 24, 400, 3, s + 2)
    # nudos
    kn = np.zeros((RH, W), np.float32)
    for _ in range(R.integers(1, 4)):
        cu, cv = R.random(), 0.2 + 0.6 * R.random(); d = np.sqrt(((u - cu) * 2.4 / 0.22 * 0.6) ** 2 + (v - cv) ** 2)
        kn = np.maximum(kn, np.clip(1 - d / (0.08 + 0.07 * R.random()), 0, 1))
    # grietas a lo largo de la veta
    cr = np.zeros((RH, W), np.float32)
    for _ in range(R.integers(2, 6)):
        cv = R.random(); u0 = R.random(); L = 0.1 + 0.4 * R.random()
        line = np.exp(-((v - cv - (fbm(1, W, 1, 8, 3, int(R.integers(1e6)))[0] - 0.5) * 0.08) / 0.006) ** 2)
        mask = np.clip(1 - np.abs(((u - u0 + 0.5) % 1) - 0.5) / L * 2, 0, 1) ** 0.5
        cr = np.maximum(cr, line * mask)
    edge = np.clip(np.minimum(v, 1 - v) / 0.05, 0, 1); endd = np.clip(np.minimum(u, 1 - u) / 0.01, 0, 1)
    wear = fbm(RH, W, 3, 20, 5, s + 3)
    wet = np.clip((fbm(RH, W, 2, 8, 4, s + 4) - 0.45) * 3, 0, 1)
    algae = np.clip((1 - edge) * 1.2 * np.clip((fbm(RH, W, 4, 60, 3, s + 5) - 0.35) * 3, 0, 1), 0, 1)
    base = np.array([0.42, 0.37, 0.30]) * (0.85 + 0.3 * R.random())
    grey = np.array([0.50, 0.49, 0.46])
    c = base * (0.75 + 0.35 * grain[..., None]) * (0.85 + 0.3 * fine[..., None])
    c = c * (1 - wear[..., None] * 0.5) + grey * wear[..., None] * 0.5             # gris de intemperie
    c = c * (1 - 0.5 * kn[..., None]) ; c = c * (1 - 0.75 * cr[..., None])
    c = c * (1 - 0.45 * wet[..., None]) * (0.55 + 0.45 * edge[..., None]) * (0.6 + 0.4 * endd[..., None])
    c = c * (1 - algae[..., None] * 0.6) + np.array([0.10, 0.16, 0.06]) * algae[..., None] * 0.6
    h = grain * 0.25 + fine * 0.15 - cr * 0.9 - (1 - edge) * 0.5 - kn * 0.2
    # clavos en los largueros (u 0,04 / 0,5 / 0,96), dos por tabla
    for nu in (0.04, 0.5, 0.96):
        for nv in (0.27, 0.73):
            d = np.sqrt(((u - nu) * 2.4) ** 2 + ((v - nv) * 0.22) ** 2)   # metros
            head = np.clip(1 - d / 0.006, 0, 1); ring = np.clip(1 - np.abs(d - 0.007) / 0.002, 0, 1)
            rust = np.clip(1 - d / 0.03, 0, 1) ** 2 * (0.6 + 0.4 * fine)
            streak = np.clip(1 - np.abs((u - nu) * 2.4) / 0.004, 0, 1) * np.clip(1 - (v - nv) * 0.22 / 0.05, 0, 1) * (v > nv)
            c = c * (1 - rust[..., None] * 0.55) + np.array([0.30, 0.14, 0.05]) * rust[..., None] * 0.55
            c = c * (1 - streak[..., None] * 0.3) + np.array([0.28, 0.13, 0.05]) * streak[..., None] * 0.3
            c = c * (1 - head[..., None]) + np.array([0.12, 0.10, 0.09]) * head[..., None]
            h = h + head ** 0.5 * 0.5 - ring * 0.3
    r0 = k * RH
    alb[r0:r0 + RH] = c; hgt[r0:r0 + RH] = h
    sm[r0:r0 + RH] = 0.12 + wet * 0.38 + algae * 0.25
    ao[r0:r0 + RH] = np.clip(0.55 + 0.45 * edge, 0, 1) * (1 - cr * 0.5)
mm_u = 2400 / W; mm_v = 220 / RH
m = np.zeros((H, W, 4), np.float32); m[..., 1] = ao; m[..., 3] = sm
save(alb, norm_from_h(hgt, mm_u, mm_v, 1.2), m, 'PierPlank')

# ---------- pilotes: 512 (u, contorno) x 2048 (v: altura y = -4 m .. +1,6 m), línea de flotación en v = 4/5,6
W, H = 512, 2048
uu = np.linspace(0, 1, W, endpoint=False)[None, :]; y = np.linspace(-4, 1.6, H)[:, None]
grain = 0.5 + 0.5 * np.sin((uu * 22 + fbm(H, W, 30, 3, 4, 201) * 2.5) * np.pi * 2)
fine = fbm(H, W, 300, 60, 3, 202)
cr = np.clip((fbm(H, W, 6, 40, 3, 203) - 0.7) * 6, 0, 1) * (np.sin(uu * np.pi * 2 * 7 + fbm(H, W, 4, 3, 3, 204) * 6) > 0.95)
c = np.array([0.36, 0.31, 0.25]) * (0.75 + 0.35 * grain[..., None]) * (0.85 + 0.3 * fine[..., None])
c = c * (1 - 0.6 * cr[..., None])
wave = 0.12 * np.sin(uu * np.pi * 2 * 3) + (fbm(1, W, 1, 6, 3, 205)[0] - 0.5) * 0.3
wetz = np.clip((0.75 + wave - y) / 0.5, 0, 1)                         # mojado hasta ~0,75 m
mossz = np.clip((0.3 + wave - y) / 0.35, 0, 1) * np.clip((y + 1.2) / 0.8, 0, 1)   # musgo/algas verdes en la franja
deep = np.clip((-0.4 - y) / 0.8, 0, 1)                                 # algas pardas bajo el agua
mossn = fbm(H, W, 80, 40, 5, 206)
moss = np.clip(mossz * (0.6 + mossn) - 0.3, 0, 1) ** 0.8
weed = deep * np.clip(fbm(H, W, 40, 20, 4, 207) * 1.4 - 0.2, 0, 1)
barn = np.zeros((H, W), np.float32)
for _ in range(900):
    by = R.uniform(-3.5, 0.15); bu = R.random(); rr = R.uniform(0.004, 0.012)
    iy = int((by + 4) / 5.6 * H); iu = int(bu * W); sy = max(2, int(rr / 5.6 * H * 1.0)); su = max(2, int(rr / 1.07 * W))
    ys, xs = slice(max(0, iy - sy * 2), min(H, iy + sy * 2)), np.arange(iu - su * 2, iu + su * 2) % W
    yy = np.arange(ys.start, ys.stop)[:, None]; xx = xs[None, :]
    d = np.sqrt(((yy - iy) / sy) ** 2 + (((xx - iu + W / 2) % W - W / 2) / su) ** 2)
    barn[ys][:, xs] = np.maximum(barn[ys][:, xs], np.clip(1 - d, 0, 1))
    barn[ys.start:ys.stop, xs] = np.maximum(barn[ys.start:ys.stop, xs], np.clip(1 - d, 0, 1))
c = c * (1 - 0.5 * wetz[..., None])
c = c * (1 - weed[..., None] * 0.8) + np.array([0.08, 0.09, 0.04]) * weed[..., None] * 0.8
c = c * (1 - moss[..., None]) + (np.array([0.08, 0.15, 0.04]) * (0.6 + 0.5 * mossn[..., None])) * moss[..., None]
bmask = (barn > 0.05).astype(np.float32)
c = c * (1 - bmask[..., None] * 0.85) + np.array([0.55, 0.53, 0.47]) * (0.6 + 0.4 * barn[..., None]) * bmask[..., None] * 0.85
h = grain * 0.3 + fine * 0.2 - cr * 0.8 + moss * 0.5 * mossn + weed * 0.3 + barn ** 0.6 * 1.2
sm = 0.12 + wetz * 0.35 + moss * 0.2
aoP = np.clip(1 - cr * 0.5 - (barn > 0) * (1 - barn) * 0.3, 0.3, 1)
m = np.zeros((H, W, 4), np.float32); m[..., 1] = aoP; m[..., 3] = sm
save(c, norm_from_h(h, 1070 / W, 5600 / H, 1.2), m, 'PierPost')

# ---------- cuerda: 256 (u, contorno) x 256 (v, 0,08 m a lo largo); cordones en espiral
W = H = 256
uu = np.linspace(0, 1, W, endpoint=False)[None, :]; vv = np.linspace(0, 1, H, endpoint=False)[:, None]
ph = (uu * 3 + vv * 3) % 1.0
strand = np.sin(ph * np.pi) ** 0.7
fib = 0.5 + 0.5 * np.sin((uu * 40 - vv * 20 + fbm(H, W, 8, 8, 3, 301) * 2) * np.pi * 2)
c = np.array([0.52, 0.43, 0.28]) * (0.55 + 0.45 * strand[..., None]) * (0.85 + 0.2 * fib[..., None])
grime = fbm(H, W, 4, 4, 4, 302); c = c * (0.8 + 0.2 * grime[..., None]) * np.array([0.9, 0.95, 0.85])
h = strand * 1.0 + fib * 0.15
m = np.zeros((H, W, 4), np.float32); m[..., 1] = 0.5 + 0.5 * strand; m[..., 3] = 0.08 + 0.1 * grime
save(c, norm_from_h(h, 300 / W, 80 / H, 1.0), m, 'PierRope')
print('ok')
