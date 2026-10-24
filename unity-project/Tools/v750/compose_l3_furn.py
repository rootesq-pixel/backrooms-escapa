# v7.5.0: texturas PBR de los muebles nuevos del hotel (nivel 3). Bases CC0 de ambientCG: Fabric062 (lino de los manteles),
# PaintedPlaster017 (yeso pintado de las columnas), Marble006 (mármol negro del mostrador) + dibujo propio: manteles marfil
# amarillentos con polvo, manchas de vino y café y cercos; columnas de yeso marfil con mugre, grietas y desconchones; mármol
# algo más cálido y gastado. Salida como compose_l3.py: _a (sRGB), _n (normal GL), _m (R metal, G AO, A suavidad).
import numpy as np, os, sys
from PIL import Image
from scipy.ndimage import gaussian_filter, map_coordinates
C='/workspace/v750/cc0'; O=sys.argv[1] if len(sys.argv)>1 else '/workspace/v750/furn/tex'
os.makedirs(O+'/Hotel', exist_ok=True)
def load(id, kind, mode='RGB'):
    d=f'{C}/{id}'; pre=[f for f in os.listdir(d) if f.endswith('_Color.jpg')][0][:-10]
    return np.asarray(Image.open(f'{d}/{pre}_{kind}.jpg').convert(mode).resize((1024,1024),Image.LANCZOS),np.float32)/255
def blurw(a, s): return gaussian_filter(a, sigma=(s,s) if a.ndim==2 else (s,s,0), mode='wrap')
def noise(cells, seed, N, octaves=4, gain=0.5):
    r=np.random.default_rng(seed); acc=np.zeros((N,N),np.float32); amp=1; tot=0
    for o in range(octaves):
        c=int(cells*2**o); g=r.random((c,c)).astype(np.float32)
        yy=(np.arange(N)+0.5)/N*c-0.5; Y,X=np.meshgrid(yy,yy,indexing='ij')
        acc+=amp*map_coordinates(np.pad(g,2,mode='wrap'),[Y+2,X+2],order=1,mode='wrap'); tot+=amp; amp*=gain
    a=acc/tot; return (a-a.min())/(a.max()-a.min()+1e-6)
def pdist(a, c, L): return (a-c+L/2)%L-L/2
def ss(e0,e1,x): t=np.clip((x-e0)/(e1-e0),0,1); return t*t*(3-2*t)
def h2n(h, k, base=None):
    dx=(np.roll(h,-1,1)-np.roll(h,1,1))*0.5; dy=-(np.roll(h,-1,0)-np.roll(h,1,0))*0.5
    nx,ny=-dx*k,-dy*k
    if base is not None:
        b=base*2-1; nx=nx+b[...,0]/np.maximum(b[...,2],0.2); ny=ny+b[...,1]/np.maximum(b[...,2],0.2)
    l=np.sqrt(nx*nx+ny*ny+1); return np.stack([nx/l,ny/l,1/l],-1)*0.5+0.5
def mix(a,b,t):
    t=np.asarray(t,np.float32)
    if t.ndim==2: t=t[...,None]
    return a*(1-t)+b*t
def save(name, alb, nrm, ao, sm, metal=None):
    Image.fromarray(np.clip(alb*255+0.5,0,255).astype(np.uint8)).save(f'{O}/{name}_a.jpg',quality=93)
    Image.fromarray(np.clip(nrm*255+0.5,0,255).astype(np.uint8)).save(f'{O}/{name}_n.png')
    if metal is None: metal=np.zeros_like(ao)
    m=np.stack([metal,ao,np.zeros_like(ao),sm],-1)
    Image.fromarray(np.clip(m*255+0.5,0,255).astype(np.uint8),'RGBA').save(f'{O}/{name}_m.png')
    l=alb[...,0]*0.299+alb[...,1]*0.587+alb[...,2]*0.114; print(name,'lum',round(float(l.mean()),3),'std',round(float(l.std()),3),flush=True)
