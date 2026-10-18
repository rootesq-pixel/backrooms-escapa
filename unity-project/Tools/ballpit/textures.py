# v7.1.0 Parque de bolas: texturas PBR procedurales (numpy, periódicas) -> <Nombre>_a.jpg / _n.png / _m.png
# m = Standard mask: R metálico, G oclusión, B 0, A suavidad. Uso: python3 textures.py <salida> [nombres...]
import sys, os, numpy as np, cv2
OUT = sys.argv[1] if len(sys.argv) > 1 else '/workspace/ballpit/tex'
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(71)

def fbm(n, scale, oct=5, gain=0.5, seed=0):
    """ruido fBm periódico n x n, 'scale' = frecuencia base (ciclos por textura)"""
    r = np.random.default_rng(seed)
    acc = np.zeros((n, n), np.float32); amp = 1.0; tot = 0
    fy = np.fft.fftfreq(n)[:, None] * n; fx = np.fft.fftfreq(n)[None, :] * n
    f = np.sqrt(fx * fx + fy * fy)
    for o in range(oct):
        fr = scale * (2 ** o)
        spec = np.exp(-((f - fr) / (fr * 0.6 + 1e-6)) ** 2) * (f > 0)
        ph = np.fft.ifft2(spec * np.exp(2j * np.pi * r.random((n, n)))).real
        ph /= (ph.std() + 1e-9)
        acc += amp * ph; tot += amp; amp *= gain
    acc /= tot
    return (acc - acc.min()) / (acc.max() - acc.min() + 1e-9)

def blur(a, s): return cv2.GaussianBlur(a, (0, 0), s, borderType=cv2.BORDER_WRAP) if s > 0 else a
def wrapblur(a, s):
    p = int(s * 4) + 2
    b = np.pad(a, ((p, p), (p, p)) + ((0, 0),) * (a.ndim - 2), mode='wrap')
    b = cv2.GaussianBlur(b, (0, 0), s)
    return b[p:-p, p:-p]
def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)
def srgb_to_lin(c): return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)

def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)   # fila 0 arriba (imagen): v crece hacia arriba
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5

def save(name, albedo, height, smooth_, ao, strength=3.0, metal=None, alpha=None):
    a = np.clip(albedo, 0, 1)
    if alpha is None:
        cv2.imwrite(os.path.join(OUT, name + '_a.jpg'), (a[..., ::-1] * 255 + 0.5).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 92])
    else:
        rgba = np.dstack([a, np.clip(alpha, 0, 1)])
        cv2.imwrite(os.path.join(OUT, name + '_a.png'), (rgba[..., [2, 1, 0, 3]] * 255 + 0.5).astype(np.uint8))
    n = normal_from_height(height.astype(np.float32), strength)
    cv2.imwrite(os.path.join(OUT, name + '_n.png'), (n[..., ::-1] * 255 + 0.5).astype(np.uint8))
    m = np.zeros(a.shape[:2] + (4,), np.float32)
    m[..., 0] = 0 if metal is None else metal; m[..., 1] = np.clip(ao, 0, 1); m[..., 3] = np.clip(smooth_, 0, 1)
    cv2.imwrite(os.path.join(OUT, name + '_m.png'), (m[..., [2, 1, 0, 3]] * 255 + 0.5).astype(np.uint8))
    print('tex', name, a.shape, flush=True)

def coords(n): 
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    return x, y

