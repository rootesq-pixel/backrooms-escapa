"""
Uso:  blender -b -P bake.py -- <carpeta_salida> [estilo ...]
Hornea cada estilo a PNG de 16 bits: <Estilo>_albedo.png (RGB), <Estilo>_data.png (R=altura, G=suavidad, B=AO) y
<Estilo>_metal.png (solo si el estilo es metalico). Despues Tools/blender/pack_textures.py los convierte al formato de Unity.
"""
import sys, os, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import texlib
import styles

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
out = argv[0] if argv else '/workspace/texbake'
names = argv[1:] or list(styles.STYLES.keys())
os.makedirs(out, exist_ok=True)

for name in names:
    fn, W, H, res_a, res_d, bh = styles.STYLES[name]
    t0 = time.time()
    for kind, res in (('albedo', res_a), ('data', res_d), ('metal', res_d)):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        plane = texlib.setup_scene(res)
        g = texlib.G(name + '_' + kind, W, H)
        ch = fn(g)
        if kind == 'albedo':
            sock = g.combine(ch['albedo'])
        elif kind == 'data':
            sock = g.combine_vals(g.clamp(ch['height']), g.clamp(ch['smooth']), g.clamp(ch['ao']))
        else:
            if name not in ('MetalWall', 'MetalFloor'): continue
            sock = g.combine_vals(g.clamp(ch['metal']), 0.0, 0.0)
        texlib.render_channel(g, plane, sock, os.path.join(out, '%s_%s.png' % (name, kind)))
    print('[bake] %s listo en %.1f s' % (name, time.time() - t0), flush=True)
