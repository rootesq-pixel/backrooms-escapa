# Nivel 7460 (pasillo de puertas): texturas PBR procedurales periódicas (numpy) -> <Nombre>_a.jpg / _n.png / _m.png
# m = máscara Standard: R metálico, G oclusión, B 0, A suavidad. Uso: python3 textures.py <salida>
# Escalas (metros por textura): suelo 6,8 m, pared 2,0 m, molduras/puertas 1,0 m, techo 2,0 m.
import sys, os, numpy as np, cv2
OUT = sys.argv[1] if len(sys.argv) > 1 else '/workspace/v7460/tex'
os.makedirs(OUT, exist_ok=True)

def fbm(n, scale, oct=5, gain=0.5, seed=0, aniso=1.0):
    r = np.random.default_rng(seed)
    acc = np.zeros((n, n), np.float32); amp = 1.0; tot = 0
    fy = np.fft.fftfreq(n)[:, None] * n; fx = np.fft.fftfreq(n)[None, :] * n
    f = np.sqrt(fx * fx + (fy * aniso) ** 2)
    for o in range(oct):
        fr = scale * (2 ** o)
        spec = np.exp(-((f - fr) / (fr * 0.6 + 1e-6)) ** 2) * (f > 0)
        ph = np.fft.ifft2(spec * np.exp(2j * np.pi * r.random((n, n)))).real
        ph /= (ph.std() + 1e-9)
        acc += amp * ph; tot += amp; amp *= gain
    acc /= tot
    return (acc - acc.min()) / (acc.max() - acc.min() + 1e-9)

def blur(a, s):
    if s <= 0: return a
    p = int(s * 4) + 2
    b = np.pad(a, ((p, p), (p, p)) + ((0, 0),) * (a.ndim - 2), mode='wrap')
    return cv2.GaussianBlur(b, (0, 0), s)[p:-p, p:-p]
def srgb_to_lin(c): return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5
def save(name, albedo, height, smooth_, ao, strength=3.0, metal=None):
    a = np.clip(albedo, 0, 1)
    cv2.imwrite(os.path.join(OUT, name + '_a.jpg'), (a[..., ::-1] * 255 + 0.5).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 94])
    n = normal_from_height(height.astype(np.float32), strength)
    cv2.imwrite(os.path.join(OUT, name + '_n.png'), (n[..., ::-1] * 255 + 0.5).astype(np.uint8))
    m = np.zeros(a.shape[:2] + (4,), np.float32)
    m[..., 0] = 0 if metal is None else metal; m[..., 1] = np.clip(ao, 0, 1); m[..., 3] = np.clip(smooth_, 0, 1)
    cv2.imwrite(os.path.join(OUT, name + '_m.png'), (m[..., [2, 1, 0, 3]] * 255 + 0.5).astype(np.uint8))
    print('tex', name, a.shape, 'albedo mean', a.reshape(-1, 3).mean(0).round(3), 'smooth', float(np.mean(smooth_)), flush=True)

def col(rgb255): return np.array(rgb255, np.float32) / 255.0

