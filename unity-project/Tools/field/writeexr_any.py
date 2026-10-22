# uso: blender -b --python writeexr_any.py -- <in.npy HxWx3> <out.exr>   (EXR half ZIP, cualquier tamaño)
import bpy, sys, numpy as np
src,dst=sys.argv[sys.argv.index('--')+1:][:2]
a=np.load(src); H,W=a.shape[:2]
img=bpy.data.images.new('LM',W,H,float_buffer=True,alpha=False)
px=np.ones((H,W,4),np.float32); px[:,:,:3]=a; img.pixels.foreach_set(px.ravel())
s=bpy.context.scene.render.image_settings; s.file_format='OPEN_EXR'; s.color_depth='16'; s.exr_codec='ZIP'; s.color_mode='RGB'
img.save_render(dst); print('EXR ok',W,H)