N=1024; yy=(np.arange(N)+0.5)/N; X,Y=np.meshgrid(yy,yy)   # 1 repetición = 1 m (las mallas usan UV en metros)
# ---------------- mantel: lino marfil amarillento, polvo, manchas de vino/café, cercos de copa, arrugas suaves
fab=load('Fabric062','Color','L'); fabN=load('Fabric062','NormalGL')
f2=blurw(fab,0.6)
base=np.array([0.78,0.74,0.63],np.float32)
alb=base*(0.86+0.22*f2[...,None])
yel=noise(3,2001,N,5); alb=mix(alb,alb*np.array([0.92,0.84,0.62],np.float32),ss(0.35,0.9,yel)*0.6)
dust=noise(6,2002,N,5); alb=mix(alb,np.array([0.52,0.48,0.40],np.float32),ss(0.45,0.95,dust)*0.45)
rng=np.random.default_rng(2003); st=np.zeros((N,N),np.float32); ring=np.zeros((N,N),np.float32); stcol=np.zeros((N,N,3),np.float32)
for i in range(6):
    cx,cy=rng.random(),rng.random(); r=0.03+0.08*rng.random()
    dx=pdist(X,cx,1); dy=pdist(Y,cy,1); wob=1+0.9*(noise(5,2100+i,N,4)-0.5)
    d=np.sqrt(dx*dx+dy*dy)/(r*wob)
    blot=ss(1.0,0.6,d); edge=np.exp(-((d-0.95)/0.08)**2)
    col=np.array([0.42,0.12,0.12],np.float32) if i%3==0 else np.array([0.50,0.36,0.20],np.float32)
    a=np.clip(blot*0.45+edge*0.18,0,1)*(0.35+0.4*rng.random())
    alb=mix(alb,alb*col/0.7,a*0.8)
for i in range(4):
    cx,cy=rng.random(),rng.random(); r=0.03+0.015*rng.random()
    d=np.sqrt(pdist(X,cx,1)**2+pdist(Y,cy,1)**2)
    ring+=np.exp(-((d-r)/0.0018)**2)*(0.2+0.4*rng.random())*ss(0.2,0.6,noise(6,2150+i,N,3))
alb=mix(alb,alb*np.array([0.62,0.50,0.36],np.float32),np.clip(ring,0,1)*0.7)
wr=blurw(noise(10,2004,N,3),3)
save('Hotel/HotelTablecloth',np.clip(alb,0,1),h2n(wr*1.5,2.0,fabN),np.clip(0.85+0.15*f2,0,1),np.clip(0.18-0.08*ss(0.45,0.95,dust),0,1))
# ---------------- columna: yeso pintado marfil, mugre hacia abajo y en las estrías (el canal V: 0..1 = 0..1 m; se repite en altura)
pl=load('PaintedPlaster017','Color','L'); pn=load('PaintedPlaster017','NormalGL')
alb=np.array([0.70,0.66,0.57],np.float32)*(0.78+0.3*pl[...,None])
g=noise(4,2201,N,5); alb=mix(alb,np.array([0.34,0.30,0.24],np.float32),ss(0.5,1.0,g)*0.55)
streak=np.zeros((N,N),np.float32)
for i in range(14):
    cx=rng.random(); w=0.002+0.008*rng.random(); streak+=np.exp(-(pdist(X,cx,1)/w)**2)*(0.3+0.7*rng.random())*(0.4+0.6*noise(3,2300+i,N,3))
alb=mix(alb,np.array([0.30,0.26,0.20],np.float32),np.clip(streak,0,1)*0.25)
cr=1-np.clip(pl*2.2-0.9,0,1); crack=ss(0.35,0.7,blurw(cr,0.4))
alb=mix(alb,np.array([0.16,0.14,0.11],np.float32),crack*0.7)
chip=np.clip((noise(7,2202,N,5)-0.74)*7,0,1); alb=mix(alb,np.array([0.52,0.47,0.40],np.float32)*(0.8+0.3*pl[...,None]),chip)
save('Hotel/HotelColumn',np.clip(alb,0,1),h2n(-chip*0.8-crack*0.6,2.0,pn),np.clip(1-0.4*crack-0.3*chip,0,1),np.clip(0.32-0.2*ss(0.5,1,g)-0.2*chip,0,1))
# ---------------- mármol negro algo más cálido y gastado (mostrador de recepción)
mc=load('Marble006','Color'); mn=load('Marble006','NormalGL'); mr=load('Marble006','Roughness','L')
alb=mc*np.array([1.05,1.0,0.92],np.float32)*1.15+0.012
wear=noise(5,2401,N,5); alb=mix(alb,alb*0.8+0.05,ss(0.5,1,wear)*0.4)
save('Hotel/HotelMarble',np.clip(alb,0,1),mn*0+h2n(np.zeros((N,N),np.float32),1,mn),np.ones((N,N),np.float32),np.clip((1-mr)*0.9-0.25*ss(0.5,1,wear),0.05,0.9))
