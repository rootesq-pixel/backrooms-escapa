# v7.4.0 PC pass: same packing as packtex.py, from the ambientCG 2K sources, at higher resolution
import packtex
packtex.C = '/workspace/v740/cc0'
P = lambda id, *a, **k: packtex.pack(id + '_2K', *a, odir='tex_hi', **k)
P('Metal009','ElevSteel',1024, metal=0.55, smooth_mul=0.9)
P('Metal038','ElevSteelDark',1024, metal=0.6, smooth_mul=0.8)
P('Metal009','ElevPaint',1024, metal=0.08, smooth_mul=0.4, tint=(0.80,0.80,0.76))
P('Wood051','ElevWood',2048, metal=0.0, smooth_mul=0.55, smooth_add=0.0)
P('Rubber001','ElevFloor',1024, metal=0.0)
