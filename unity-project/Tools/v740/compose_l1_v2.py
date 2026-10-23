# v7.4.0 (v2): texturas PBR del Nivel 1 (aparcamiento). Base CC0 de ambientCG (Concrete042A suelo, Concrete031 muros/pilares,
# Concrete012 techo) + capas propias con más carácter que las v6.6: juntas de encofrado y agujeros de latiguillo con relieve,
# chorretones de óxido y agua, rozaduras de parachoques; en el suelo líneas pintadas nítidas con desconchones, rodadas y
# derrapes de neumático, manchas de aceite con brillo, zonas húmedas, juntas de corte y grietas selladas.
# Todo es periódico (la textura se repite sin costuras). Salida 2048: _a (sRGB), _n (normal GL), _m (R metal, G AO, A suavidad).
import numpy as np, os, sys
from PIL import Image
from scipy.ndimage import gaussian_filter
C='/workspace/v740/cc0'; O=sys.argv[1] if len(sys.argv)>1 else '/workspace/v740/l1/tex2'; N=2048
os.makedirs(O, exist_ok=True)
def load(id, kind, mode='RGB'):
    d=f'{C}/{id}'; pre=[f for f in os.listdir(d) if f.endswith('_Color.jpg')][0][:-10]
    return np.asarray(Image.open(f'{d}/{pre}_{kind}.jpg').convert(mode),np.float32)/255
def resample(img, sx, sy):
    h,w=img.shape[:2]
    xs=(np.arange(N)*(w*sx/N)).astype(np.int64)%w; ys=(np.arange(N)*(h*sy/N)).astype(np.int64)%h
    return img[ys][:,xs]
