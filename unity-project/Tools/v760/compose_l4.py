# v7.6.0: texturas PBR del Nivel 4 (Complejo industrial). Bases CC0 de ambientCG (Rust009 óxido, Metal041B acero con churretes,
# MetalPlates013 acero oscuro, DiamondPlate008C chapa lagrimada, PaintedMetal006 pintura) + dibujo propio:
#   IndustrialWall  (3,6 m x 3,4 m, altura completa): chapas remachadas de 1,2 m con solape, zócalo de chapa sin pintar, franja
#                   amarilla desconchada, pintura gris verdosa gastada con desconchones en bordes y remaches, churretes de óxido,
#                   mugre abajo, polvo en los solapes, arañazos de carretillas.
#   IndustrialFloor (2,4 m): chapa lagrimada de verdad (resaltes en relieve alternos), chapas de 1,2 x 2,4 con tornillos avellanados,
#                   resaltes pulidos por las pisadas, mugre en los huecos, óxido, aceite y polvo.
#   IndustrialCeiling (2,4 m): forjado de chapa grecada con viga, óxido en los valles, manchas de agua y hollín.
#   IndustrialPipe  (2 m de largo x perímetro): pintura verde gastada, cordón de soldadura, franja de señalización con flecha,
#                   óxido abajo y polvo arriba.
# Salida: _a (sRGB), _n (normal GL), _m (R metal, G AO, A suavidad).
import numpy as np, os, sys
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, map_coordinates, distance_transform_edt
C='/workspace/v760/cc0'; O=sys.argv[1] if len(sys.argv)>1 else '/workspace/v760/l4/tex'
os.makedirs(O, exist_ok=True)
def load(id, kind, mode='RGB'):
    d=f'{C}/{id}'; pre=[f for f in os.listdir(d) if f.endswith('_Color.jpg')][0][:-10]
    return np.asarray(Image.open(f'{d}/{pre}_{kind}.jpg').convert(mode),np.float32)/255
def resample(img, sx, sy, NX, NY, ox=0.0, oy=0.0):
    h,w=img.shape[:2]
    xs=((np.arange(NX)*(w*sx/NX))+ox*w).astype(np.int64)%w; ys=((np.arange(NY)*(h*sy/NY))+oy*h).astype(np.int64)%h
    return img[ys][:,xs]
def blurw(a, s, sy=None):
    sy=s if sy is None else sy
    return gaussian_filter(a, sigma=(sy,s) if a.ndim==2 else (sy,s,0), mode='wrap')
def noise(cells, seed, NX, NY, octaves=4, gain=0.5, cy=None):
    r=np.random.default_rng(seed); acc=np.zeros((NY,NX),np.float32); amp=1; tot=0
    cy=cells if cy is None else cy
    for o in range(octaves):
        cx_=int(cells*2**o); cyy=int(cy*2**o); g=r.random((cyy,cx_)).astype(np.float32)
        yy=(np.arange(NY)+0.5)/NY*cyy-0.5; xx=(np.arange(NX)+0.5)/NX*cx_-0.5
        Y,X=np.meshgrid(yy,xx,indexing='ij')
        up=map_coordinates(np.pad(g,2,mode='wrap'),[Y+2,X+2],order=3,mode='wrap')
        acc+=amp*up; tot+=amp; amp*=gain
    a=acc/tot; return (a-a.min())/(a.max()-a.min()+1e-6)
def pdist(a, c, L): return (a-c+L/2)%L-L/2
def ss(e0,e1,x): t=np.clip((x-e0)/(e1-e0),0,1); return t*t*(3-2*t)
def h2n(h, kx, ky=None, base=None, bk=1.0):
    ky=kx if ky is None else ky
    dx=(np.roll(h,-1,1)-np.roll(h,1,1))*0.5; dy=-(np.roll(h,-1,0)-np.roll(h,1,0))*0.5
    nx,ny=-dx*kx,-dy*ky
    if base is not None:
        b=base*2-1; nx=nx+bk*b[...,0]/np.maximum(b[...,2],0.2); ny=ny+bk*b[...,1]/np.maximum(b[...,2],0.2)
    l=np.sqrt(nx*nx+ny*ny+1); return np.stack([nx/l,ny/l,1/l],-1)*0.5+0.5
