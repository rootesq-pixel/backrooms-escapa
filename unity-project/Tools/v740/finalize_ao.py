# AO crudo (Blender) -> Resources/Elevator/V740/ElevAO.png codificado para el detalle x2 del Standard (espacio gamma): v = (1 - K (1 - ao)) / 2
import sys, numpy as np
from PIL import Image, ImageFilter
src, dst, K = sys.argv[1], sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 0.85
ao = np.clip(np.load(src), 0, 1)
img = Image.fromarray((ao * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(3))
ao = np.asarray(img, np.float32) / 255
m = 1 - K * (1 - ao)
Image.fromarray(np.clip(m / 2 * 255 + 0.5, 0, 255).astype(np.uint8), 'L').save(dst)
print('AO', dst, ao.shape, 'mean mult', round(float(m.mean()), 3))