# ---------------------------------------------------------------------------------------------
# Suelo de goma EVA: losetas de 0,6 m con borde de puzle, 2,4 m por textura (4x4), colores desvaídos, polvo y rozaduras
# ---------------------------------------------------------------------------------------------
def play_floor(n=2048):
    x, y = coords(n); T = 4
    u, v = x * T, y * T
    iu, iv = np.floor(u).astype(int), np.floor(v).astype(int)
    fu, fv = u - iu, v - iv
    # dientes de puzle: el borde se desplaza con una onda cuadrada suavizada a lo largo de cada lado
    # dientes de puzle: el borde vertical se desplaza en u con una onda suavizada (continuo), igual el horizontal en v
    def teeth(t): return 0.03 * np.tanh(5 * np.sin(2 * np.pi * 5 * t))
    us = u + teeth(v) * np.exp(-((fu - np.round(fu)) / 0.05) ** 2)
    vs = v + teeth(u) * np.exp(-((fv - np.round(fv)) / 0.05) ** 2)
    cu = np.floor(us).astype(int) % T; cv_ = np.floor(vs).astype(int) % T
    pal = np.array([[0.62, 0.20, 0.17], [0.20, 0.36, 0.58], [0.25, 0.48, 0.28], [0.80, 0.63, 0.20], [0.55, 0.30, 0.52], [0.86, 0.45, 0.20]], np.float32)
    rr = np.random.default_rng(5); ids = rr.integers(0, len(pal), (T, T))
    for i in range(T):  # sin dos vecinos iguales
        for j in range(T):
            while (i > 0 and ids[i, j] == ids[i - 1, j]) or (j > 0 and ids[i, j] == ids[i, j - 1]): ids[i, j] = rr.integers(0, len(pal))
    col = pal[ids[cv_, cu]]
    # desvaído (sol, años): hacia gris cálido
    fade = 0.50 + 0.18 * fbm(n, 3, seed=1)[..., None]
    col = col * (1 - fade) + np.array([0.56, 0.53, 0.48]) * fade
    # junta: distancia al borde efectivo
    eu = np.abs((us % 1.0) - 0.5); ev = np.abs((vs % 1.0) - 0.5)
    ed = 0.5 - np.maximum(eu, ev)
    joint = 1 - smooth(0.002, 0.009, ed)
    # textura de la goma: grano fino tipo piel de naranja + rombos muy suaves del molde
    grain = fbm(n, 220, oct=3, seed=2)
    dia = 0.5 + 0.5 * np.sin(2 * np.pi * (u + v) * 18) * np.sin(2 * np.pi * (u - v) * 18)
    dust = fbm(n, 4, seed=3); dirt = smooth(0.62, 0.95, fbm(n, 9, seed=4)) * 0.5
    scuff = smooth(0.78, 0.95, fbm(n, 60, oct=3, seed=6)) * smooth(0.4, 0.7, fbm(n, 3, seed=7)) * 0.6
    col = col * (0.90 + 0.12 * grain[..., None])
    col = col * (1 - 0.55 * joint[..., None])
    col = col * (1 - 0.25 * dirt[..., None]) + 0.10 * dirt[..., None] * np.array([0.35, 0.30, 0.24])
    col = col * (1 - 0.18 * scuff[..., None]) + 0.18 * scuff[..., None] * np.array([0.22, 0.21, 0.20])
    col = col + 0.06 * smooth(0.5, 0.9, dust)[..., None] * np.array([0.75, 0.72, 0.66])
    height = 0.6 * (1 - joint) + 0.03 * grain + 0.02 * dia - 0.02 * scuff
    sm = 0.28 + 0.10 * grain - 0.15 * smooth(0.5, 0.9, dust) + 0.1 * scuff
    ao = 1 - 0.5 * joint - 0.15 * dirt
    save('PlayFloor', col, height, sm, ao, strength=4.0)

# ---------------------------------------------------------------------------------------------
# Vinilo acolchado (gris claro, se tiñe por material): grano de piel sintética, costuras, arrugas y rozaduras.
# Una textura de 1,2 x 1,2 m con una costura central horizontal y otra vertical (cojines de 0,6 m).
# ---------------------------------------------------------------------------------------------
def pad_vinyl(n=1024):
    x, y = coords(n)
    grain = fbm(n, 160, oct=3, seed=11)
    wr = fbm(n, 6, oct=4, seed=12)
    # cojines: cúpula suave por cojín de 0,6 m (2x2 por textura)
    fu = (x * 2) % 1.0; fv = (y * 2) % 1.0
    dome = np.clip(np.sin(np.pi * fu), 0, 1) ** 0.35 * np.clip(np.sin(np.pi * fv), 0, 1) ** 0.35
    seam = (1 - smooth(0.004, 0.02, np.minimum(np.minimum(fu, 1 - fu), np.minimum(fv, 1 - fv))))
    # pespunte: puntos a 1 cm del borde
    st_d = np.abs(np.minimum(np.minimum(fu, 1 - fu), np.minimum(fv, 1 - fv)) - 0.03)
    along = np.where(np.minimum(fu, 1 - fu) < np.minimum(fv, 1 - fv), fv, fu)
    stitch = (1 - smooth(0.002, 0.005, st_d)) * (np.sin(2 * np.pi * along * 60) > 0.2)
    crease = smooth(0.82, 0.95, fbm(n, 14, oct=4, seed=13)) * 0.6
    scuff = smooth(0.80, 0.95, fbm(n, 60, oct=3, seed=14)) * 0.7
    stain = smooth(0.72, 0.9, fbm(n, 6, seed=15)) * 0.6
    base = 0.80 + 0.06 * (wr - 0.5) + 0.03 * grain
    col = np.dstack([base] * 3)
    col *= (1 - 0.45 * seam - 0.25 * stitch - 0.10 * crease)[..., None]
    col = col * (1 - 0.30 * scuff[..., None]) + 0.30 * scuff[..., None] * np.array([0.55, 0.53, 0.50])
    col = col * (1 - 0.25 * stain[..., None]) + 0.25 * stain[..., None] * np.array([0.62, 0.55, 0.43])
    height = 0.55 * dome + 0.02 * grain - 0.25 * seam - 0.05 * crease - 0.04 * stitch
    sm = 0.50 + 0.12 * grain - 0.25 * scuff - 0.2 * stain
    ao = 1 - 0.55 * seam - 0.1 * crease
    save('PadVinyl', col, height, sm, ao, strength=3.0)