def lum(a): return a[...,0]*0.299+a[...,1]*0.587+a[...,2]*0.114
def mix(a,b,t):
    t=np.asarray(t,np.float32)
    if t.ndim==2: t=t[...,None]
    return a*(1-t)+b*t
def col(c): return np.array(c,np.float32)
def save(name, alb, nrm, ao, sm, metal):
    Image.fromarray(np.clip(alb*255+0.5,0,255).astype(np.uint8)).save(f'{O}/{name}_a.jpg',quality=93)
    Image.fromarray(np.clip(nrm*255+0.5,0,255).astype(np.uint8)).save(f'{O}/{name}_n.png')
    m=np.stack([metal,ao,np.zeros_like(ao),sm],-1)
    Image.fromarray(np.clip(m*255+0.5,0,255).astype(np.uint8),'RGBA').save(f'{O}/{name}_m.png')
    print(name,alb.shape,'lum',round(float(lum(alb).mean()),3),'std',round(float(lum(alb).std()),3),'smooth',round(float(sm.mean()),3),'metal',round(float(metal.mean()),3),'ao',round(float(ao.mean()),3),flush=True)
def grid(NX,NY):
    U,R=np.meshgrid((np.arange(NX)+0.5)/NX,(np.arange(NY)+0.5)/NY); return U.astype(np.float32),(1-R).astype(np.float32)
def downstreak(src, decay, jitter_seed, NX, NY, wob=0.0):
    # churretes: lo que hay arriba gotea hacia abajo (fila 0 = arriba), con decaimiento y variación por columna
    out=np.zeros_like(src); acc=np.zeros(NX,np.float32)
    r=np.random.default_rng(jitter_seed); colk=(0.75+0.25*r.random(NX)).astype(np.float32)
    colk=gaussian_filter(colk,2,mode='wrap')
    d=decay*colk
    for y in range(NY):
        acc=np.maximum(src[y], acc*d); out[y]=acc
    return out
def rivets(X, H, xs, ys, rad):
    # cúpulas de remache centradas en la rejilla de puntos (xs: lista de x, ys: lista de y), distancia en metros
    best=np.full(X.shape, 1e9, np.float32)
    return best
def warp(f, amt, seed, NX, NY, cells=8):
    wx=noise(cells,seed,NX,NY,4)-0.5; wy=noise(cells,seed+1,NX,NY,4)-0.5
    yy,xx=np.mgrid[0:NY,0:NX].astype(np.float32)
    return map_coordinates(f,[yy+wy*amt,xx+wx*amt],order=1,mode='wrap')
def chipfield(seed, NX, NY, cells=9, cy=None):
    # ruido fractal (ganancia alta = borde dentado) deformado: forma de desconchón de pintura real
    a=noise(cells,seed,NX,NY,6,0.62,cy=cy); a=warp(a,18*NX/2048,seed+7,NX,NY,cells*2)
    b=noise(cells*5,seed+3,NX,NY,3,0.6,cy=None if cy is None else cy*5)
    return 0.72*a+0.28*b
def oldrust(r):
    l=lum(r)[...,None]; t=mix(r, l*np.array([1.25,0.80,0.55],np.float32), 0.45)
    return np.clip(t*np.array([0.62,0.55,0.50],np.float32),0,1)
ONLY=os.environ.get('ONLY','')
rust=load('Rust009','Color'); rustN=load('Rust009','NormalGL'); rustR=load('Rust009','Roughness','L')
steel=load('Metal041B','Color'); steelR=load('Metal041B','Roughness','L')
dark=load('MetalPlates013','Color')

