"""
Convierte los PNG de 16 bits de bake.py al formato de Unity (Assets/Resources/Textures):
  <Estilo>_a.jpg  albedo (sRGB, JPG q92)
  <Estilo>_n.png  normal tangente (a partir de la altura, con la misma formula que Noise.NormalFromHeight)
  <Estilo>_m.png  mascara Standard: R = metalico, G = oclusion, B = 0, A = suavidad (lineal)
Uso: python3 pack_textures.py <carpeta_bake> <carpeta_destino> [carpeta_referencia]
v7.1.1: la fuerza de la normal se escala con la resolución (la altura es la misma: a doble resolución la diferencia entre
píxeles vecinos es la mitad). Con carpeta_referencia (las texturas publicadas antes) se igualan además la inclinación media de
la normal, la oclusión y la suavidad medias y el color medio, para que el horneado en GPU a más resolución se vea igual de
«fuerte» que antes, sólo más nítido.
"""
import sys, os
import numpy as np, cv2

STRENGTH = {'WallpaperYellow': 2.2, 'WallpaperHotel': 2.6, 'CarpetBrown': 3.0, 'CarpetRed': 3.0, 'CeilingBeige': 2.5, 'CeilingPool': 2.5,
            'CeilingDark': 2.5, 'ConcreteWall': 2.4, 'ConcreteFloor': 2.0, 'ConcreteCeiling': 2.0, 'PoolWall': 2.8, 'PoolFloor': 2.8,
            'MetalWall': 2.6, 'MetalFloor': 3.2}

def load(path):
    a = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    if a is None: raise IOError(path)
    if a.ndim == 3: a = a[..., [2, 1, 0, 3]] if a.shape[2] == 4 else a[..., ::-1]   # BGR(A) -> RGB(A) (v7.1.1: antes BGRA quedaba ARGB)
    return a.astype(np.float32) / (65535.0 if a.dtype == np.uint16 else 255.0)

def normal_from_height(h, strength):
    h = np.flipud(h)                                  # fila 0 = v 0 (como en Unity)
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * strength
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * strength
    nx, ny, nz = -dx, -dy, np.ones_like(h)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx * inv, ny * inv, nz * inv], -1) * 0.5 + 0.5
    return np.flipud(n)

def wr(path, rgb, params=None):
    a = (np.clip(rgb, 0, 1) * 255.0 + 0.5).astype(np.uint8)
    if a.ndim == 3 and a.shape[2] >= 3:
        a = a[..., [2, 1, 0] + ([3] if a.shape[2] == 4 else [])]
    cv2.imwrite(path, a, params or [])

def tilt(n):   # inclinación media de una normal empaquetada (0..1)
    v = n * 2.0 - 1.0
    return float(np.sqrt(v[..., 0] ** 2 + v[..., 1] ** 2).mean())

def load_ref(ref, name, suf):
    for ext in ('.png', '.jpg'):
        p = os.path.join(ref, name + suf + ext)
        if os.path.exists(p): return load(p)
    return None

if __name__ == '__main__':
    src, dst = sys.argv[1], sys.argv[2]
    ref = sys.argv[3] if len(sys.argv) > 3 else None
    os.makedirs(dst, exist_ok=True)
    total = 0
    for name, st in STRENGTH.items():
        alb = load(os.path.join(src, name + '_albedo.png'))
        data = load(os.path.join(src, name + '_data.png'))
        h, sm, ao = data[..., 0], data[..., 1], data[..., 2]
        mp = os.path.join(src, name + '_metal.png')
        metal = load(mp)[..., 0] if os.path.exists(mp) else np.zeros_like(h)
        st = st * h.shape[0] / 1024.0                     # v7.1.1: misma pendiente física a cualquier resolución
        info = ''
        if ref:
            ra, rn, rm = load_ref(ref, name, '_a'), load_ref(ref, name, '_n'), load_ref(ref, name, '_m')
            if rn is not None:
                for _ in range(3):                         # igualar la inclinación media de la normal
                    t = tilt(normal_from_height(h, st))
                    if t > 1e-6: st *= tilt(rn) / t
            if ra is not None:
                k = ra[..., :3].astype(np.float64).reshape(-1, 3).mean(0) / np.maximum(alb[..., :3].astype(np.float64).reshape(-1, 3).mean(0), 1e-4)
                alb = np.clip(alb[..., :3] * np.clip(k, 0.8, 1.25), 0, 1)
            if rm is not None and rm.ndim == 3 and rm.shape[2] == 4:
                ao = np.clip(ao * (rm[..., 1].astype(np.float64).mean() / max(ao.astype(np.float64).mean(), 1e-4)), 0, 1)
                sm = np.clip(sm + (rm[..., 3].astype(np.float64).mean() - sm.astype(np.float64).mean()), 0, 1)
            info = ' (fuerza normal %.2f)' % st
        wr(os.path.join(dst, name + '_a.jpg'), alb, [cv2.IMWRITE_JPEG_QUALITY, 92])
        wr(os.path.join(dst, name + '_n.png'), normal_from_height(h, st), [cv2.IMWRITE_PNG_COMPRESSION, 9])
        mask = np.stack([metal, ao, np.zeros_like(h), sm], -1)
        wr(os.path.join(dst, name + '_m.png'), mask, [cv2.IMWRITE_PNG_COMPRESSION, 9])
        sz = sum(os.path.getsize(os.path.join(dst, name + s)) for s in ('_a.jpg', '_n.png', '_m.png'))
        total += sz
        print('%-16s %6.0f KB%s' % (name, sz / 1024.0, info))
    print('total %.1f MB' % (total / 1048576.0))
