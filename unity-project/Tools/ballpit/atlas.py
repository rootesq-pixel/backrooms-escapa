# v7.1.0: atlas de carteles y atrezo del parque de bolas (menú de meriendas, póster de cumpleaños, normas, banderines,
# mantel, dibujo infantil, rótulo, muestras de color). Salida: tex/PartyAtlas_a.png (RGBA, alfa = recorte de banderines)
# y tex/atlas.json con las regiones UV (u0, v0, u1, v1; v hacia arriba como Blender/Unity).
import json, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
W, H = 2048, 1024
OUT = '/workspace/ballpit/tex/'
F = '/usr/share/fonts/truetype/dejavu/'
def font(sz, bold=True, serif=False):
    return ImageFont.truetype(F + ('DejaVuSerif' if serif else 'DejaVuSans') + ('-Bold' if bold else '') + '.ttf', sz)
im = Image.new('RGBA', (W, H), (200, 200, 200, 255)); d = ImageDraw.Draw(im)
R = {}
def reg(name, x0, y0, x1, y1): R[name] = [x0 / W, 1 - y1 / H, x1 / W, 1 - y0 / H]; return (x0, y0, x1, y1)
rnd = random.Random(5)

# menú de meriendas (pizarra impresa, letras de plástico)
x0, y0, x1, y1 = reg('menu', 0, 0, 1024, 512)
d.rectangle((x0, y0, x1, y1), fill=(34, 52, 46, 255)); d.rectangle((x0 + 10, y0 + 10, x1 - 10, y1 - 10), outline=(196, 160, 60, 255), width=8)
d.text((512, 64), 'MERIENDAS', font=font(78), fill=(240, 196, 70, 255), anchor='mm')
items = [('Palomitas', '1,50'), ('Perrito caliente', '2,20'), ('Batido de fresa', '1,80'), ('Zumo', '1,00'), ('Gofre con nata', '2,50'), ('Tarta de cumpleaños', '--'), ('Agua', '0,80')]
for i, (n, p) in enumerate(items):
    y = 150 + i * 48
    d.text((70, y), n, font=font(36, False), fill=(232, 232, 220, 255), anchor='lm')
    d.text((950, y), p + (' €' if p != '--' else ''), font=font(36), fill=(250, 220, 120, 255), anchor='rm')
    tl = 70 + font(36, False).getlength(n) + 16
    for k in range(int(max(380, tl)), 800, 18): d.ellipse((k, y + 2, k + 4, y + 6), fill=(140, 150, 140, 255))
d.text((512, 488), 'Horario: 10:00 - 20:00   ·   Cerrado por reformas', font=font(24, False), fill=(190, 190, 180, 255), anchor='mm')

# póster de fiestas de cumpleaños
x0, y0, x1, y1 = reg('poster', 1024, 0, 1408, 512)
d.rectangle((x0, y0, x1, y1), fill=(246, 230, 120, 255))
for k in range(9):
    cx, cy = x0 + rnd.randint(30, 354), y0 + rnd.randint(170, 330); c = rnd.choice([(220, 60, 50), (60, 110, 210), (60, 170, 80), (230, 120, 40), (170, 80, 190)])
    d.line((cx, cy + 34, cx + rnd.randint(-20, 20), y1 - 90), fill=(90, 90, 90, 255), width=2); d.ellipse((cx - 26, cy - 34, cx + 26, cy + 34), fill=c + (255,))
d.text((x0 + 192, y0 + 70), '¡CELEBRA', font=font(52), fill=(200, 40, 50, 255), anchor='mm')
d.text((x0 + 192, y0 + 125), 'TU CUMPLE!', font=font(52), fill=(200, 40, 50, 255), anchor='mm')
d.text((x0 + 192, y1 - 70), 'Sala de fiestas', font=font(30), fill=(40, 60, 140, 255), anchor='mm')
d.text((x0 + 192, y1 - 32), 'Pregunta en el mostrador', font=font(20, False), fill=(40, 40, 40, 255), anchor='mm')