# =========================== PARED: u 3,6 m por repetición, v 0..1 = suelo..techo (3,4 m) ===========================
if ONLY in ('','wall'):
    N=2048; WU,WH=3.6,3.4
    U,V=grid(N,N); X=U*WU; Hh=V*WH; px=WU/N
    rows=[0.0,0.36,1.24,2.30,3.40]   # zócalo | panel bajo | panel alto | panel de arriba
    PW=1.2
    rowi=np.digitize(Hh,rows[1:-1])           # 0..3
    xoff=np.where(rowi==3,0.6,0.0)            # la fila de arriba va al tresbolillo
    Xp=(X+xoff)%WU
    coli=np.floor(Xp/PW).astype(np.int32)%3
    dxe=np.minimum(Xp%PW, PW-(Xp%PW))
    lo=np.choose(rowi,[rows[0],rows[1],rows[2],rows[3]]); hi=np.choose(rowi,[rows[1],rows[2],rows[3],rows[4]])
    dyl=Hh-lo; dyh=hi-Hh; dye=np.minimum(dyl,dyh)
    de=np.minimum(dxe,dye)
    # relieve (1 = 1 mm): chapa de 3 mm con canto redondeado; la de arriba monta sobre la de abajo (solape)
    plate=ss(0.0,0.005,de)*1.5
    height=plate + 3.0*(rowi%2)*ss(0.0,0.004,dyl)
    # abolladuras suaves por chapa
    height+=2.0*(noise(5,4400,N,N,3)-0.5)*ss(0.0,0.05,de)
    # remaches de cabeza redonda (r 7 mm, 4 mm de alto): dos filas en las juntas horizontales, una por lado en las verticales
    rv=np.zeros((N,N),np.float32); RAD=0.0075; P=0.075
    for sy in rows[1:-1]:
        for o,ph in ((-0.026,0.0),(0.026,P*0.5)):
            dy=Hh-(sy+o); dx=pdist(X,ph,P)
            d2=(dx*dx+dy*dy)/(RAD*RAD); rv=np.maximum(rv,np.sqrt(np.clip(1-d2,0,1)))
    for r_ in range(4):
        band=(rowi==r_)*(dye>0.05)
        xo=0.6 if r_==3 else 0.0
        for o in (-0.025,0.025):
            dx=pdist(X+xo, o, PW); dy=pdist(Hh-rows[r_],P*0.5,P)
            d2=(dx*dx+dy*dy)/(RAD*RAD); rv=np.maximum(rv,np.sqrt(np.clip(1-d2,0,1))*band)
    rv=rv*(Hh>0.02)*(Hh<WH-0.02)
    height+=5.0*rv
    # pintura por chapa: gris verdoso gastado arriba, más oscuro abajo; zócalo sin pintar
    rng=np.random.default_rng(4401)
    ptint=rng.uniform(0.86,1.12,(4,3)).astype(np.float32); phue=rng.uniform(-0.015,0.015,(4,3)).astype(np.float32)
    pk=ptint[rowi,coli]; ph=phue[rowi,coli]
    paintU=col([0.265,0.300,0.268]); paintL=col([0.165,0.175,0.158])
    paint=np.where((rowi>=2)[...,None], paintU, paintL)*pk[...,None]
    paint=paint+np.stack([ph,ph*0.3,-ph],-1)
    yb=ss(1.115,1.118,Hh)*(1-ss(1.217,1.220,Hh))
    paint=mix(paint,col([0.46,0.36,0.10]),yb*0.80)
    # suciedad de agua que baja (churretes oscuros finos) y manchado suave
    st=resample(steel,1.8,1.7,N,N); stl=lum(st); stl=stl/stl.mean()
    mott=noise(4,4402,N,N,5)
    runs=downstreak(ss(0.75,0.95,noise(40,4414,N,N,2))*(dye<0.06)*(rowi>0)*1.0, 0.997, 4415, N, N)
    runs=blurw(runs*ss(0.3,0.7,noise(80,4416,N,N,2,cy=3)),1.2,0.5)
    paint=paint*(0.86+0.18*mott[...,None])*(0.92+0.10*np.clip(stl,0.4,1.6)[...,None])*(1-0.38*runs)[...,None]
    dk=resample(dark,1.8,1.7,N,N,0.31,0.17)
    kick=dk*np.array([0.55,0.53,0.51],np.float32)
    # desconchones: borde fractal; más en bordes de chapa, alrededor de remaches y abajo; arriba pocos y pequeños
    ch=chipfield(4403,N,N,9)
    edge=np.exp(-de/0.03)+0.7*np.clip(blurw(rv,5)*2,0,1)
    low=1-ss(0.3,1.5,Hh)
    big=noise(2,4404,N,N,3)
    chip_t=ch+0.30*edge+0.16*low+0.22*(big-0.5)
    chip=ss(0.79,0.80,chip_t)
    halo=ss(0.66,0.79,chip_t)*(1-chip)           # óxido que asoma bajo la pintura (abombada y teñida)
    chip=np.where((rowi==0),1.0,chip); halo=np.where(rowi==0,0.0,halo)
    ru=oldrust(resample(rust,1.5,1.4,N,N,0.12,0.4))
    dkr=dk*np.array([0.42,0.36,0.31],np.float32)
    ru=mix(ru,dkr,0.35*noise(16,4417,N,N,3))
    bare=ss(0.83,0.86,chip_t+0.12*edge)*(rowi>0)*ss(0.4,0.6,noise(20,4418,N,N,3))
    under=mix(ru, st*np.array([0.50,0.50,0.51],np.float32), bare*0.7)
    under=np.where((rowi==0)[...,None], mix(kick,ru,ss(0.35,0.75,noise(6,4405,N,N,4)+0.4*(1-ss(0.0,0.3,Hh)))), under)
    alb=mix(paint,under,chip)
    alb=mix(alb, alb*np.array([0.78,0.66,0.55],np.float32), halo*0.8)
    cedge=np.clip(blurw(chip,1.0)-chip,0,1)*3
    height+=0.35*(1-chip)*(rowi>0) + 0.25*halo
    alb=alb*(1-0.18*np.clip(cedge,0,1))[...,None]
    # churretes de óxido desde desconchones, remaches y juntas
    srcs=np.clip(chip*(rowi>0)*ss(0.45,0.7,noise(26,4406,N,N,2)) + rv*1.0*ss(0.35,0.55,noise(30,4407,N,N,2)),0,1)
    stk=downstreak(srcs, 0.9982, 4408, N, N)
    stk=stk*ss(0.15,0.75,noise(70,4409,N,N,2,cy=4))
    stk=blurw(stk,1.3,0.6)
    alb=mix(alb, ru*np.array([1.05,0.85,0.70],np.float32), np.clip(stk*0.75,0,0.7)*(1-chip))
    # mugre abajo, polvo sobre los solapes, cercos de humedad
    grime=(1-ss(0.0,0.9,Hh))*(0.55+0.45*noise(8,4410,N,N,4))
    alb=alb*(1-0.40*grime)[...,None]
    dust=np.exp(-np.maximum(Hh-lo,0)/0.010)*(rowi>0)
    alb=mix(alb,col([0.30,0.27,0.22]),np.clip(dust*0.30,0,1))
    tide=noise(3,4411,N,N,4); tm=np.exp(-((tide-0.62)/0.02)**2)*0.25+ss(0.62,0.78,tide)*0.22
    alb=alb*(1-0.18*tm*(1-chip))[...,None]
    # rozaduras horizontales (0,45-1,05 m): finas, apagadas
    im=Image.new('L',(N,N),0); dr=ImageDraw.Draw(im); r2=np.random.default_rng(4412)
    for k in range(70):
        y=r2.uniform(0.45,1.05); x0=r2.uniform(0,WU); L_=r2.uniform(0.05,0.5); a_=r2.uniform(-0.04,0.04)
        for dxw in (0,WU,-WU):
            p0=((x0+dxw)/WU*N,(1-y/WH)*N); p1=((x0+dxw+L_)/WU*N,(1-(y+a_*L_)/WH)*N)
            dr.line([p0,p1],fill=int(r2.uniform(60,200)),width=1)
    scr=blurw(np.asarray(im,np.float32)/255,0.7)*(rowi>0)*(1-chip)
    alb=mix(alb, st*0.55, scr*0.35)
    rim=np.clip(blurw(rv,1.5)-rv,0,1)*3
    alb=alb*(1-0.5*np.clip(rim,0,1))[...,None]*(1+0.10*rv)[...,None]
    alb=mix(alb,ru,rv*ss(0.55,0.7,noise(50,4419,N,N,2))*0.8)
    alb=np.clip(alb,0,1)
    rr=resample(rustR,1.5,1.4,N,N,0.12,0.4)
    sm=0.36+0.10*mott-0.12*grime-0.1*halo
    sm=mix(sm[...,None],(0.13-0.07*rr)[...,None],chip)[...,0]
    sm=np.where(bare>0.5,0.40,sm)
    sm=sm*(1-0.55*np.clip(stk*0.7,0,1))*(1-0.2*runs)+0.12*scr
    metal=np.clip(bare*0.75+scr*0.4+(rowi==0)*0.2*(1-ss(0.35,0.75,noise(6,4405,N,N,4))),0,1)
    groove=np.exp(-de/0.0035)
    ao=np.clip(1-0.5*groove-0.35*np.clip(blurw(rv,3)-rv,0,1)*3-0.15*(1-ss(0.0,0.5,Hh)),0.3,1)
    rh=lum(ru); rh=(rh-rh.mean())/max(float(rh.std()),1e-3)
    micro=noise(160,4413,N,N,2)-0.5
    height+=chip*0.35*rh+0.12*micro*(1-chip)
    k=1.0/(px/0.001)*0.5   # 1 unidad de altura = 1 mm; px en metros (vertical casi igual)
    nrm=h2n(height, k, k*(WU/WH))
    save('IndustrialWall',alb,nrm,ao,np.clip(sm,0.02,0.9),metal)