# ---------------------------------------------------------------------------------------------
# Pared: zócalo acolchado de 1,2 m (cojines verticales de colores desvaídos) + pintura plástica con mural infantil
# casi borrado (nubes, globos, estrellas) y suciedad. Textura 4,8 m x 4,8 m (la pared de la sala mide 4,6 m).
# ---------------------------------------------------------------------------------------------
def play_wall(n=2048):
    x, y = coords(n)   # y=0 arriba en la imagen -> altura h = (1 - y) * 4.8
    W = 4.8; hgt = (1 - y) * W; xm = x * W
    paint = np.array([0.80, 0.77, 0.66], np.float32)
    col = np.ones((n, n, 3), np.float32) * paint
    mott = fbm(n, 5, seed=21); col *= (0.93 + 0.1 * (mott - 0.5))[..., None]
    height = 0.5 + 0.02 * fbm(n, 300, oct=2, seed=22)
    ao = np.ones((n, n), np.float32); sm = np.ones((n, n), np.float32) * 0.22
    # franja de mural (1,4 - 4,2 m): cielo azul desvaído con nubes, globos y estrellas, desconchado
    band = smooth(1.40, 1.46, hgt) * (1 - smooth(4.10, 4.16, hgt))
    sky = np.array([0.56, 0.70, 0.78])
    col = col * (1 - 0.75 * band[..., None]) + 0.75 * band[..., None] * sky * (0.92 + 0.12 * mott[..., None])
    # nubes
    cl = smooth(0.62, 0.70, fbm(n, 3, oct=3, seed=23) * 0.7 + 0.3 * smooth(3.2, 3.9, hgt))
    col = col * (1 - 0.7 * (cl * band)[..., None]) + 0.7 * (cl * band)[..., None] * np.array([0.88, 0.87, 0.82])
    r = np.random.default_rng(24)
    shapes = np.zeros((n, n), np.float32); shcol = np.zeros((n, n, 3), np.float32)
    palette = [np.array(c) for c in ([0.78, 0.28, 0.22], [0.86, 0.70, 0.25], [0.30, 0.55, 0.35], [0.70, 0.38, 0.62], [0.90, 0.50, 0.22])]
    for k in range(9):   # globos con cordel
        cx, cy = r.uniform(0.2, 4.6), r.uniform(2.2, 3.7); rad = r.uniform(0.16, 0.26)
        for ox in (-W, 0, W):
            dx = (xm - cx - ox) / rad; dy = (hgt - cy) / (rad * 1.25)
            d = dx * dx + dy * dy
            m = 1 - smooth(0.92, 1.0, d)
            c = palette[k % len(palette)]
            shcol = shcol * (1 - m[..., None]) + m[..., None] * c * (0.85 + 0.25 * np.clip(-dx * 0.4 - dy * 0.5 + 0.5, 0, 1))[..., None]
            shapes = np.maximum(shapes, m)
            line = (1 - smooth(0.004, 0.009, np.abs(xm - cx - ox - 0.05 * np.sin((hgt - cy) * 9)))) * (hgt < cy - rad * 1.2) * (hgt > cy - rad * 1.2 - 0.7)
            shcol = shcol * (1 - line[..., None]) + line[..., None] * np.array([0.35, 0.33, 0.30]); shapes = np.maximum(shapes, line)
    for k in range(14):  # estrellas
        cx, cy = r.uniform(0, 4.8), r.uniform(1.6, 4.0); rad = r.uniform(0.07, 0.14)
        for ox in (-W, 0, W):
            dx = xm - cx - ox; dy = hgt - cy
            ang = np.arctan2(dy, dx); rr_ = np.sqrt(dx * dx + dy * dy)
            lim = rad * (0.55 + 0.45 * np.cos(5 * ang)) ** 1.0
            m = 1 - smooth(lim * 0.9, lim, rr_)
            shcol = shcol * (1 - m[..., None]) + m[..., None] * np.array([0.92, 0.80, 0.35]); shapes = np.maximum(shapes, m)
    wear = fbm(n, 12, oct=5, seed=25)
    peel = smooth(0.58, 0.62, wear) * smooth(0.3, 0.6, fbm(n, 3, seed=26))
    shapes *= (1 - peel) * band
    col = col * (1 - 0.85 * shapes[..., None]) + 0.85 * shapes[..., None] * shcol
    # desconchones: se ve el yeso blanco y el borde algo más oscuro
    chip = smooth(0.70, 0.73, wear) * smooth(0.5, 0.8, fbm(n, 4, seed=27))
    col = col * (1 - chip[..., None]) + chip[..., None] * np.array([0.86, 0.84, 0.80])
    height = height - 0.12 * chip
    # zócalo acolchado 0 - 1,2 m: cojines verticales de 0,6 m, colores alternos desvaídos
    zoc = 1 - smooth(1.19, 1.21, hgt)
    pu = (xm / 0.6) % 1.0; pi_ = np.floor(xm / 0.6).astype(int) % 8
    zpal = np.array([[0.62, 0.20, 0.18], [0.82, 0.64, 0.22], [0.20, 0.38, 0.62], [0.26, 0.50, 0.30]])
    zc = zpal[pi_ % 4] * (0.95 + 0.1 * mott[..., None])
    fade = 0.38 + 0.2 * fbm(n, 4, seed=28)[..., None]
    zc = zc * (1 - fade) + np.array([0.58, 0.55, 0.50]) * fade
    dome = np.clip(np.sin(np.pi * pu), 0, 1) ** 0.3 * np.clip(np.sin(np.pi * np.clip(hgt / 1.2, 0, 1)), 0, 1) ** 0.25
    seam = 1 - smooth(0.004, 0.02, np.minimum(pu, 1 - pu))
    scuff = smooth(0.72, 0.9, fbm(n, 30, oct=4, seed=29)) * (hgt < 0.7)
    zc = zc * (1 - 0.4 * seam[..., None]) * (0.85 + 0.15 * dome[..., None])
    zc = zc * (1 - 0.3 * scuff[..., None]) + 0.3 * scuff[..., None] * np.array([0.3, 0.29, 0.27])
    col = col * (1 - zoc[..., None]) + zoc[..., None] * zc
    height = height * (1 - zoc) + zoc * (0.5 + 0.45 * dome - 0.3 * seam)
    sm = sm * (1 - zoc) + zoc * (0.45 - 0.2 * scuff)
    ao = ao * (1 - zoc) + zoc * (1 - 0.5 * seam)
    # remate (perfil de goma) en 1,2 m y rodapié
    cap = (1 - smooth(0.0, 0.025, np.abs(hgt - 1.215)))
    col = col * (1 - cap[..., None]) + cap[..., None] * np.array([0.25, 0.25, 0.26]); height += 0.25 * cap
    # suciedad de manos a 0,6 - 1,6 m y chorretones desde arriba
    hands = smooth(0.6, 0.85, fbm(n, 10, seed=30)) * smooth(0.5, 0.9, hgt) * (1 - smooth(1.4, 1.9, hgt))
    drip = smooth(0.75, 0.85, fbm(n, 40, oct=2, seed=31) * 0.5 + 0.5 * np.repeat(fbm(n, 30, seed=32)[:1], n, 0)) * smooth(2.5, 4.8, hgt)
    col *= (1 - 0.15 * hands - 0.2 * drip)[..., None]
    save('PlayWall', col, height, sm, ao, strength=3.0)

