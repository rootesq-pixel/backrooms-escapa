import sys, os
from PIL import Image, ImageChops, ImageOps, ImageDraw, ImageFont, ImageFilter
import numpy as np
C='/workspace/v740/cc0'
def pack(id, out, res=None, metal=None, smooth_mul=1.0, smooth_add=0.0, tint=None, odir='tex'):
    d=f'{C}/{id}'; pre=[f for f in os.listdir(d) if f.endswith('_Color.jpg')][0][:-10]
    col=Image.open(f'{d}/{pre}_Color.jpg').convert('RGB')
    nrm=Image.open(f'{d}/{pre}_NormalGL.jpg').convert('RGB')
    rough=np.asarray(Image.open(f'{d}/{pre}_Roughness.jpg').convert('L'),dtype=np.float32)/255
    n=col.size[0] if res is None else res
    if res: col=col.resize((res,res),Image.LANCZOS); nrm=nrm.resize((res,res),Image.LANCZOS)
    rough=np.asarray(Image.fromarray((rough*255).astype(np.uint8)).resize((n,n),Image.LANCZOS),dtype=np.float32)/255
    if os.path.exists(f'{d}/{pre}_Metalness.jpg'):
        met=np.asarray(Image.open(f'{d}/{pre}_Metalness.jpg').convert('L').resize((n,n)),dtype=np.float32)/255
    else: met=np.zeros((n,n),np.float32)
    if metal is not None: met=np.full((n,n),metal,np.float32)
    ao=np.ones((n,n),np.float32)
    if os.path.exists(f'{d}/{pre}_AmbientOcclusion.jpg'):
        ao=np.asarray(Image.open(f'{d}/{pre}_AmbientOcclusion.jpg').convert('L').resize((n,n)),dtype=np.float32)/255
    sm=np.clip((1-rough)*smooth_mul+smooth_add,0,1)
    m=np.stack([met,ao,np.zeros_like(ao),sm],-1)
    os.makedirs(odir,exist_ok=True)
    if tint is not None:
        a=np.asarray(col,dtype=np.float32)*np.array(tint,np.float32); col=Image.fromarray(np.clip(a,0,255).astype(np.uint8))
    col.save(f'{odir}/{out}_a.jpg',quality=92); nrm.save(f'{odir}/{out}_n.png')
    Image.fromarray((m*255).astype(np.uint8),'RGBA').save(f'{odir}/{out}_m.png')
    print(out, n, 'metal', round(float(met.mean()),2), 'smooth', round(float(sm.mean()),2))
if len(sys.argv)>1 and sys.argv[1]=='elev':
    pack('Metal009','ElevSteel',512, metal=0.55, smooth_mul=0.9)
    pack('Metal038','ElevSteelDark',512, metal=0.6, smooth_mul=0.8)
    pack('Metal009','ElevPaint',512, metal=0.08, smooth_mul=0.4, tint=(0.80,0.80,0.76))
    pack('Wood051','ElevWood',1024, metal=0.0, smooth_mul=0.55, smooth_add=0.0)
    pack('Rubber001','ElevFloor',512, metal=0.0)

if len(sys.argv)>1 and sys.argv[1]=='elev':
    # suciedad en el borde del suelo (RGBA): oscura, con ruido, alfa que se desvanece desde la pared (v=0) hacia dentro (v=1)
    rng=np.random.default_rng(7); W,H=512,64
    base=np.asarray(Image.fromarray((rng.random((H//4,W//4))*255).astype(np.uint8)).resize((W,H),Image.BICUBIC),np.float32)/255
    fine=rng.random((H,W)).astype(np.float32)
    v=np.linspace(0,1,H)[:,None]
    a=np.clip((1-v)**1.6*(0.55+0.6*base)-0.08*fine,0,1)*0.85
    rgb=np.stack([np.full((H,W),48),np.full((H,W),42),np.full((H,W),34)],-1).astype(np.float32)*(0.8+0.4*base[...,None])
    Image.fromarray(np.dstack([np.clip(rgb,0,255),a*255]).astype(np.uint8),'RGBA').save('tex/ElevGrime_a.png')
    print('grime ok')