# =========================== SUELO: chapa lagrimada 2,4 m ===========================
if ONLY in ('','floor'):
    N=4096; S=4.8; px=S/N   # v7.6.0b: 4,8 m (misma densidad que 2,4 m a 2048): el óxido, el aceite y el polvo no se repiten cada 2,4 m
    U,V=grid(N,N); X=U*S; Y=V*S
    P=0.040   # paso de los resaltes (chapa lagrimada de 5 mm)
    cx=np.floor(X/P); cy=np.floor(Y/P); lx=X-(cx+0.5)*P; ly=Y-(cy+0.5)*P
    sgn=np.where(((cx+cy)%2)==0,1.0,-1.0)
    c_,s_=np.cos(np.pi/4),np.sin(np.pi/4)
    ax=(lx*c_+ly*s_*sgn); ay=(-lx*s_*sgn+ly*c_)
    HL,HW=0.0140,0.0045
    qx=np.maximum(np.abs(ax)-HL+HW,0); d=np.sqrt(qx*qx+ay*ay)/HW
    lug=np.sqrt(np.clip(1-d*d,0,1))**0.7
    PWx,PH=1.2,2.4
    ry=np.floor(Y/PH); Xs=(X+0.6*(ry%2))%S   # filas de chapas al tresbolillo
    dxe=np.minimum(Xs%PWx,PWx-(Xs%PWx)); dye=np.minimum(Y%PH,PH-(Y%PH)); de=np.minimum(dxe,dye)
    seam=np.exp(-(de/0.0022)**2)
    lug=lug*ss(0.014,0.022,de)
    bolt=np.zeros((N,N),np.float32); bslot=np.zeros((N,N),np.float32)
    for side in (-0.032,0.032):
        dx=pdist(Xs,side,PWx); dy=pdist(Y,0.15,0.3); r=np.sqrt(dx*dx+dy*dy)
        bolt=np.maximum(bolt,ss(0.011,0.009,r)); bslot=np.maximum(bslot,(np.abs(dx)<0.0012)*(r<0.008))
        dy2=pdist(Y,side,PH); dx2=pdist(Xs,0.15,0.3); r2_=np.sqrt(dx2*dx2+dy2*dy2)
        bolt=np.maximum(bolt,ss(0.011,0.009,r2_)); bslot=np.maximum(bslot,(np.abs(dy2)<0.0012)*(r2_<0.008))
    lug=lug*(1-ss(0.0,1.0,blurw(bolt,4)))
    # relieve (1 = 1 mm): resalte 1,5 mm, junta 1 mm, tornillo avellanado
    height=1.5*lug - 1.0*seam - 0.6*bolt - 0.8*bslot + 0.3*bolt*(1-bslot)
    height+=1.2*(noise(8,4500,N,N,3)-0.5)   # chapa no del todo plana
    big=noise(6,4501,N,N,5); mid=noise(24,4502,N,N,4); fine=noise(120,4507,N,N,3)
    base=col([0.31,0.305,0.295])*(0.80+0.28*big[...,None])*(0.92+0.16*fine[...,None])
    # por chapa un tono un poco distinto
    pid=((np.floor(Xs/PWx)+ry)%2).astype(np.int32); base=base*np.where(pid==0,1.0,0.93)[...,None]
    wear=ss(0.25,0.75,noise(10,4503,N,N,4))
    top=ss(0.35,0.9,lug)
    alb=mix(base, col([0.56,0.55,0.53])*(0.9+0.2*fine[...,None]), top*wear*0.85)
    recess=(1-ss(0.0,0.3,lug))
    gr=ss(0.25,0.75,mid)
    alb=mix(alb, col([0.11,0.095,0.08]), recess*gr*0.45)
    ru=oldrust(resample(rust,2.4,2.4,N,N,0.55,0.1))
    rt=chipfield(4504,N,N,12)+0.16*np.exp(-de/0.04)*ss(0.45,0.7,noise(16,4509,N,N,3))
    rmask=ss(0.74,0.80,rt)*(1-0.7*top*wear)
    rbleed=ss(0.62,0.76,rt)*recess*(1-rmask)
    alb=mix(alb,ru,rmask)
    alb=mix(alb,ru*0.9,rbleed*0.6)
    oil=ss(0.80,0.86,chipfield(4505,N,N,6)); dust=ss(0.70,0.80,chipfield(4506,N,N,10))*(0.4+0.6*recess)
    alb=alb*(1-0.50*oil)[...,None]
    alb=mix(alb,col([0.34,0.31,0.27]),dust*0.30)
    alb=mix(alb,col([0.12,0.11,0.10]),seam*0.85)
    alb=mix(alb,col([0.46,0.45,0.44]),bolt*(1-bslot)*0.5)
    # pisadas/arrastres: rayas finas pulidas
    im=Image.new('L',(N,N),0); dr=ImageDraw.Draw(im); r2=np.random.default_rng(4508)
    for k in range(360):
        x0=r2.uniform(0,S); y0=r2.uniform(0,S); L_=r2.uniform(0.05,0.45); an=r2.uniform(0,np.pi)
        for ox in (0,S,-S):
            for oy in (0,S,-S):
                p0=((x0+ox)/S*N,(1-(y0+oy)/S)*N); p1=((x0+ox+L_*np.cos(an))/S*N,(1-(y0+oy+L_*np.sin(an))/S)*N)
                dr.line([p0,p1],fill=int(r2.uniform(60,200)),width=1)
    scr=blurw(np.asarray(im,np.float32)/255,0.6)*(1-rmask)
    alb=mix(alb,col([0.55,0.54,0.52]),scr*0.35)
    alb=np.clip(alb,0,1)
    metal=np.clip(0.60+0.30*top*wear-0.4*oil-0.65*rmask-0.3*rbleed-0.35*dust-0.3*recess*gr+0.2*scr,0,1)
    sm=np.clip(0.30+0.26*top*wear-0.22*rmask-0.12*rbleed-0.16*dust+0.10*oil-0.08*recess*gr+0.1*scr,0.04,0.75)
    ao=np.clip(1-0.22*recess*(1-ss(0.0,0.5,lug))-0.5*seam-0.3*bslot,0.4,1)
    rh=lum(ru); rh=(rh-rh.mean())/max(float(rh.std()),1e-3)
    height+=rmask*(0.35*rh-0.4)
    k=1.0/(px/0.001)*0.5
    nrm=h2n(height,k)
    save('IndustrialFloor',alb,nrm,ao,sm,metal)