# ---------------------------------------------------------------------------------------------
# Plástico rotomoldeado (tobogán, tubos): blanco roto (se tiñe), arañazos finos, opaco con brillo medio
# ---------------------------------------------------------------------------------------------
def plastic(n=1024):
    x, y = coords(n)
    g = fbm(n, 120, oct=3, seed=41)
    scr = np.zeros((n, n), np.float32)
    r = np.random.default_rng(42)
    img = np.zeros((n, n), np.uint8)
    for k in range(260):
        x0, y0 = r.integers(0, n, 2); a = r.uniform(0, np.pi); L = r.uniform(10, 120)
        x1, y1 = int(x0 + np.cos(a) * L), int(y0 + np.sin(a) * L)
        cv2.line(img, (int(x0), int(y0)), (x1, y1), int(r.integers(60, 200)), 1, cv2.LINE_AA)
    scr = img.astype(np.float32) / 255
    grime = smooth(0.6, 0.85, fbm(n, 5, seed=43))
    col = np.dstack([0.86 + 0.03 * g] * 3) * (1 - 0.18 * grime[..., None]) + 0.18 * grime[..., None] * np.array([0.55, 0.50, 0.42])
    col = col * (1 + 0.06 * scr[..., None])
    sm = 0.62 - 0.3 * scr - 0.25 * grime
    save('PlayPlastic', col, 0.5 + 0.02 * g - 0.05 * scr, sm, 1 - 0.1 * grime, strength=2.0)

