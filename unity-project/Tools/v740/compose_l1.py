# v7.4.0: texturas PBR del Nivel 1 (aparcamiento) a partir de CC0 de ambientCG (Concrete042A suelo, Concrete031 paredes/pilares,
# Concrete012 techo) + capas propias: líneas de aparcamiento pintadas y gastadas, suciedad de salpicadura, óxido bajo los
# agujeros de encofrado, manchas de humedad y eflorescencia en el techo. Salida 2048: _a (sRGB), _n (normal GL), _m (R met, G AO, A suavidad)
import numpy as np, os
from PIL import Image, ImageFilter
C='/workspace/v740/cc0'; O='/workspace/v740/l1/tex'; N=2048
rng=np.random.default_rng(1740)
def load(id, kind, mode='RGB'):
    d=f'{C}/{id}'; pre=[f for f in os.listdir(d) if f.endswith('_Color.jpg')][0][:-10]
    p=f'{d}/{pre}_{kind}.jpg'
    if not os.path.exists(p): return None
    return np.asarray(Image.open(p).convert(mode),np.float32)/255
def resample(img, sx, sy):
    # muestrea la textura fuente (que se repite) a N x N cubriendo sx x sy repeticiones
    h,w=img.shape[:2]
    xs=(np.arange(N)*(w*sx/N)).astype(np.int64)%w; ys=(np.arange(N)*(h*sy/N)).astype(np.int64)%h
    return img[ys][:,xs]