# =========================== TECHO: chapa grecada + viga, 2,4 m ===========================
if ONLY in ('','ceil'):
    N=2048; S=2.4; px=S/N
    U,V=grid(N,N); X=U*S; Y=V*S
    PR=0.2   # paso de la greca (a lo largo de Y), 12 por repetición
    t=(Y%PR)/PR
    prof=np.interp(t,[0,0.28,0.40,0.62,0.74,1.0],[0,0,1,1,0,0]).astype(np.float32)
    prof=blurw(prof,0,1.2)
    bx=np.abs(pdist(X,0.0,S)); beam=ss(0.082,0.078,bx)
    # relieve (1 = 1 mm): greca 40 mm (se aplana a 12 para que el normal no sea exagerado en una cara plana), viga
    height=12.0*prof*(1-beam)+16.0*beam
    rvb=np.zeros((N,N),np.float32)
    for o in (-0.05,0.05):
        dx=pdist(X,o,S); dy=pdist(Y,0.0,0.15); d2=(dx*dx+dy*dy)/(0.008**2); rvb=np.maximum(rvb,np.sqrt(np.clip(1-d2,0,1)))
    height+=4.0*rvb*beam
    height+=1.5*(noise(6,4600,N,N,3)-0.5)
    big=noise(3,4601,N,N,5); fine=noise(50,4607,N,N,3)
    slope=np.abs(np.gradient(prof,axis=0))*12
    base=col([0.235,0.232,0.222])*(0.80+0.30*big[...,None])*(0.93+0.14*fine[...,None])
    base=base*(0.88+0.16*prof[...,None])
    beamc=col([0.20,0.185,0.17])*(0.85+0.3*noise(10,4602,N,N,4))[...,None]
    alb=mix(base,beamc,beam)
    valley=1-prof
    ru=oldrust(resample(rust,1.2,1.2,N,N,0.3,0.7))
    rt=chipfield(4603,N,N,5)
    rm=ss(0.76,0.82,rt+0.12*valley)*(1-0.6*beam)
    # en los valles se acumula agua: óxido en líneas finas a lo largo de la greca, a trozos
    vline=np.exp(-((t-0.14)/0.06)**2)*ss(0.55,0.70,noise(3,4604,N,N,4,cy=24))
    rm=np.maximum(rm,vline*0.8*(1-beam))
    alb=mix(alb,ru,rm*0.85)
    stain=noise(2,4605,N,N,5); tm=np.exp(-((stain-0.58)/0.02)**2)*0.35+ss(0.58,0.74,stain)*0.25
    alb=alb*(1-0.25*tm)[...,None]
    soot=ss(0.6,0.9,noise(3,4606,N,N,4)); alb=alb*(1-0.30*soot)[...,None]
    alb=mix(alb,ru*0.8,rvb*beam*0.5)
    alb=np.clip(alb,0,1)
    metal=np.clip(0.40*(1-rm)*(1-beam)-0.2*soot,0,1)
    sm=np.clip(0.28-0.18*rm-0.1*soot+0.04*beam,0.03,0.6)
    ao=np.clip(1-0.30*valley-0.35*np.exp(-np.abs(bx-0.08)/0.012)*(1-beam),0.4,1)
    rh=lum(ru); rh=(rh-rh.mean())/max(float(rh.std()),1e-3)
    height+=rm*0.5*rh
    k=1.0/(px/0.001)*0.5
    nrm=h2n(height,k)
    save('IndustrialCeiling',alb,nrm,ao,sm,metal)

