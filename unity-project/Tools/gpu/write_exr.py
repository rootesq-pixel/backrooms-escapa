# v7.1.1: lm_dn.npy -> EXR half ZIP (Blender). Uso: blender -b --factory-startup -P write_exr.py -- <entrada.npy> <salida.exr> [tamaño]
import bpy, sys, numpy as np
argv = sys.argv[sys.argv.index('--') + 1:]
a = np.load(argv[0]).astype(np.float32)
if len(argv) > 2 and int(argv[2]) != a.shape[0]:
    k = a.shape[0] // int(argv[2]); a = a.reshape(a.shape[0] // k, k, a.shape[1] // k, k, 3).mean((1, 3))   # reducción por cajas
R = a.shape[0]
img = bpy.data.images.new('LM', R, R, float_buffer=True, alpha=False)
px = np.ones((R, R, 4), np.float32); px[:, :, :3] = a; img.pixels.foreach_set(px.ravel())
s = bpy.context.scene.render.image_settings; s.file_format = 'OPEN_EXR'; s.color_depth = '16'; s.exr_codec = 'ZIP'; s.color_mode = 'RGB'
img.save_render(argv[1]); print('EXR ok', R, argv[1])