def noise(scale, seed, octaves=4):
    r=np.random.default_rng(seed); acc=np.zeros((N,N),np.float32); amp=1; tot=0
    for o in range(octaves):
        s=max(2,int(scale*2**o)); g=r.random((s,s)).astype(np.float32)
        g=np.pad(g,((0,1),(0,1)),mode='wrap')
        acc+=amp*np.asarray(Image.fromarray((g*255).astype(np.uint8)).resize((N+N//s,N+N//s),Image.BICUBIC),np.float32)[:N,:N]/255
        tot+=amp; amp*=0.5
    return acc/tot
def save(name, alb, nrm, ao, sm):
    Image.fromarray(np.clip(alb*255,0,255).astype(np.uint8)).save(f'{O}/{name}_a.jpg',quality=92)
    Image.fromarray(np.clip(nrm*255,0,255).astype(np.uint8)).save(f'{O}/{name}_n.png')
    m=np.stack([np.zeros_like(ao),ao,np.zeros_like(ao),sm],-1)
    Image.fromarray(np.clip(m*255,0,255).astype(np.uint8),'RGBA').save(f'{O}/{name}_m.png')
    print(name, 'albedo', alb.mean(axis=(0,1)).round(3), 'smooth', round(float(sm.mean()),3))
def flatten(nrm, k):
    flat=np.array([0.5,0.5,1.0],np.float32); return nrm*(1-k[...,None])+flat*k[...,None]
def blur(a, r): return np.asarray(Image.fromarray(np.clip(a*255,0,255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r)),np.float32)/255

# ---------------- suelo: 2.7 m (u) x 5.4 m (v) por repetición; la fuente cubre ~2 m ----------------
col=load('Concrete042A','Color'); nr=load('Concrete042A','NormalGL'); ro=load('Concrete042A','Roughness','L'); ao=load('Concrete042A','AmbientOcclusion','L')
sx,sy=2.7/2.0,5.4/2.0
alb=resample(col,sx,sy); nrm=resample(nr,sx,sy); rough=resample(ro,sx,sy); aoc=resample(ao,sx,sy)
alb=alb*0.86   # un poco más oscuro (hormigón de aparcamiento, polvo)
u=(np.arange(N)+0.5)/N; v=(np.arange(N)+0.5)/N; U,V=np.meshgrid(u,v)
mx=U*2.7; my=V*5.4   # metros
# rodadas: dos franjas algo más oscuras y pulidas por donde pasan las ruedas (a lo largo de v)
tyre=np.zeros((N,N),np.float32)
for c in (0.85,1.85):
    tyre+=np.exp(-((mx-c)/0.18)**2)*(0.5+0.5*noise(6,11))
tyre=np.clip(tyre,0,1)*np.clip((my-0.6)/1.0,0,1)
alb*=1-0.10*tyre[...,None]; rough=np.clip(rough-0.10*tyre,0,1)
# líneas amarillas: borde izquierdo (u=0) de 0.10 m y línea de fondo (v=0) de 0.10 m, pintura gastada
wear=noise(10,21,5); chips=noise(48,22,3)
line=((mx<0.10)|(mx>2.7-0.0)|(my<0.10)).astype(np.float32)
paint=line*np.clip((wear-0.12)*4.0,0,1)*np.clip((chips-0.08)*6,0,1)*0.92
paint=blur(paint,0.8)
yel=np.array([0.80,0.64,0.16],np.float32)*(0.85+0.15*noise(20,23)[...,None])
alb=alb*(1-paint[...,None])+yel*paint[...,None]*(0.75+0.25*alb.mean(-1,keepdims=True)/0.5)
rough=rough*(1-paint)+0.55*paint; nrm=flatten(nrm,paint*0.7)
# polvo/suciedad fina y alguna grieta reparada (sellado oscuro)
dirt=noise(4,31,5); alb*=(0.92+0.12*dirt[...,None])
save('ParkingFloor', alb, nrm, np.clip(aoc,0,1), np.clip(1-rough,0,1))

# ---------------- paredes y pilares: v=0 suelo .. v=1 techo (3.6 m), u: 3 m por repetición ----------------
col=load('Concrete031','Color'); nr=load('Concrete031','NormalGL'); ro=load('Concrete031','Roughness','L'); ao=load('Concrete031','AmbientOcclusion','L')
# Concrete031 cubre ~2 m: 3 m en u = 1.5 rep, 3.6 m en v = 1.8 rep
sx,sy=1.5,1.8
alb=resample(col,sx,sy)*0.92; nrm=resample(nr,sx,sy); rough=resample(ro,sx,sy); aoc=resample(ao,sx,sy)
# v crece hacia arriba en la malla; en la imagen la fila 0 es arriba -> altura h = (1 - fila/N) * 3.6
H=(1-V)*3.6; X=U*3.0
# salpicaduras y suciedad de ruedas en la base (0..0.5 m), más oscura y algo grasienta
splash=np.clip(1-H/0.55,0,1)**1.5*(0.55+0.45*noise(14,41,5))
alb*=1-0.42*splash[...,None]; rough=np.clip(rough+0.05*splash,0,1)
# línea pintada amarilla/negra de protección a 0.2..0.5 m? (solo en pilares se ve; se deja como franja gris oscura de alquitrán)
# chorretones de óxido bajo los agujeros del encofrado (retícula 0.75 m x 0.9 m)
rust=np.zeros((N,N),np.float32)
for hx in np.arange(0.375,3.0,0.75):
    for hy in (0.9,1.8,2.7):
        dx=X-hx; dy=hy-H
        hole=np.exp(-(dx**2+(H-hy)**2)/0.0004)
        streak=np.exp(-(dx/0.035)**2)*np.clip(dy,0,None)*np.exp(-np.clip(dy,0,None)/0.35)*(dy>0)
        rust+=hole*0.9+streak*3.5*(0.6+0.4*rng.random())*(rng.random()<0.55)
rust=np.clip(rust,0,1)*(0.6+0.4*noise(30,42))
rc=np.array([0.42,0.24,0.12],np.float32)
alb=alb*(1-0.7*rust[...,None])+rc*0.7*rust[...,None]
holes=np.zeros((N,N),np.float32)
for hx in np.arange(0.375,3.0,0.75):
    for hy in (0.9,1.8,2.7): holes+=np.exp(-((X-hx)**2+(H-hy)**2)/0.00018)
holes=np.clip(holes,0,1); alb*=1-0.8*holes[...,None]; aoc=aoc*(1-0.7*holes)
# manchas de humedad desde el techo (zona alta) y eflorescencia blanquecina
damp=np.clip((H-2.6)/1.0,0,1)*np.clip((noise(5,43,5)-0.45)*3,0,1)
alb*=1-0.25*damp[...,None]
eff=np.clip((noise(8,44,5)-0.68)*4,0,1)*np.clip((H-2.9)/0.7,0,1)
alb=alb*(1-0.4*eff[...,None])+0.78*0.4*eff[...,None]
save('ParkingWall', alb, nrm, np.clip(aoc,0,1), np.clip(1-rough,0,1))

# ---------------- techo: 3 m por repetición, losa con juntas, manchas de agua y eflorescencia ----------------
col=load('Concrete012','Color'); nr=load('Concrete012','NormalGL'); ro=load('Concrete012','Roughness','L')
sx=sy=1.5
alb=resample(col,sx,sy); nrm=resample(nr,sx,sy); rough=resample(ro,sx,sy); aoc=np.ones((N,N),np.float32)
g=alb.mean(-1,keepdims=True); alb=g*0.95+alb*0.05   # gris neutro (el CC0 es algo verdoso)
alb=alb*0.80
X=U*3.0; Y=V*3.0
joint=np.exp(-((np.minimum(X%1.5,1.5-X%1.5))/0.012)**2)+np.exp(-((np.minimum(Y%3.0,3.0-Y%3.0))/0.012)**2)
joint=np.clip(joint,0,1); alb*=1-0.45*joint[...,None]; aoc*=1-0.5*joint
stain=np.clip((noise(4,51,5)-0.5)*3.5,0,1)
ring=np.clip(blur(stain,6)-stain*0.85,0,1)*2.5
alb=alb*(1-0.22*stain[...,None])*(1-0.25*np.clip(ring,0,1)[...,None])
alb=alb*(1-0.1*stain[...,None])+np.array([0.36,0.30,0.20],np.float32)*0.1*stain[...,None]
eff=np.clip((noise(9,52,5)-0.7)*4,0,1)*(1-joint)
alb=alb*(1-0.35*eff[...,None])+0.8*0.35*eff[...,None]
save('ParkingCeiling', alb, nrm, np.clip(aoc,0,1), np.clip(1-rough,0,1))

# ---------------- calcomanías: manchas de aceite (4 variantes, RGBA) y charcos (alfa = humedad) ----------------
D=512
def blob(seed, size=0.55, lobes=5):
    r=np.random.default_rng(seed); yy,xx=np.mgrid[0:D,0:D]/D-0.5
    m=np.zeros((D,D),np.float32)
    for i in range(lobes):
        cx,cy=(r.random(2)-0.5)*0.35; rad=size*(0.25+0.35*r.random())*0.5
        m=np.maximum(m,np.clip(1-np.sqrt(((xx-cx)/rad)**2+((yy-cy)/(rad*(0.6+0.8*r.random())))**2),0,1))
    nn=np.asarray(Image.fromarray((r.random((32,32))*255).astype(np.uint8)).resize((D,D),Image.BICUBIC),np.float32)/255
    m=np.clip(m*1.5+(nn-0.5)*0.35,0,1); edge=np.clip(1-np.sqrt(xx**2+yy**2)*2.1,0,1)
    return m*edge
oil=np.zeros((D*2,D*2,4),np.float32)
for k in range(4):
    b=blob(600+k, 0.75, 6); b=blur(b,3)**1.2
    core=np.clip((b-0.35)*2.5,0,1)
    rgb=np.stack([np.full((D,D),0.06),np.full((D,D),0.055),np.full((D,D),0.05)],-1)
    a=np.clip(b*0.55+core*0.4,0,0.92)
    oy,ox=(k//2)*D,(k%2)*D; oil[oy:oy+D,ox:ox+D,:3]=rgb; oil[oy:oy+D,ox:ox+D,3]=a
Image.fromarray((oil*255).astype(np.uint8),'RGBA').save(f'{O}/ParkingOil_a.png')
wet=np.zeros((D*2,D*2,4),np.float32)
for k in range(4):
    b=blob(700+k, 0.95, 7); b=blur(b,5)
    a=np.clip((b-0.12)*1.7,0,0.85)
    oy,ox=(k//2)*D,(k%2)*D; wet[oy:oy+D,ox:ox+D,:3]=0.10; wet[oy:oy+D,ox:ox+D,3]=a
Image.fromarray((wet*255).astype(np.uint8),'RGBA').save(f'{O}/ParkingWet_a.png')
print('decals ok')