# normas del parque
x0, y0, x1, y1 = reg('rules', 1408, 0, 1792, 512)
d.rectangle((x0, y0, x1, y1), fill=(236, 236, 228, 255)); d.rectangle((x0, y0, x1, y0 + 80), fill=(40, 90, 180, 255))
d.text((x0 + 192, y0 + 40), 'NORMAS', font=font(46), fill=(255, 255, 255, 255), anchor='mm')
rules = ['Calcetines obligatorios', 'Prohibido comer', 'en la piscina de bolas', 'Niños de 3 a 12 años', 'No subir por el tobogán', 'No te quedes dentro', 'después del cierre', 'Cuenta a tus amigos', 'al salir']
for i, t in enumerate(rules):
    d.text((x0 + 26, y0 + 112 + i * 42), ('• ' if i in (0, 1, 3, 4, 5, 7) else '  ') + t, font=font(25, False), fill=(30, 30, 30, 255), anchor='lm')

# dibujo infantil (ceras): la piscina de bolas con algo dentro
x0, y0, x1, y1 = reg('drawing', 1792, 0, 2048, 512)
d.rectangle((x0, y0, x1, y1), fill=(250, 248, 240, 255))
for k in range(220):
    cx, cy = x0 + rnd.randint(20, 236), y0 + rnd.randint(300, 470); c = rnd.choice([(220, 50, 40), (50, 90, 200), (240, 200, 40), (60, 160, 70)])
    d.ellipse((cx - 7, cy - 7, cx + 7, cy + 7), outline=c + (255,), width=3)
d.rectangle((x0 + 14, y0 + 290, x1 - 14, y0 + 480), outline=(200, 40, 40, 255), width=6)
d.ellipse((x0 + 100, y0 + 255, x0 + 156, y0 + 315), fill=(20, 20, 20, 255)); d.ellipse((x0 + 112, y0 + 272, x0 + 122, y0 + 282), fill=(255, 255, 255, 255)); d.ellipse((x0 + 134, y0 + 272, x0 + 144, y0 + 282), fill=(255, 255, 255, 255))
for a in (-1, 1): d.line((x0 + 128 + a * 24, y0 + 300, x0 + 128 + a * 70, y0 + 230), fill=(20, 20, 20, 255), width=6)
d.text((x0 + 128, y0 + 60), 'MI AMIGO', font=font(34), fill=(60, 60, 200, 255), anchor='mm')
d.text((x0 + 128, y0 + 105), 'DE ABAJO', font=font(34), fill=(60, 60, 200, 255), anchor='mm')
d.text((x0 + 128, y0 + 150), 'lucía 6 años', font=font(20, False), fill=(120, 120, 120, 255), anchor='mm')