# --- Suelo: resina epoxi gris pulida (como el de la imagen): tono casi uniforme, nubes muy suaves, árido fino, marcas de pulido
def floor(n=2048):
    base = col((128, 131, 136))
    big = fbm(n, 2, 4, 0.55, 11); mid = fbm(n, 9, 4, 0.5, 12); fine = fbm(n, 90, 2, 0.5, 13)
    tone = 1.0 + (big - 0.5) * 0.07 + (mid - 0.5) * 0.035 + (fine - 0.5) * 0.02
    r = np.random.default_rng(14)
    speck = (r.random((n, n)) > 0.9985).astype(np.float32); speck = blur(speck, 0.7) * 6
    alb = base[None, None, :] * tone[..., None] * (1 - 0.10 * np.clip(speck, 0, 1))[..., None] + 0.05 * np.clip(blur((r.random((n, n)) > 0.9993).astype(np.float32), 0.6) * 5, 0, 1)[..., None]
    # pulido: remolinos de la pulidora (arcos) en la rugosidad, muy sutiles
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    swirl = np.zeros((n, n), np.float32)
    for k in range(18):
        cx, cy, R = r.random(), r.random(), 0.04 + r.random() * 0.08
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                d = np.sqrt((x - cx - dx) ** 2 + (y - cy - dy) ** 2)
                swirl += np.exp(-((d - R) / 0.004) ** 2) * 0.5
    scratches = np.zeros((n, n), np.float32)
    for k in range(260):
        x0, y0 = r.integers(0, n, 2); L = r.integers(20, 160); a = r.random() * np.pi
        x1, y1 = int(x0 + np.cos(a) * L), int(y0 + np.sin(a) * L)
        cv2.line(scratches, (int(x0), int(y0)), (x1, y1), 1.0, 1, cv2.LINE_AA)
    scratches = np.maximum(scratches, np.roll(np.roll(scratches, n // 2, 0), n // 2, 1) * 0)  # (sin costura: se usa tal cual, líneas cortas)
    sm = 0.90 - 0.06 * np.clip(swirl, 0, 1) - 0.10 * scratches - (big - 0.5) * 0.05 - (mid - 0.5) * 0.04
    h = (big - 0.5) * 0.4 + (fine - 0.5) * 0.15 - scratches * 0.25
    save('C7460Floor', alb, h, sm, np.ones((n, n)), strength=1.2)

# --- Pared: pintura plástica gris pizarra mate-satinada; piel de naranja del rodillo y pasadas verticales casi invisibles
def wall(n=1024):
    base = col((86, 92, 104))
    peel = fbm(n, 120, 3, 0.55, 21); roll = fbm(n, 6, 3, 0.5, 22, aniso=0.15); big = fbm(n, 1, 3, 0.5, 23)
    tone = 1.0 + (roll - 0.5) * 0.035 + (big - 0.5) * 0.03 + (peel - 0.5) * 0.02
    alb = base[None, None, :] * tone[..., None]
    sm = 0.42 + (peel - 0.5) * 0.10 + (roll - 0.5) * 0.04
    h = (peel - 0.5) * 1.0 + (roll - 0.5) * 0.3
    save('C7460Wall', alb, h, sm, np.ones((n, n)), strength=2.2)

# --- Blanco satinado de molduras, zócalos y puertas (esmalte al agua): pinceladas muy finas a lo largo (eje U)
def trim(n=1024):
    base = col((236, 237, 234))
    brush = fbm(n, 40, 3, 0.5, 31, aniso=8.0); peel = fbm(n, 160, 2, 0.5, 32); big = fbm(n, 2, 3, 0.5, 33)
    tone = 1.0 + (big - 0.5) * 0.02 + (brush - 0.5) * 0.012
    alb = base[None, None, :] * tone[..., None]
    sm = 0.66 + (brush - 0.5) * 0.08 + (peel - 0.5) * 0.04
    h = (brush - 0.5) * 0.5 + (peel - 0.5) * 0.4
    save('C7460Trim', alb, h, sm, np.ones((n, n)), strength=1.6)

# --- Techo: pintura blanca mate con textura de gotelé finísima
def ceil(n=1024):
    base = col((231, 232, 231))
    g = fbm(n, 140, 3, 0.55, 41); big = fbm(n, 2, 3, 0.5, 42)
    alb = base[None, None, :] * (1.0 + (big - 0.5) * 0.02 + (g - 0.5) * 0.02)[..., None]
    save('C7460Ceil', alb, (g - 0.5), 0.12 + (g - 0.5) * 0.05, np.ones((n, n)), strength=2.0)

# --- Ladrillo tapiado (puertas que no llevan a ninguna parte): ladrillo gris pintado con llagas de mortero
def brick(n=1024):
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    rows = 16; bh = 1.0 / rows; bw = 1.0 / 4
    row = np.floor(y / bh); off = (row % 2) * 0.5 * bw
    bx = ((x + off) % bw) / bw; by = (y % bh) / bh
    mort = np.minimum(np.minimum(bx, 1 - bx) * bw, np.minimum(by, 1 - by) * bh) < 0.006
    noise = fbm(n, 30, 4, 0.5, 51); big = fbm(n, 3, 3, 0.5, 52)
    r = np.random.default_rng(53); per = r.random((rows, 8))
    idx = (np.floor((x + off) / bw) % 4).astype(int); var = per[row.astype(int) % rows, idx]
    base = col((176, 176, 172)) * (0.9 + 0.12 * var[..., None]) * (0.95 + 0.1 * noise[..., None])
    alb = np.where(mort[..., None], col((150, 150, 146)), base)
    h = np.where(mort, -1.0, 0.0) + (noise - 0.5) * 0.4
    h = blur(h.astype(np.float32), 1.0)
    save('C7460Brick', alb, h, np.where(mort, 0.05, 0.25) + noise * 0.05, np.where(mort, 0.7, 1.0), strength=4.0)

floor(); wall(); trim(); ceil(); brick()