# =========================== TUBERÍA: u = 2 m a lo largo, v = perímetro (0 arriba, 0,5 abajo) ===========================
if ONLY in ('','pipe'):
    NX,NY=2048,1024; L=2.0; CIRC=0.65
    U,V=grid(NX,NY); X=U*L; A=V
    down=0.5-0.5*np.cos(A*2*np.pi)
    paint=col([0.175,0.235,0.200])
    mott=noise(6,4701,NX,NY,5,cy=3)
    st=resample(steel,1.0,0.5,NX,NY,0.6,0.3); stl=lum(st)/lum(st).mean()
    alb=paint*(0.84+0.26*mott[...,None])*(0.92+0.10*np.clip(stl,0.5,1.5)[...,None])
    weld=np.exp(-(pdist(X,0.0,1.0)/0.005)**2)
    band=ss(0.545,0.548,X)*(1-ss(0.652,0.655,X))
    alb=mix(alb,col([0.50,0.39,0.10]),band*0.80)
    im=Image.new('L',(NX,NY),0); dr=ImageDraw.Draw(im)
    sx=NX/L; sy=NY/1.0
    for vc in (0.25,0.75):
        x0=0.565*sx; x1=0.635*sx; xh=0.612*sx; yc=vc*sy; hb=0.012/CIRC*sy; hh=0.026/CIRC*sy
        dr.polygon([(x0,yc-hb),(xh,yc-hb),(xh,yc-hh),(x1,yc),(xh,yc+hh),(xh,yc+hb),(x0,yc+hb)],fill=255)
    arrow=blurw(np.asarray(im,np.float32)/255,0.8)
    alb=mix(alb,col([0.05,0.05,0.045]),arrow*0.80)
    ch=chipfield(4702,NX,NY,12,cy=6)
    chip_t=ch+0.22*down+0.25*np.exp(-np.abs(pdist(X,0.0,1.0))/0.03)
    chip=ss(0.85,0.86,chip_t); halo=ss(0.74,0.85,chip_t)*(1-chip)
    ru=oldrust(resample(rust,1.0,0.5,NX,NY,0.2,0.2))
    alb=mix(alb,ru,chip)
    alb=mix(alb,alb*np.array([0.78,0.66,0.55],np.float32),halo*0.8)
    rb=ss(0.55,0.95,down)*ss(0.5,0.75,noise(30,4703,NX,NY,3,cy=4))
    alb=mix(alb,ru*0.9,rb*0.40*(1-chip))
    dust=ss(0.7,0.95,1-down)*(0.6+0.4*noise(8,4704,NX,NY,3))
    alb=mix(alb,col([0.33,0.30,0.25]),dust*0.35)
    alb=mix(alb,col([0.22,0.21,0.19]),weld*0.6)
    alb=np.clip(alb,0,1)
    sm=np.clip(0.38+0.1*mott-0.25*chip-0.1*halo-0.15*rb-0.25*dust,0.04,0.8)
    metal=np.clip(weld*0.3,0,1)
    ao=np.clip(1-0.12*down,0.6,1)
    rh=lum(ru); rh=(rh-rh.mean())/max(float(rh.std()),1e-3)
    height=0.3*(1-chip)+0.2*halo+1.5*weld+0.08*(noise(120,4705,NX,NY,2,cy=60)-0.5)+chip*0.3*rh
    nrm=h2n(height,0.5*0.001/(L/NX),0.5*0.001/(CIRC/NY))
    save('IndustrialPipe',alb,nrm,ao,sm,metal)