# banderines (alfa recortado): 16 de 128 px, "FELIZ CUMPLE" + adornos
letters = 'FELIZ*CUMPLE****'
cols = [(220, 60, 50), (240, 190, 50), (60, 120, 210), (70, 170, 90)]
x0, y0, x1, y1 = reg('pennants', 0, 512, 1024, 768)
d.rectangle((x0, y0, x1, y1), fill=(0, 0, 0, 0))
for i, ch in enumerate(letters):
    px, py = (i % 8) * 128, 512 + (i // 8) * 128
    c = cols[i % 4]
    d.polygon([(px + 4, py + 4), (px + 124, py + 4), (px + 64, py + 124)], fill=c + (255,))
    if ch != '*': d.text((px + 64, py + 44), ch, font=font(56), fill=(255, 255, 255, 255), anchor='mm')
for i in range(16): R['pen%d' % i] = [(i % 8) * 128 / W, 1 - (512 + (i // 8) * 128 + 128) / H, ((i % 8) * 128 + 128) / W, 1 - (512 + (i // 8) * 128) / H]

# rótulo de la sala
x0, y0, x1, y1 = reg('sign', 0, 768, 1024, 1024)
d.rectangle((x0, y0, x1, y1), fill=(230, 70, 60, 255)); d.rectangle((x0 + 12, y0 + 12, x1 - 12, y1 - 12), outline=(255, 230, 120, 255), width=10)
txt = 'PARQUE DE BOLAS'; f88 = font(88); xx = 512 - f88.getlength(txt) / 2
for i, ch in enumerate(txt):
    c = [(255, 230, 90), (120, 200, 255), (255, 255, 255), (160, 240, 140)][i % 4]
    d.text((xx, 896 + (8 if i % 2 else -8)), ch, font=f88, fill=c + (255,), anchor='lm'); xx += f88.getlength(ch)

# mantel de papel (cuadros y confeti)
x0, y0, x1, y1 = reg('cloth', 1024, 512, 1536, 1024)
for i in range(8):
    for j in range(8):
        c = (236, 236, 240) if (i + j) % 2 else (210, 90, 110)
        d.rectangle((x0 + i * 64, y0 + j * 64, x0 + i * 64 + 64, y0 + j * 64 + 64), fill=c + (255,))
for k in range(160):
    cx, cy = x0 + rnd.randint(0, 511), y0 + rnd.randint(0, 511)
    d.ellipse((cx - 4, cy - 4, cx + 4, cy + 4), fill=rnd.choice(cols) + (255,))

# muestras (4x4 de 128 px)
SW = ['plate', 'frosting', 'sponge', 'hatA', 'hatB', 'cardboard', 'steel', 'popcorn', 'cupred', 'napkin', 'register', 'balloonR', 'balloonB', 'balloonY', 'balloonG', 'stool']
SC = {'plate': (238, 238, 232), 'frosting': (238, 196, 206), 'sponge': (214, 170, 110), 'cardboard': (176, 140, 96), 'steel': (170, 172, 176), 'popcorn': (240, 214, 140),
      'cupred': (200, 50, 45), 'napkin': (244, 244, 240), 'register': (70, 72, 78), 'balloonR': (200, 40, 40), 'balloonB': (40, 80, 190), 'balloonY': (230, 190, 40), 'balloonG': (50, 150, 70), 'stool': (190, 40, 40)}
for i, n in enumerate(SW):
    px, py = 1536 + (i % 4) * 128, 512 + (i // 4) * 128
    if n == 'hatA':
        for k in range(8): d.rectangle((px, py + k * 16, px + 128, py + k * 16 + 16), fill=((60, 120, 210) if k % 2 else (240, 200, 60)) + (255,))
    elif n == 'hatB':
        d.rectangle((px, py, px + 128, py + 128), fill=(220, 70, 120, 255))
        for k in range(14): cx, cy = px + rnd.randint(8, 120), py + rnd.randint(8, 120); d.ellipse((cx - 6, cy - 6, cx + 6, cy + 6), fill=(255, 255, 255, 255))
    elif n == 'popcorn':
        d.rectangle((px, py, px + 128, py + 128), fill=(200, 160, 80, 255))
        for k in range(90): cx, cy = px + rnd.randint(4, 124), py + rnd.randint(4, 124); d.ellipse((cx - 7, cy - 6, cx + 7, cy + 6), fill=(250, 236, 190, 255))
    else: d.rectangle((px, py, px + 128, py + 128), fill=SC[n] + (255,))
    R['sw_' + n] = [(px + 32) / W, 1 - (py + 96) / H, (px + 96) / W, 1 - (py + 32) / H]

# envejecido: desaturar algo, manchas y polvo (no toca el alfa)
a = np.asarray(im).astype(np.float32) / 255
rgb = a[..., :3]; lum = rgb.mean(2, keepdims=True); rgb = rgb * 0.82 + lum * 0.18
def blob(s, seed):
    r = np.random.default_rng(seed).random((H // s + 2, W // s + 2)).astype(np.float32)
    return np.asarray(Image.fromarray((r * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC)).astype(np.float32) / 255
g = 0.86 + 0.14 * blob(64, 1) * blob(16, 2) + 0.04 * blob(4, 3)
rgb = rgb * np.clip(g, 0.7, 1.05)[..., None] * 0.92 + 0.03
a[..., :3] = np.clip(rgb, 0, 1)
Image.fromarray((a * 255).astype(np.uint8), 'RGBA').save(OUT + 'PartyAtlas_a.png')
json.dump(R, open(OUT + 'atlas.json', 'w'), indent=0)
Image.fromarray((a[..., :3] * 255).astype(np.uint8)).resize((1024, 512)).save('/workspace/ballpit/atlas_prev.jpg')
print('atlas ok', len(R))
