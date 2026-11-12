# Nivel 188 (v7.12.0): empaqueta texturas PBR CC0 de ambientCG (2K JPG) al formato del juego:
#   <Nombre>_a.jpg (albedo sRGB), <Nombre>_n.png (normal OpenGL), <Nombre>_m.png (R metálico, G oclusión, B 0, A suavidad)
# Uso: python3 pack_textures.py <carpeta con las descargas> <salida Assets/Resources/W188>
import sys, os, glob, numpy as np, cv2
SRC, OUT = sys.argv[1], sys.argv[2]
os.makedirs(OUT, exist_ok=True)
# nombre en el juego: (asset ambientCG, tamaño, tinte multiplicativo sRGB, suavidad extra, metal)
SETS = {
    'W188Facade':  ('PaintedPlaster017', 2048, (1.00, 0.95, 0.86), 0.00, 0.0),   # revoco pintado crema (fachadas del patio)
    'W188Paving':  ('PavingStones136',   2048, (0.92, 0.92, 0.90), 0.00, 0.0),   # losas grandes del patio
    'W188Carpet':  ('Carpet012',         2048, (1.00, 1.00, 1.00), 0.00, 0.0),   # moqueta azul marino de los pasillos
    'W188RoomCarpet': ('Carpet016',      1024, (1.00, 1.00, 1.00), 0.00, 0.0),   # moqueta beige de las habitaciones
    'W188Wall':    ('Wallpaper001A',     1024, (0.97, 0.94, 0.88), 0.00, 0.0),   # papel pintado blanco roto (pasillos)
    'W188Curtain': ('Fabric062',         1024, (1.00, 0.97, 0.92), 0.00, 0.0),   # tela de las cortinas
    'W188Concrete':('Concrete034',       1024, (1.00, 1.00, 1.00), 0.00, 0.0),   # escalera, techos de servicio
    'W188Door':    ('PaintedWood005',    1024, (1.00, 1.00, 1.00), 0.10, 0.0),   # puertas pintadas
}
def find(d, key):
    f = glob.glob(os.path.join(d, '*_' + key + '.jpg')); return f[0] if f else None
for name, (asset, size, tint, smooth_add, metal) in SETS.items():
    d = os.path.join(SRC, asset)
    col = cv2.imread(find(d, 'Color'), cv2.IMREAD_COLOR).astype(np.float32) / 255.0
    col = cv2.resize(col, (size, size), interpolation=cv2.INTER_AREA)
    col = np.clip(col * np.array(tint[::-1], np.float32), 0, 1)
    cv2.imwrite(os.path.join(OUT, name + '_a.jpg'), (col * 255 + 0.5).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 92])
    nrm = cv2.imread(find(d, 'NormalGL'), cv2.IMREAD_COLOR)
    nrm = cv2.resize(nrm, (size, size), interpolation=cv2.INTER_AREA)
    cv2.imwrite(os.path.join(OUT, name + '_n.png'), nrm)
    ms = max(512, size // 2)
    rough = cv2.imread(find(d, 'Roughness'), cv2.IMREAD_GRAYSCALE).astype(np.float32) / 255.0
    rough = cv2.resize(rough, (ms, ms), interpolation=cv2.INTER_AREA)
    aof = find(d, 'AmbientOcclusion')
    ao = cv2.resize(cv2.imread(aof, cv2.IMREAD_GRAYSCALE).astype(np.float32) / 255.0, (ms, ms)) if aof else np.ones((ms, ms), np.float32)
    m = np.zeros((ms, ms, 4), np.float32)
    m[..., 0] = 0                       # B (cv2 BGRA): 0
    m[..., 1] = np.clip(ao, 0, 1)       # G: oclusión
    m[..., 2] = metal                   # R: metálico
    m[..., 3] = np.clip(1.0 - rough + smooth_add, 0, 1)   # A: suavidad
    cv2.imwrite(os.path.join(OUT, name + '_m.png'), (m * 255 + 0.5).astype(np.uint8))
    print(name, asset, size)
