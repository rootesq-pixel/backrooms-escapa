# Nivel 7460: texturas de los muebles de las habitaciones (madera, chapa pintada, cartón, tela). Uso: python3 textures_props.py <salida>
import sys, numpy as np
sys.argv = sys.argv[:2]
import textures as T
def wood(n=512):
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    w = T.fbm(n, 3, 4, 0.5, 31, aniso=8.0)
    rings = 0.5 + 0.5 * np.sin((y * 34 + w * 6) * 2 * np.pi)
    fine = T.fbm(n, 60, 2, 0.5, 32, aniso=10.0)
    tone = 0.82 + 0.12 * rings + 0.10 * (fine - 0.5)
    alb = T.col((150, 104, 66))[None, None, :] * tone[..., None]
    T.save('C7460Wood', alb, rings * 0.3 + fine * 0.2, 0.38 + 0.08 * fine, np.ones((n, n)), strength=1.0)
def paint(n=256):
    g = T.fbm(n, 30, 3, 0.5, 41)
    alb = T.col((118, 122, 126))[None, None, :] * (0.97 + 0.06 * (g - 0.5))[..., None]
    T.save('C7460Paint', alb, g * 0.15, 0.55 + 0.05 * g, np.ones((n, n)), strength=0.6)
def card(n=256):
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    flute = 0.5 + 0.5 * np.sin(y * 48 * 2 * np.pi); g = T.fbm(n, 12, 4, 0.5, 51)
    alb = T.col((172, 128, 82))[None, None, :] * (0.9 + 0.1 * g + 0.03 * flute)[..., None]
    tape = (np.abs(x - 0.5) < 0.06).astype(np.float32)
    alb = alb * (1 - tape[..., None]) + T.col((190, 160, 110))[None, None, :] * tape[..., None]
    T.save('C7460Card', alb, flute * 0.1 + g * 0.2, 0.08 + 0.3 * tape, np.ones((n, n)), strength=1.0)
def fabric(n=256):
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    weave = (0.5 + 0.25 * np.sin(x * 128 * 2 * np.pi) + 0.25 * np.sin(y * 128 * 2 * np.pi)); g = T.fbm(n, 20, 3, 0.5, 61)
    alb = T.col((46, 50, 58))[None, None, :] * (0.92 + 0.1 * weave + 0.05 * g)[..., None]
    T.save('C7460Fabric', alb, weave * 0.3, 0.05 + 0 * g, np.ones((n, n)), strength=1.2)
wood(); paint(); card(); fabric()