# ---------------------------------------------------------------------------------------------
# Red de seguridad: cuerda trenzada en rombo de 10 cm (alfa recortado), 1 m x 1 m
# ---------------------------------------------------------------------------------------------
def net(n=1024):
    x, y = coords(n)
    k = 10
    a = (x + y) * k; b = (x - y) * k
    da = np.abs((a % 1.0) - 0.5); db = np.abs((b % 1.0) - 0.5)
    th = 0.5 - 0.085
    ra = smooth(th - 0.012, th + 0.004, da); rb = smooth(th - 0.012, th + 0.004, db)
    rope = np.maximum(ra, rb)
    twist = 0.5 + 0.5 * np.sin(2 * np.pi * (np.where(ra > rb, b, a)) * 9)
    knot = ra * rb
    col = np.dstack([0.16 + 0.05 * twist, 0.17 + 0.05 * twist, 0.18 + 0.05 * twist]) * (1 + 0.6 * knot[..., None])
    alpha = smooth(0.25, 0.6, rope)
    save('PlayNet', col, rope * 0.6 + 0.1 * twist * rope, 0.25 * rope, 1 - 0.3 * knot, strength=2.0, alpha=alpha)

# ---------------------------------------------------------------------------------------------
# Bolas (solo para el horneado): superficie de bolas vista de lejos (puntos de colores con sombra entre bolas)
# ---------------------------------------------------------------------------------------------
def ball_surface(n=1024):
    x, y = coords(n)
    k = 1.0 / 0.075   # bolas por metro, textura de 1 m
    pal = np.array([[0.70, 0.12, 0.10], [0.85, 0.70, 0.12], [0.12, 0.32, 0.65], [0.15, 0.50, 0.22], [0.75, 0.75, 0.72]])
    r = np.random.default_rng(51)
    col = np.zeros((n, n, 3), np.float32); hgt = np.zeros((n, n), np.float32)
    for layer in range(2):
        jx, jy = r.random(2)
        u = (x * k + jx + layer * 0.5); v = (y * k * 1.15 + jy + layer * 0.5)
        iu, iv = np.floor(u).astype(int), np.floor(v).astype(int)
        fu, fv = u - iu - 0.5, v - iv - 0.5
        d = np.sqrt(fu * fu + fv * fv) * 2
        m = (1 - smooth(0.85, 0.98, d)) * (hgt < 0.01 + layer)
        idx = ((iu * 73856093) ^ (iv * 19349663) ^ (layer * 83492791)) % len(pal)
        shade = np.sqrt(np.clip(1 - d * d, 0, 1))
        c = pal[idx] * (0.45 + 0.55 * shade)[..., None]
        col = col * (1 - m[..., None]) + c * m[..., None]; hgt = np.maximum(hgt, m * shade)
    fade = 0.35
    col = col * (1 - fade) + fade * np.array([0.55, 0.52, 0.48]) * (col.sum(-1, keepdims=True) > 0)
    save('BallSurface', col, hgt, 0.6 * hgt, 0.4 + 0.6 * hgt, strength=3.0)

names = sys.argv[2:] or ['play_floor', 'pad_vinyl', 'play_wall', 'plastic', 'net', 'ball_surface']
for nm in names: globals()[nm]()