def blurw(a, s): return gaussian_filter(a, sigma=(s,s) if a.ndim==2 else (s,s,0), mode='wrap')
def noise(cells, seed, octaves=4, gain=0.5):
    r=np.random.default_rng(seed); acc=np.zeros((N,N),np.float32); amp=1; tot=0
    for o in range(octaves):
        c=int(cells*2**o); g=r.random((c,c)).astype(np.float32)
        up=np.kron(g, np.ones((N//c+1, N//c+1),np.float32))[:N,:N] if N%c else np.kron(g, np.ones((N//c,N//c),np.float32))
        acc+=amp*blurw(up, N/c*0.5); tot+=amp; amp*=gain
    a=acc/tot; return (a-a.min())/(a.max()-a.min()+1e-6)
def pdist(a, c, L): return (a-c+L/2)%L-L/2
u=(np.arange(N)+0.5)/N; U,R=np.meshgrid(u,u)      # R: fila (0 arriba). v (de la malla) = 1-R
V=1-R
def h2n(h, k, base=None):
    # altura -> normal GL (verde = +v). La fila crece hacia -v.
    dx=(np.roll(h,-1,1)-np.roll(h,1,1))*0.5; dy=-(np.roll(h,-1,0)-np.roll(h,1,0))*0.5   # pendiente por píxel
    nx,ny=-dx*k,-dy*k
    if base is not None:
        b=base*2-1; nx=nx+b[...,0]/np.maximum(b[...,2],0.2); ny=ny+b[...,1]/np.maximum(b[...,2],0.2)
    l=np.sqrt(nx*nx+ny*ny+1); return np.stack([nx/l,ny/l,1/l],-1)*0.5+0.5
def boost(nrm, k):
    b=nrm*2-1; b[...,0]*=k; b[...,1]*=k; b/=np.linalg.norm(b,axis=-1,keepdims=True); return b*0.5+0.5
def lum(a): return a[...,0]*0.299+a[...,1]*0.587+a[...,2]*0.114
def tone(alb, mean, contrast):
    l=lum(alb); m=l.mean(); out=(alb-m)*contrast+m; return np.clip(out*(mean/max(lum(np.clip(out,0,1)).mean(),1e-3)),0,1)
def save(name, alb, nrm, ao, sm):
    Image.fromarray(np.clip(alb*255+0.5,0,255).astype(np.uint8)).save(f'{O}/{name}_a.jpg',quality=93)
    Image.fromarray(np.clip(nrm*255+0.5,0,255).astype(np.uint8)).save(f'{O}/{name}_n.png')
    m=np.stack([np.zeros_like(ao),ao,np.zeros_like(ao),sm],-1)
    Image.fromarray(np.clip(m*255+0.5,0,255).astype(np.uint8),'RGBA').save(f'{O}/{name}_m.png')
    print(name,'lum',round(float(lum(alb).mean()),3),'std',round(float(lum(alb).std()),3),'smooth',round(float(sm.mean()),3),'ao',round(float(ao.mean()),3))

# ======================= MUROS Y PILARES: u 3 m por repetición, v 0..1 = suelo..techo (3.6 m) =======================
rng=np.random.default_rng(7410)
col=load('Concrete031','Color'); nr=load('Concrete031','NormalGL'); ro=load('Concrete031','Roughness','L'); ao=load('Concrete031','AmbientOcclusion','L')
alb=resample(col,1.5,1.8); nb=boost(resample(nr,1.5,1.8),1.6); rough=resample(ro,1.5,1.8); aoc=resample(ao,1.5,1.8)
X=U*3.0; H=V*3.6
# variación por tablero de encofrado (paneles de 1.5 x 1.2 m, cada uno con su tono, como el hormigón visto real)
PW,PH=1.5,1.2
pi=np.floor(X/PW).astype(int); pj=np.floor(H/PH).astype(int)
ptone=np.random.default_rng(31).random((2,3))[pi%2,pj%3]
alb=alb*(0.90+0.16*ptone[...,None])
big=noise(3,32,4); alb*=(0.86+0.24*big[...,None])
height=np.zeros((N,N),np.float32)
# juntas entre tableros: rehundidas 3 mm con rebaba (lechada) a los lados
dxs=np.abs(pdist(X,0,PW)); dys=np.abs(pdist(H,0,PH))
sd=np.minimum(dxs,dys)
seam=np.exp(-(sd/0.0045)**2); burr=np.exp(-((sd-0.011)/0.004)**2)*(0.5+0.5*noise(40,33,2))
height+=-1.0*seam+0.35*burr
alb*=(1-0.55*seam[...,None]); alb=alb*(1-0.10*burr[...,None])+0.62*0.10*burr[...,None]; aoc*=1-0.6*seam
# agujeros de latiguillo: 2 x 2 por tablero (a 0.3 m de los bordes), cono rehundido + tapón oscuro; óxido en algunos
holes=np.zeros((N,N),np.float32); cone=np.zeros((N,N),np.float32); rust=np.zeros((N,N),np.float32)
hr=np.random.default_rng(34)
for hx in (0.3,1.2,1.8,2.7):
    for hy in (0.3,0.9,1.5,2.1,2.7,3.3):
        d=np.sqrt(pdist(X,hx,3.0)**2+(H-hy)**2)
        holes+=np.clip(1-d/0.014,0,1)**0.6
        cone+=np.clip(1-d/0.026,0,1)
        if hr.random()<0.38:
            dy=hy-H; wob=0.012*np.sin(H*9+hr.random()*6)
            ln=0.08+0.30*hr.random()
            st=np.exp(-((pdist(X,hx,3.0)+wob)/(0.010+0.012*np.clip(dy,0,None)))**2)*(dy>0)*np.exp(-np.clip(dy,0,None)/ln)
            rust+=st*(0.5+0.5*hr.random())+np.clip(1-d/0.03,0,1)*0.8
holes=np.clip(holes,0,1); cone=np.clip(cone,0,1)
height+=-1.6*cone-2.0*holes
rust=np.clip(rust,0,1)*(0.55+0.45*noise(60,35,3))
rc=np.array([0.47,0.26,0.12],np.float32)
alb=alb*(1-0.65*rust[...,None])+rc*0.65*rust[...,None]
alb*=(1-0.35*cone[...,None]); alb=alb*(1-0.85*holes[...,None])+np.array([0.07,0.07,0.075],np.float32)*0.85*holes[...,None]
aoc*=(1-0.5*cone)*(1-0.6*holes)
# chorretones de agua (verticales, más oscuros) desde arriba y desde algunas juntas
wr=np.random.default_rng(36); streaks=np.zeros((N,N),np.float32)
for k in range(11):
    sx_=wr.random()*3.0; top=3.6-wr.random()*1.4; ln=0.6+wr.random()*1.8; w=0.03+0.07*wr.random()
    e=np.exp(-(pdist(X,sx_,3.0)/w)**2)*np.clip((top-H)/0.1,0,1)*np.exp(-np.clip(top-H,0,None)/ln)
    streaks+=e*(0.5+0.5*noise(24,37+k,3))
streaks=np.clip(streaks,0,1)
alb*=(1-0.28*streaks[...,None]); rough=np.clip(rough-0.08*streaks,0,1)
# suciedad de la base (salpicaduras), rozaduras negras de parachoques a 0.35-0.65 m y raspones claros
splash=np.clip(1-H/0.6,0,1)**1.4*(0.5+0.5*noise(18,38,5))
alb*=(1-0.45*splash[...,None])
sr=np.random.default_rng(39); scuff=np.zeros((N,N),np.float32); scrape=np.zeros((N,N),np.float32)
for k in range(9):
    cx=sr.random()*3.0; cy=0.35+0.3*sr.random(); L=0.15+0.5*sr.random(); ang=(sr.random()-0.5)*0.25
    dx=pdist(X,cx,3.0); dy=(H-cy)-dx*ang
    m=np.exp(-(dy/0.012)**2)*np.clip(1-np.abs(dx)/L,0,1)*(noise(80,40+k,2)>0.35)
    (scuff if k%3 else scrape)[...]+=m
scuff=blurw(np.clip(scuff,0,1),0.8); scrape=blurw(np.clip(scrape,0,1),0.8)
alb=alb*(1-0.6*scuff[...,None])+0.05*0.6*scuff[...,None]
alb=alb*(1-0.35*scrape[...,None])+0.72*0.35*scrape[...,None]; height+=-0.4*scrape
# grietas finas (paseo aleatorio) con oscurecido
cr=np.zeros((N,N),np.float32); crng=np.random.default_rng(41)
for k in range(6):
    p=np.array([crng.random()*N, crng.random()*N]); a=crng.random()*6.28
    for s in range(int(150+crng.random()*250)):
        a+=(crng.random()-0.5)*0.6; p=(p+np.array([np.cos(a),np.sin(a)])*2.0)%N
        cr[int(p[1])%N,int(p[0])%N]=1
cr=np.clip(blurw(cr,0.7)*3,0,1)
alb*=(1-0.5*cr[...,None]); height+=-0.8*cr
# humedad y eflorescencia en la zona alta
damp=np.clip((H-2.5)/1.1,0,1)*np.clip((noise(5,43,5)-0.42)*3,0,1)
alb*=1-0.22*damp[...,None]
eff=np.clip((noise(10,44,5)-0.64)*4,0,1)*np.clip((H-2.8)/0.8,0,1)
alb=alb*(1-0.45*eff[...,None])+0.82*0.45*eff[...,None]
alb=tone(alb,0.48,1.25)
nrm=h2n(blurw(height,0.8), 0.9, nb)
save('ParkingWall', alb, nrm, np.clip(aoc,0,1), np.clip((1-rough)*0.55,0,1))

# ======================= SUELO: u 2.7 m, v 5.4 m por repetición (una plaza) =======================
col=load('Concrete042A','Color'); nr=load('Concrete042A','NormalGL'); ro=load('Concrete042A','Roughness','L'); ao=load('Concrete042A','AmbientOcclusion','L')
sx,sy=2.7/2.0,5.4/2.0
alb=resample(col,sx,sy); nb=boost(resample(nr,sx,sy),2.6); rough=resample(ro,sx,sy); aoc=resample(ao,sx,sy)
X=U*2.7; Y=V*5.4
height=np.zeros((N,N),np.float32)
# manchado grande del fratasado + parches más claros/oscuros
big=noise(3,51,5); mid=noise(9,52,4)
alb*=(0.78+0.34*big[...,None])*(0.92+0.14*mid[...,None])
# junta de corte (serrada) a lo largo de v=0 y grieta sellada con masilla negra
cut=np.exp(-(np.abs(pdist(Y,2.7,5.4))/0.004)**2)
alb*=(1-0.6*cut[...,None]); height+=-1.2*cut; aoc*=1-0.5*cut
sealant=np.zeros((N,N),np.float32); srng=np.random.default_rng(53)
p=np.array([0.35*N,0.62*N]); a=0.3
for s in range(900):
    a+=(srng.random()-0.5)*0.35; p=(p+np.array([np.cos(a),np.sin(a)])*1.6)%N; sealant[int(p[1])%N,int(p[0])%N]=1
sealant=np.clip(blurw(sealant,1.1)*4,0,1)
alb=alb*(1-0.55*sealant[...,None])+0.05*0.55*sealant[...,None]; rough=rough*(1-sealant)+0.35*sealant; height+=0.3*sealant
# rodadas pulidas (más oscuras y brillantes) por donde pasan las ruedas
tyre=np.zeros((N,N),np.float32)
for c in (0.78,1.92):
    tyre+=np.exp(-(pdist(X,c,2.7)/0.16)**2)*(0.55+0.45*noise(12,54,3))
tyre=np.clip(tyre,0,1)*np.clip((Y-0.4)/1.2,0,1)
alb*=1-0.16*tyre[...,None]; rough=np.clip(rough-0.18*tyre,0,1)
# marcas de neumático (goma negra): arcos de giro y frenadas rectas
tm=np.zeros((N,N),np.float32); trng=np.random.default_rng(55)
for k in range(4):
    cx,cy=trng.random()*2.7,trng.random()*5.4; rad=2.0+trng.random()*5; a0=trng.random()*6.28; span=0.4+trng.random()*0.6
    for side in (0,0.19):
        d=np.sqrt(pdist(X,cx,2.7)**2+pdist(Y,cy,5.4)**2)
        ang=np.arctan2(pdist(Y,cy,5.4),pdist(X,cx,2.7))
        band=np.exp(-((d-(rad+side)%2.6)/0.045)**2)*(np.abs(((ang-a0+np.pi)%(2*np.pi))-np.pi)<span)
        tm+=band*(0.3+0.7*noise(30,56+k,3))
tm=np.clip(blurw(tm,1.2),0,1)*(noise(6,57,3)>0.25)
alb=alb*(1-0.30*tm[...,None])+0.03*0.30*tm[...,None]; rough=np.clip(rough-0.1*tm,0,1)
# manchas de aceite en la plaza (debajo del motor), con brillo; y goteo disperso
oil=np.zeros((N,N),np.float32); orng=np.random.default_rng(58)
for (cx,cy,s) in [(1.35,3.9,0.45),(1.2,4.3,0.25),(1.55,3.4,0.18),(0.5,1.2,0.12),(2.2,2.4,0.1)]:
    d=np.sqrt(pdist(X,cx,2.7)**2+(pdist(Y,cy,5.4)*1.2)**2)
    oil+=np.clip(1-d/(s*(0.75+0.5*noise(14,59,3))),0,1)**0.7
for k in range(40):
    cx,cy=1.35+orng.normal(0,0.35),3.9+orng.normal(0,0.6); s=0.01+0.03*orng.random()
    oil+=np.clip(1-np.sqrt(pdist(X,cx,2.7)**2+pdist(Y,cy,5.4)**2)/s,0,1)
oil=np.clip(oil,0,1); oil=blurw(oil,1.5)
ring=np.clip(oil-blurw(oil,6)*0.6,0,1)
oc=np.array([0.10,0.085,0.06],np.float32)
alb=alb*(1-0.72*oil[...,None])+oc*0.72*oil[...,None]; alb*=1-0.25*ring[...,None]
rough=rough*(1-oil*0.8)+0.38*oil*0.8
# zonas húmedas (oscuras y lisas, sin color)
wet=np.clip((noise(4,60,5)-0.62)*4,0,1)
alb*=1-0.30*wet[...,None]; rough=np.clip(rough*(1-0.85*wet)+0.08*wet,0,1)
# líneas pintadas: bordes nítidos, desconchones de bordes duros, desgaste en la rodada, polvo en los poros
lw=0.10
d_line=np.minimum(np.minimum(X, 2.7-X), np.abs(pdist(Y,0,5.4)) + 9*(Y>0.0)*0) # u=0 / u=2.7
d_line=np.minimum(np.minimum(X,2.7-X), np.where(Y<lw*1.0, 0, 9))
inside=np.minimum(np.minimum(X,2.7-X)-0.0, 9)
line_u=np.clip((lw*0.5-np.minimum(X,2.7-X))*N/2.7*0.8+0.5,0,1)     # 0.05 m a cada lado de la costura = 0.10 m en total
line_v=np.clip((lw-np.abs(pdist(Y,0,5.4)))*N/5.4*0.8+0.5,0,1)*(np.abs(pdist(Y,0,5.4))<lw*1.5)
line_v=np.clip((lw*0.5-np.abs(pdist(Y,0,5.4)))*N/5.4*0.8+0.5,0,1)
line=np.maximum(line_u,line_v)
chip=(noise(70,61,3)>0.30).astype(np.float32)*(noise(16,62,3)>0.22)
wearp=np.clip(1-0.75*tyre,0,1)*(noise(26,63,4)>0.18)
paint=line*chip*wearp
paint=np.clip(blurw(paint,0.45),0,1)
pores=(noise(200,64,2)>0.72)*0.35
alb=tone(alb,0.48,1.3)
yel=np.array([0.88,0.71,0.15],np.float32)*(0.88+0.12*noise(20,65)[...,None])
alb=alb*(1-paint[...,None])+yel*(1-pores[...,None])*paint[...,None]+alb*pores[...,None]*paint[...,None]*0.0
rough=rough*(1-paint)+0.52*paint; height+=0.5*paint; aoc=aoc*(1-0.0*paint)+paint*0.0+aoc*0
nrm=h2n(blurw(height,0.7), 0.8, nb)
save('ParkingFloor', alb, nrm, np.clip(aoc*0.9+0.1,0,1), np.clip(1-rough,0,1)*0.85)

# ======================= TECHO: 3 m por repetición, losa con juntas, manchas de agua y eflorescencia =======================
col=load('Concrete012','Color'); nr=load('Concrete012','NormalGL'); ro=load('Concrete012','Roughness','L')
alb=resample(col,1.5,1.5); nb=boost(resample(nr,1.5,1.5),1.4); rough=resample(ro,1.5,1.5); aoc=np.ones((N,N),np.float32)
g=alb.mean(-1,keepdims=True); alb=g*0.95+alb*0.05
X=U*3.0; Y=V*3.0; height=np.zeros((N,N),np.float32)
sd=np.minimum(np.abs(pdist(X,0,1.5)),np.abs(pdist(Y,0,3.0)))
joint=np.exp(-(sd/0.006)**2); height+=-1.0*joint
alb*=1-0.5*joint[...,None]; aoc*=1-0.5*joint
# marcas de los tableros del encofrado (vetas lineales suaves)
boards=np.sin(Y/0.2*np.pi)*0.5+0.5; alb*=(0.96+0.06*boards[...,None]); height+=0.15*boards
alb*=(0.85+0.25*noise(3,71,5)[...,None])
stain=np.clip((noise(4,72,5)-0.55)*3.5,0,1)
ring=np.clip(blurw(stain,5)-stain*0.85,0,1)*2.5
alb=alb*(1-0.25*stain[...,None])*(1-0.3*np.clip(ring,0,1)[...,None])
alb=alb*(1-0.12*stain[...,None])+np.array([0.40,0.32,0.20],np.float32)*0.12*stain[...,None]
eff=np.clip((noise(9,73,5)-0.68)*4,0,1)*(1-joint)
alb=alb*(1-0.4*eff[...,None])+0.84*0.4*eff[...,None]
alb=tone(alb,0.50,1.2)
nrm=h2n(blurw(height,0.8), 0.8, nb)
save('ParkingCeiling', alb, nrm, np.clip(aoc,0,1), np.clip(1-rough,0,1)*0.6)
