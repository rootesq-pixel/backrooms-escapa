import numpy as np, pyoidn as o, sys
a=np.load('/workspace/pool-render/fixed/lm_raw.npy').astype(np.float32); m=a.sum(2)>0
d=o.Device(); d.commit(); f=o.Filter(d,o.OIDN_FILTER_TYPE_RT_LIGHTMAP)
inp=np.ascontiguousarray(a); out=np.zeros_like(inp)
f.set_image(o.OIDN_IMAGE_COLOR,inp,o.OIDN_FORMAT_FLOAT3); f.set_image(o.OIDN_IMAGE_OUTPUT,out,o.OIDN_FORMAT_FLOAT3)
f.set_bool('hdr',True) if False else None
f.commit(); f.execute()
out[~m]=0
# dilate 6 px
for _ in range(6):
    mm=out.sum(2)>0; acc=np.zeros_like(out); cnt=np.zeros(m.shape)
    for dy,dx in ((1,0),(-1,0),(0,1),(0,-1)):
        s=np.roll(out,(dy,dx),(0,1)); sm=np.roll(mm,(dy,dx),(0,1)); acc+=s*sm[...,None]; cnt+=sm
    fill=(~mm)&(cnt>0); out[fill]=acc[fill]/cnt[fill][:,None]
np.save('/workspace/pool-render/fixed/lm_dn.npy',out); print('dn',np.percentile(out[m],[5,50,95]))
