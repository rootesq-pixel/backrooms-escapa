"""
Convierte los PNG de 16 bits de bake.py al formato de Unity (Assets/Resources/Textures):
  <Estilo>_a.jpg  albedo (sRGB, JPG q92)
  <Estilo>_n.png  normal tangente (a partir de la altura, con la misma formula que Noise.NormalFromHeight)
  <Estilo>_m.png  mascara Standard: R = metalico, G = oclusion, B = 0, A = suavidad (lineal)
Uso: python3 pack_textures.py <carpeta_bake> <carpeta_destino>
"""
import sys, os
import numpy as np, cv2

STRENGTH = {'WallpaperYellow': 2.2, 'WallpaperHotel': 2.6, 'CarpetBrown': 3.0, 'CarpetRed': 3.0, 'CeilingBeige': 2.5, 'CeilingPool': 2.5,
            'CeilingDark': 2.5, 'ConcreteWall': 2.4, 'ConcreteFloor': 2.0, 'ConcreteCeiling': 2.0, 'PoolWall': 2.8, 'PoolFloor': 2.8,
            'MetalWall': 2.6, 'MetalFloor': 3.2}

def load(path):
    a = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    if a is None: raise IOError(path)
    a = a[..., ::-1] if a.ndim == 3 else a
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

if __name__ == '__main__':
    src, dst = sys.argv[1], sys.argv[2]
    os.makedirs(dst, exist_ok=True)
    total = 0
    for name, st in STRENGTH.items():
        alb = load(os.path.join(src, name + '_albedo.png'))
        data = load(os.path.join(src, name + '_data.png'))
        h, sm, ao = data[..., 0], data[..., 1], data[..., 2]
        mp = os.path.join(src, name + '_metal.png')
        metal = load(mp)[..., 0] if os.path.exists(mp) else np.zeros_like(h)
        wr(os.path.join(dst, name + '_a.jpg'), alb, [cv2.IMWRITE_JPEG_QUALITY, 92])
        wr(os.path.join(dst, name + '_n.png'), normal_from_height(h, st), [cv2.IMWRITE_PNG_COMPRESSION, 9])
        mask = np.stack([metal, ao, np.zeros_like(h), sm], -1)
        wr(os.path.join(dst, name + '_m.png'), mask, [cv2.IMWRITE_PNG_COMPRESSION, 9])
        sz = sum(os.path.getsize(os.path.join(dst, name + s)) for s in ('_a.jpg', '_n.png', '_m.png'))
        total += sz
        print('%-16s %6.0f KB' % (name, sz / 1024.0))
    print('total %.1f MB' % (total / 1048576.0))
