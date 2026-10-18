import bpy, numpy as np, sys, os
D=os.environ.get('POOL_OUT','/workspace/pool-render/fixed').rstrip('/')+'/cube/'; out=sys.argv[sys.argv.index('--')+1]
ims=[]
for n in ['px','nx','py','ny','pz','nz']:
    im=bpy.data.images.load(D+n+'.exr'); R=im.size[0]
    a=np.empty(R*R*4,np.float32); im.pixels.foreach_get(a); ims.append(a.reshape(R,R,4))
s=np.concatenate(ims,axis=1); H,W=s.shape[:2]
img=bpy.data.images.new('C',W,H,float_buffer=True,alpha=False); img.pixels.foreach_set(s.ravel())
st=bpy.context.scene.render.image_settings; st.file_format='OPEN_EXR'; st.color_depth='16'; st.exr_codec='ZIP'; st.color_mode='RGB'
img.save_render(out); print('STRIP ok',W,H)
