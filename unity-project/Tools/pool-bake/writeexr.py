import bpy, numpy as np, os
D=os.environ.get('POOL_OUT','/workspace/pool-render/fixed').rstrip('/')+'/'
a=np.load(D+'lm_dn.npy'); R=a.shape[0]
img=bpy.data.images.new('LM',R,R,float_buffer=True,alpha=False)
px=np.ones((R,R,4),np.float32); px[:,:,:3]=a; img.pixels.foreach_set(px.ravel())
s=bpy.context.scene.render.image_settings; s.file_format='OPEN_EXR'; s.color_depth='16'; s.exr_codec='ZIP'; s.color_mode='RGB'
img.filepath_raw=D+'PoolRoom_LM.exr'; img.file_format='OPEN_EXR'
img.save_render(D+'PoolRoom_LM.exr'); print('EXR ok')
