# v7.12.1 Nivel 188: texturas PBR CC0 de ambientCG (1K JPG) para los muebles nuevos de las habitaciones (room_build.py), al
# formato del juego (como pack_textures.py): <Nombre>_a.jpg albedo sRGB, _n.png normal OpenGL, _m.png (R metal, G oclusión, A suavidad)
# Uso: python3 pack_room_textures.py <carpeta con las descargas> <salida Assets/Resources/W188>
import sys, os, glob, numpy as np, cv2
SRC, OUT = sys.argv[1], sys.argv[2]
SETS = {   # nombre: (asset ambientCG, tamaño, tinte sRGB, suavidad extra)
    'W188Sheet':  ('Fabric036', 1024, (1.18, 1.14, 1.06), -0.05),   # sábanas y almohadas (algodón blanco roto)
    'W188Duvet':  ('Fabric008', 1024, (1.00, 1.00, 1.00), 0.00),    # colcha acolchada en rombos (se tiñe por cuarto)
    'W188Uphol':  ('Fabric039', 1024, (2.00, 1.80, 1.55), 0.00),    # tapizado del cabecero, asiento de la silla, tapas del libro
    'W188Shade':  ('Fabric032', 512, (1.00, 0.97, 0.90), 0.00),     # pantallas de las lámparas
    'W188Wood':   ('Wood066', 1024, (1.55, 1.50, 1.45), -0.12),      # muebles de nogal barnizado
    'W188WoodDark': ('Wood051', 512, (1.30, 1.30, 1.30), -0.05),     # marco del cuadro
    'W188Rug':    ('Carpet008', 1024, (1.00, 1.00, 1.00), 0.00),    # campo de la alfombra
    'W188RugBorder': ('Carpet015', 512, (1.00, 1.00, 1.00), 0.00),  # cenefa de la alfombra
}
def find(d, key):
    f = glob.glob(os.path.join(d, '*_' + key + '.jpg')); return f[0] if f else None
for name, (asset, size, tint, sm) in SETS.items():
    d = os.path.join(SRC, asset)
    col = cv2.imread(find(d, 'Color'), cv2.IMREAD_COLOR).astype(np.float32) / 255.0
    col = cv2.resize(col, (size, size), interpolation=cv2.INTER_AREA)
    col = np.clip(col * np.array(tint[::-1], np.float32), 0, 1)
    cv2.imwrite(os.path.join(OUT, name + '_a.jpg'), (col * 255 + 0.5).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 90])
    nrm = cv2.resize(cv2.imread(find(d, 'NormalGL'), cv2.IMREAD_COLOR), (size, size), interpolation=cv2.INTER_AREA)
    cv2.imwrite(os.path.join(OUT, name + '_n.png'), nrm)
    ms = max(256, size // 2)
    rough = cv2.resize(cv2.imread(find(d, 'Roughness'), cv2.IMREAD_GRAYSCALE).astype(np.float32) / 255.0, (ms, ms), interpolation=cv2.INTER_AREA)
    aof = find(d, 'AmbientOcclusion')
    ao = cv2.resize(cv2.imread(aof, cv2.IMREAD_GRAYSCALE).astype(np.float32) / 255.0, (ms, ms)) if aof else np.ones((ms, ms), np.float32)
    m = np.zeros((ms, ms, 4), np.float32)
    m[..., 1] = np.clip(ao, 0, 1); m[..., 3] = np.clip(1.0 - rough + sm, 0, 1)
    cv2.imwrite(os.path.join(OUT, name + '_m.png'), (m * 255 + 0.5).astype(np.uint8))
    print(name, asset, size)
