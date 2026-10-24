# v7.5.0: texturas PBR del Nivel 3 (Hotel abandonado). Bases CC0 de ambientCG (Wood066 madera, Carpet012 pelo de moqueta,
# Fabric045 soporte del papel, Plaster001 yeso) + dibujo propio: damasco burdeos con brillo satinado, zócalo de madera con
# paneles, moldura (dado) y rodapié, cornisa de yeso; papel despegado en zonas, manchas de humedad y moho; moqueta burdeos con
# rosetones y rombos dorados, desgaste y manchas; techo de yeso con cercos de agua y grietas; puerta de madera de 4 paneles;
# madera lisa para marcos; atlas de números de latón grabados. Salida: _a (sRGB), _n (normal GL), _m (R metal, G AO, A suavidad).
import numpy as np, os, sys
from PIL import Image, ImageDraw, ImageFont
from scipy.ndimage import gaussian_filter, map_coordinates
C='/workspace/v750/cc0'; O=sys.argv[1] if len(sys.argv)>1 else '/workspace/v750/l3/tex'
os.makedirs(O, exist_ok=True); os.makedirs(O+'/Hotel', exist_ok=True)
def load(id, kind, mode='RGB'):
    d=f'{C}/{id}'; pre=[f for f in os.listdir(d) if f.endswith('_Color.jpg')][0][:-10]
    return np.asarray(Image.open(f'{d}/{pre}_{kind}.jpg').convert(mode),np.float32)/255
def resample(img, sx, sy, NX, NY, rot=False):
    if rot: img=np.swapaxes(img,0,1)
    h,w=img.shape[:2]
    xs=(np.arange(NX)*(w*sx/NX)).astype(np.int64)%w; ys=(np.arange(NY)*(h*sy/NY)).astype(np.int64)%h
    return img[ys][:,xs]
def blurw(a, s): return gaussian_filter(a, sigma=(s,s) if a.ndim==2 else (s,s,0), mode='wrap')
def noise(cells, seed, NX, NY, octaves=4, gain=0.5):
    r=np.random.default_rng(seed); acc=np.zeros((NY,NX),np.float32); amp=1; tot=0
    for o in range(octaves):
        c=int(cells*2**o); g=r.random((c,c)).astype(np.float32)
        yy=(np.arange(NY)+0.5)/NY*c-0.5; xx=(np.arange(NX)+0.5)/NX*c-0.5
        Y,X=np.meshgrid(yy,xx,indexing='ij')
        up=map_coordinates(np.pad(g,2,mode='wrap'),[Y+2,X+2],order=1,mode='wrap')
        acc+=amp*up; tot+=amp; amp*=gain
    a=acc/tot; return (a-a.min())/(a.max()-a.min()+1e-6)
def pdist(a, c, L): return (a-c+L/2)%L-L/2
def ss(e0,e1,x): t=np.clip((x-e0)/(e1-e0),0,1); return t*t*(3-2*t)
def h2n(h, k, base=None):
    dx=(np.roll(h,-1,1)-np.roll(h,1,1))*0.5; dy=-(np.roll(h,-1,0)-np.roll(h,1,0))*0.5
    nx,ny=-dx*k,-dy*k
    if base is not None:
        b=base*2-1; nx=nx+b[...,0]/np.maximum(b[...,2],0.2); ny=ny+b[...,1]/np.maximum(b[...,2],0.2)
    l=np.sqrt(nx*nx+ny*ny+1); return np.stack([nx/l,ny/l,1/l],-1)*0.5+0.5
def boost(nrm, k):
    b=nrm*2-1; b[...,0]*=k; b[...,1]*=k; b/=np.linalg.norm(b,axis=-1,keepdims=True); return b*0.5+0.5
def lum(a): return a[...,0]*0.299+a[...,1]*0.587+a[...,2]*0.114
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
    print(name,alb.shape,'lum',round(float(lum(alb).mean()),3),'std',round(float(lum(alb).std()),3),'smooth',round(float(sm.mean()),3),flush=True)
def grid(NX,NY):
    U,R=np.meshgrid((np.arange(NX)+0.5)/NX,(np.arange(NY)+0.5)/NY); return U.astype(np.float32),(1-R).astype(np.float32)
ONLY=os.environ.get('ONLY','')

wood=load('Wood066','Color'); woodN=load('Wood066','NormalGL'); woodR=load('Wood066','Roughness','L')
# ======================= PARED: u 1,6 m por repetición, v 0..1 = suelo..techo (2,8 m) =======================
if ONLY in ('','wall'):
    N=2048; WU,WH=1.6,2.8
    U,V=grid(N,N); X=U*WU; H=V*WH
    aa=1.5*WU/N
    walb=resample(wood,0.8,1.4,N,N); wn=boost(resample(woodN,0.8,1.4,N,N),1.3); wr=resample(woodR,0.8,1.4,N,N)
    g_=lum(walb); g_=np.clip(g_/g_.mean(),0.35,1.9)**1.3; walb=np.clip(np.array([0.165,0.062,0.036],np.float32)*g_[...,None],0,1)
    fab=load('Fabric045','Color','L'); fabN=load('Fabric045','NormalGL')
    paper=resample(fab,1.6,2.8,N,N); paper=paper/paper.mean()-1; pn=boost(resample(fabN,1.6,2.8,N,N),0.8)
    # v7.5.0b: damasco ornamental de verdad (damask.py: medallón con acantos, piña, corona, ogiva foliada y rosetas), 0,40 x 0,53 m
    sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); import damask
    TW,TH=0.40,0.53; dm=damask.tile(TW,TH,2400); dh,dw=dm.shape
    cu=(X%TW)/TW*dw; cv=(1-((H-0.99)%TH)/TH)*dh
    mot=map_coordinates(np.pad(dm,4,mode='wrap'),[cv+4,cu+4],order=1)
    mot=blurw(mot,0.5)
    edge_=np.clip(np.sqrt((np.roll(mot,1,0)-np.roll(mot,-1,0))**2+(np.roll(mot,1,1)-np.roll(mot,-1,1))**2)*1.6,0,1)
    band=ss(0.995,1.005,H)*(1-ss(2.635,2.645,H))
    gnd=np.array([0.20,0.030,0.040],np.float32); mc_=np.array([0.43,0.095,0.085],np.float32)
    alb=mix(np.ones((N,N,3),np.float32)*gnd,mc_,mot); alb=mix(alb,np.array([0.10,0.015,0.02],np.float32),edge_*0.5)
    alb*=(1+0.5*paper[...,None])
    fade=noise(3,301,N,N,5); alb*=(0.80+0.32*fade[...,None])
    sm=0.10+0.30*mot
    height=0.45*mot+0.3*paper
    seamd=np.abs(pdist(X,0,0.8))
    seam=np.exp(-(seamd/0.0015)**2)*band
    alb*=(1-0.35*seam[...,None]); height-=0.4*seam
    st=np.zeros((N,N),np.float32); rng=np.random.default_rng(302)
    for i in range(7):
        cx=rng.random()*WU; ln=0.4+rng.random()*1.2; w=0.05+0.12*rng.random(); top=2.64
        d=np.abs(pdist(X,cx,WU))/(w*(1+0.6*np.clip((top-H)/ln,0,1)))
        st+=np.exp(-d**2)*np.clip((top-H)/0.05,0,1)*np.exp(-np.clip(top-H,0,None)/ln)*(0.4+0.6*noise(20,303+i,N,N,3))
    st=np.clip(st,0,1)*band
    edge=np.clip(st*(1-st)*4,0,1)
    alb=mix(alb,alb*np.array([0.55,0.45,0.32],np.float32),st*0.75); alb=mix(alb,np.array([0.16,0.09,0.05],np.float32),edge*0.35)
    mold=np.clip((noise(8,304,N,N,5)-0.55)*4,0,1)*ss(2.25,2.62,H)*band
    alb=mix(alb,np.array([0.075,0.045,0.03],np.float32),mold*0.55)
    pl=load('Plaster001','Color','L'); plaster=resample(pl,1.6,2.8,N,N)
    pm=noise(4,305,N,N,5)
    peel=ss(0.765,0.795,pm*(0.80+0.30*ss(1.6,2.5,H)) + 0.10*np.exp(-(seamd/0.04)**2))*band
    ped=np.clip((blurw(peel,3)-peel)*3,0,1)
    shad=np.clip((np.roll(blurw(peel,4),12,axis=0)-peel)*2,0,1)
    tea=noise(6,309,N,N,4)
    plcol=mix(np.array([0.34,0.28,0.20],np.float32),np.array([0.27,0.19,0.11],np.float32),tea*0.8)*(0.65+0.55*plaster[...,None])
    alb=mix(alb,plcol,peel); alb=mix(alb,np.array([0.50,0.43,0.31],np.float32),ped*0.75); alb*=(1-0.45*shad[...,None])
    sm=sm*(1-peel)+0.05*peel; height=height*(1-peel)-0.6*peel+0.5*ped
    # zócalo de paneles 0,15-0,90; dado 0,90-0,99; rodapié 0-0,15; cornisa 2,64-2,8
    wz=ss(0.149,0.151,H)*(1-ss(0.899,0.901,H))
    PWd=0.8; inner=np.minimum(PWd/2-np.abs(pdist(X,PWd/2,PWd)), np.minimum(H-0.15,0.90-H))
    panel_h=np.clip((inner-0.07)/0.03,0,1)*0.9+np.clip((inner-0.10)/0.02,0,1)*0.3
    groove=np.exp(-((inner-0.068)/0.004)**2)
    wal=walb*(0.9+0.2*noise(6,306,N,N,3)[...,None])
    alb=mix(alb,wal*(1-0.35*groove[...,None]),wz)
    sm=sm*(1-wz)+wz*(0.30+0.3*(1-wr))
    height=height*(1-wz)+wz*(panel_h-1.0*groove)
    dz=ss(0.899,0.901,H)*(1-ss(0.989,0.991,H)); prof=np.cos(np.clip((H-0.945)/0.045,-1,1)*np.pi/2)
    alb=mix(alb,walb*0.8*(0.85+0.3*prof[...,None]),dz); sm=sm*(1-dz)+dz*0.42; height=height*(1-dz)+dz*(1.2+0.8*prof)
    sk=1-ss(0.149,0.151,H); bead=np.exp(-((H-0.135)/0.008)**2)
    alb=mix(alb,walb*0.7*(1-0.3*bead[...,None]),sk); sm=sm*(1-sk)+sk*0.35; height=height*(1-sk)+sk*(0.9+0.6*bead)
    cz=ss(2.639,2.641,H); cp=np.cos((H-2.72)/0.08*np.pi*1.5)
    alb=mix(alb,np.array([0.42,0.37,0.29],np.float32)*(0.8+0.25*cp[...,None])*(0.8+0.4*plaster[...,None]),cz); sm=sm*(1-cz)+cz*0.08; height=height*(1-cz)+cz*(1+0.6*cp)
    dust=ss(0.6,0.0,H)*(0.5+0.5*noise(12,307,N,N,4))
    alb*=(1-0.25*dust[...,None])
    scr=np.zeros((N,N),np.float32); r2=np.random.default_rng(308)
    for i in range(14):
        cx=r2.random()*WU; cy=0.2+r2.random()*0.65; L=0.05+0.25*r2.random(); a=(r2.random()-0.5)*0.4
        dx=pdist(X,cx,WU); dy=(H-cy)-dx*a
        scr+=np.exp(-(dy/0.0025)**2)*np.clip(1-np.abs(dx)/L,0,1)
    scr=np.clip(scr,0,1)*(wz+dz)
    alb=mix(alb,np.array([0.42,0.30,0.22],np.float32),scr*0.5); height-=0.3*scr
    ao=np.clip(1-0.5*groove-0.4*np.exp(-((H-0.15)/0.01)**2)-0.4*np.exp(-((H-2.64)/0.01)**2)-0.3*np.exp(-((H-0.90)/0.006)**2),0,1)
    base=mix(pn,wn,np.clip(wz+dz+sk,0,1))
    nrm=h2n(blurw(height,1.0),2.2,base)
    save('HotelWall',np.clip(alb,0,1),nrm,ao,np.clip(sm,0,1))

if ONLY in ('','carpet'):
    N=2048; U,V=grid(N,N); X=U*2.0; Y=V*2.0; aa=1.5*2.0/N
    cp_=load('Carpet012','Color','L'); cpn=load('Carpet012','NormalGL')
    fib=resample(cp_,2.0,2.0,N,N); fib=fib/fib.mean(); fn=boost(resample(cpn,2.0,2.0,N,N),1.4)
    T=0.5; lx=pdist(X,T/2,T)/(T/2); ly=pdist(Y,T/2,T)/(T/2); ax=np.abs(lx); ay=np.abs(ly)
    burg=np.array([0.33,0.045,0.05],np.float32); gold=np.array([0.60,0.41,0.16],np.float32); navy=np.array([0.045,0.055,0.13],np.float32)
    alb=np.ones((N,N,3),np.float32)*burg
    w8=aa*8
    dia=1-ss(0.042,0.042+w8,np.abs(ax+ay-1.0))
    dia2=1-ss(0.016,0.016+w8,np.abs(ax+ay-0.84))
    th=np.arctan2(ly,lx); rr=np.sqrt(lx**2+ly**2)
    ros=1-ss(0,w8,rr-0.36*(0.8+0.2*np.cos(8*th)))
    rring=1-ss(0.02,0.02+w8,np.abs(rr-0.24))
    rpet=1-ss(0,w8,rr-0.17*(0.6+0.4*np.abs(np.cos(4*th))))
    cdot=1-ss(0,w8,rr-0.05)
    crn=np.sqrt((1-ax)**2+(1-ay)**2); cth=np.arctan2(1-ay,1-ax)
    cflo=1-ss(0,w8,crn-0.20*(0.55+0.45*np.abs(np.cos(2*cth))))
    alb=mix(alb,navy,ros*0.95); alb=mix(alb,gold,rring); alb=mix(alb,gold*0.85,rpet*(1-cdot)); alb=mix(alb,burg*1.3,cdot)
    alb=mix(alb,gold*0.9,dia); alb=mix(alb,navy,dia2*0.8); alb=mix(alb,gold*0.8,cflo)
    pattern_h=0.2*(dia+rring+cflo)
    alb*=np.clip(1+0.35*(fib[...,None]-1),0.5,1.5)
    big=noise(3,401,N,N,5); alb*=(0.82+0.3*big[...,None])
    wear=np.clip((noise(4,402,N,N,5)-0.45)*2.2,0,1)
    gray=lum(alb)[...,None]*np.array([1.15,0.85,0.8],np.float32)
    alb=mix(alb,mix(alb,gray,0.5)*1.12,wear*0.55)
    stn=np.zeros((N,N),np.float32); ring_=np.zeros((N,N),np.float32); r3=np.random.default_rng(403)
    for i in range(14):
        cx,cy=r3.random()*2,r3.random()*2; R=0.06+0.25*r3.random()
        d=np.sqrt(pdist(X,cx,2)**2+pdist(Y,cy,2)**2)/R*(0.85+0.3*noise(10,404+i,N,N,3))
        stn+=np.clip(1-d,0,1)**0.4*(0.5+0.5*r3.random()); ring_+=np.exp(-((d-1)/0.06)**2)
    stn=np.clip(stn,0,1); ring_=np.clip(ring_,0,1)
    alb=mix(alb,alb*np.array([0.55,0.50,0.40],np.float32),stn*0.7); alb=mix(alb,np.array([0.10,0.06,0.04],np.float32),ring_*0.18)
    dust=np.clip((noise(6,415,N,N,5)-0.6)*3,0,1)
    alb=mix(alb,np.array([0.33,0.30,0.27],np.float32),dust*0.35)
    height=blurw(fib*0.5+pattern_h-0.4*wear,0.7)
    save('HotelCarpet',np.clip(alb,0,1),h2n(height,0.8,fn),np.clip(0.75+0.25*fib-0.15*stn,0,1),np.clip(0.04+0.06*stn+0.03*wear,0,1))

if ONLY in ('','ceil'):
    N=2048; U,V=grid(N,N); X=U*2.4; Y=V*2.4
    pc=load('Plaster001','Color','L'); pnm=load('Plaster001','NormalGL')
    p=resample(pc,1.2,1.2,N,N); pn2=boost(resample(pnm,1.2,1.2,N,N),1.2)
    alb=np.array([0.30,0.27,0.23],np.float32)*(0.8+0.35*p[...,None])
    alb*=(0.78+0.35*noise(3,501,N,N,5)[...,None])
    stn=np.zeros((N,N),np.float32); rim=np.zeros((N,N),np.float32); r4=np.random.default_rng(502)
    for i in range(6):
        cx,cy=r4.random()*2.4,r4.random()*2.4; R=0.15+0.45*r4.random()
        d=np.sqrt(pdist(X,cx,2.4)**2+pdist(Y,cy,2.4)**2)/R*(0.8+0.4*noise(8,503+i,N,N,4))
        stn+=np.clip(1-d,0,1)**0.5; rim+=np.exp(-((d-1)/0.04)**2)+0.5*np.exp(-((d-0.7)/0.03)**2)
    stn=np.clip(stn,0,1); rim=np.clip(rim,0,1)
    alb=mix(alb,alb*np.array([0.80,0.68,0.52],np.float32),stn*0.6); alb=mix(alb,np.array([0.17,0.11,0.06],np.float32),rim*0.3)
    cr=np.zeros((N,N),np.float32); cr_=np.random.default_rng(510)
    for k in range(8):
        q=np.array([cr_.random()*N, cr_.random()*N]); a=cr_.random()*6.28
        for s in range(int(200+cr_.random()*400)):
            a+=(cr_.random()-0.5)*0.7; q=(q+np.array([np.cos(a),np.sin(a)])*2.0)%N; cr[int(q[1])%N,int(q[0])%N]=1
    cr=np.clip(blurw(cr,0.8)*3,0,1)
    alb*=(1-0.6*cr[...,None])
    soot=np.clip((noise(5,511,N,N,5)-0.55)*3,0,1); alb*=(1-0.4*soot[...,None])
    height=blurw(p*0.5-0.8*cr,0.8)
    save('HotelCeiling',np.clip(alb,0,1),h2n(height,1.2,pn2),np.clip(1-0.4*cr,0,1),np.full((N,N),0.06,np.float32))

if ONLY in ('','door'):
    NX,NY=1024,2048; U,V=grid(NX,NY); X=U*0.92; Y=V*2.08
    dalb=resample(wood,0.5,1.1,NX,NY,rot=True); dn=resample(woodN,0.5,1.1,NX,NY,rot=True)
    dn=boost(np.stack([dn[...,1],dn[...,0],dn[...,2]],-1),1.2)
    g_=lum(dalb); g_=np.clip(g_/g_.mean(),0.35,1.9)**1.2; dalb=np.clip(np.array([0.235,0.095,0.055],np.float32)*g_[...,None],0,1)
    pan=[(0.12,1.08,0.43,1.92),(0.49,1.08,0.80,1.92),(0.12,0.14,0.43,0.92),(0.49,0.14,0.80,0.92)]
    hgt=np.zeros((NY,NX),np.float32); ao=np.ones((NY,NX),np.float32); grv=np.zeros((NY,NX),np.float32)
    for (x0,y0,x1,y1) in pan:
        d=np.minimum(np.minimum(X-x0,x1-X),np.minimum(Y-y0,y1-Y))
        hgt+=np.where(d>0,-0.5+np.clip(d/0.035,0,1)*0.9,0)
        g=np.exp(-(d/0.005)**2); grv+=g; ao-=0.35*g
    alb=dalb*(0.9+0.2*noise(5,601,NX,NY,3)[...,None])
    alb*=(1-0.6*np.clip(grv,0,1)[...,None]); alb*=(1+0.25*np.clip(hgt,-0.5,0.4)[...,None])
    hw=np.exp(-(((X-0.80)/0.10)**2+((Y-1.0)/0.18)**2))
    alb=mix(alb,alb*1.35+0.03,hw*0.6)
    alb*=(1-0.35*(ss(0.35,0.0,Y)*noise(10,602,NX,NY,3))[...,None])
    scr=np.zeros((NY,NX),np.float32); r5=np.random.default_rng(603)
    for i in range(18):
        cx,cy=r5.random()*0.92,r5.random()*2.0; L=0.03+0.15*r5.random(); a=(r5.random()-0.5)*2
        dx=X-cx; dy=(Y-cy)-dx*a
        scr+=np.exp(-(dy/0.0015)**2)*np.clip(1-np.abs(dx)/L,0,1)
    scr=np.clip(scr,0,1)
    alb=mix(alb,np.array([0.45,0.32,0.22],np.float32),scr*0.5)
    save('Hotel/HotelDoor',np.clip(alb,0,1),h2n(blurw(hgt-0.3*scr,1.0),6.0,dn),np.clip(ao,0,1),np.clip(0.42-0.2*hw-0.15*scr,0,1))
    NX=NY=1024
    g_=lum(resample(wood,0.5,0.5,NX,NY,rot=True)); g_=np.clip(g_/g_.mean(),0.35,1.9)**1.3; alb=np.clip(np.array([0.17,0.065,0.038],np.float32)*g_[...,None],0,1)
    nn=resample(woodN,0.5,0.5,NX,NY,rot=True); nn=boost(np.stack([nn[...,1],nn[...,0],nn[...,2]],-1),1.2)
    save('Hotel/HotelWood',np.clip(alb*(0.9+0.2*noise(4,610,NX,NY,3)[...,None]),0,1),nn,np.ones((NY,NX),np.float32)*0.95,np.full((NY,NX),0.40,np.float32))

if ONLY in ('','num'):
    NX,NY=1280,256
    img=Image.new('L',(NX,NY),0); dr=ImageDraw.Draw(img)
    font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf',170)
    for d in range(10):
        s=str(d); bb=dr.textbbox((0,0),s,font=font); w=bb[2]-bb[0]; hh=bb[3]-bb[1]
        dr.text((d*128+64-w/2-bb[0], 128-hh/2-bb[1]), s, fill=255, font=font)
    g=np.asarray(img,np.float32)/255
    brass=np.array([0.78,0.58,0.30],np.float32); dark=np.array([0.10,0.08,0.05],np.float32)
    tarn=noise(6,701,NX,NY,4)
    alb=np.ones((NY,NX,3),np.float32)*brass*(0.75+0.35*tarn[...,None])
    alb=mix(alb,np.array([0.30,0.26,0.16],np.float32),np.clip((tarn-0.6)*2.5,0,1)*0.6)
    alb=mix(alb,dark,g)
    save('Hotel/HotelNumbers',np.clip(alb,0,1),h2n(-blurw(g,1.2),3.0),np.clip(1-0.5*g,0,1),np.clip(0.62-0.3*tarn-0.4*g,0,1),metal=np.clip(1-g,0,1)*0.95)

# ======================= tela (se tiñe por material: colcha, sábanas, manteles, tapizado) =======================
if ONLY in ('','fab'):
    N=1024; fab=load('Fabric045','Color','L'); fabN=load('Fabric045','NormalGL')
    f=resample(fab,1.0,1.0,N,N); f=f/f.mean()
    alb=np.ones((N,N,3),np.float32)*0.62*np.clip(0.75+0.25*f,0.5,1.3)[...,None]
    st=np.clip((noise(4,801,N,N,5)-0.55)*3,0,1); alb=mix(alb,alb*np.array([0.72,0.62,0.48],np.float32),st*0.7)
    dust=noise(8,802,N,N,4); alb*=(0.85+0.25*dust[...,None])
    save('Hotel/HotelFabric',np.clip(alb,0,1),boost(resample(fabN,1.0,1.0,N,N),1.2),np.clip(0.8+0.2*f-0.2*st,0,1),np.full((N,N),0.08,np.float32))
# ======================= cuadros: atlas 4 x 2 de óleos oscuros (paisajes, retratos sin cara, marinas, bodegón) =======================
if ONLY in ('','paint'):
    CWp,CHp=512,640; A=np.zeros((CHp*2,CWp*4,3),np.float32); r=np.random.default_rng(901)
    yy,xx=np.meshgrid(np.linspace(0,1,CHp),np.linspace(0,1,CWp),indexing='ij')
    def craq(seed):
        rr=np.random.default_rng(seed); pts=rr.random((900,2)); d1=np.full((CHp,CWp),9.0); d2=np.full((CHp,CWp),9.0)
        for p in pts:
            dd=np.sqrt((xx-p[0])**2+((yy-p[1])*1.25)**2); m=dd<d1; d2=np.where(m,d1,np.minimum(d2,dd)); d1=np.where(m,dd,d1)
        return np.exp(-((d2-d1)/0.0035)**2)
    for k in range(8):
        img=np.zeros((CHp,CWp,3),np.float32); kind=[0,1,2,0,1,3,2,0][k]; n1=noise(4,910+k,CWp,CHp,5); n2=noise(12,930+k,CWp,CHp,4)
        if kind==0:   # paisaje: cielo ocre, colinas, árboles
            sky=mix(np.array([0.55,0.45,0.28],np.float32)*np.ones((CHp,CWp,3)),np.array([0.30,0.25,0.18],np.float32),yy[...,None]*0)
            img=mix(np.array([0.62,0.52,0.32],np.float32)*np.ones((CHp,CWp,3)),np.array([0.30,0.27,0.20],np.float32),(1-yy)[...,None]*0.8)
            img*=(0.8+0.4*n1[...,None])
            hz=0.55+0.08*np.sin(xx*6+k)+0.05*(n1-0.5)
            img=mix(img,np.array([0.16,0.17,0.10],np.float32)*(0.7+0.6*n2[...,None]),ss(hz-0.01,hz+0.01,yy))
            hz2=0.68+0.06*np.sin(xx*9+2*k)+0.06*(n2-0.5)
            img=mix(img,np.array([0.10,0.10,0.06],np.float32)*(0.7+0.6*n2[...,None]),ss(hz2-0.01,hz2+0.01,yy))
            for t in range(3+k%3):
                cx=r.random(); cy=0.45+r.random()*0.2; rad=0.06+0.08*r.random()
                tr=np.clip(1-np.sqrt(((xx-cx)/rad)**2+((yy-cy)/(rad*1.4))**2)+0.3*(n2-0.5),0,1)
                img=mix(img,np.array([0.06,0.07,0.04],np.float32),ss(0.0,0.15,tr))
        elif kind==1:  # retrato sin cara
            img=np.array([0.10,0.08,0.06],np.float32)*(0.7+0.6*n1[...,None])*np.ones((CHp,CWp,3))
            head=np.sqrt(((xx-0.5)/0.17)**2+((yy-0.36)/0.22)**2); body=np.sqrt(((xx-0.5)/0.42)**2+((yy-1.05)/0.45)**2)
            img=mix(img,np.array([0.05,0.05,0.06],np.float32)*(0.8+0.4*n2[...,None]),ss(1.02,0.98,body))
            skin=np.array([0.55,0.40,0.30],np.float32)*(0.75+0.35*n2[...,None])*(1-0.35*ss(0.3,1.0,head))[...,None]
            img=mix(img,skin,ss(1.03,0.97,head))
            blur_=np.exp(-(((xx-0.5)/0.09)**2+((yy-0.38)/0.06)**2)); img=mix(img,np.array([0.28,0.18,0.13],np.float32),blur_*0.55)   # rasgos borrados
            img=mix(img,np.array([0.03,0.03,0.03],np.float32),ss(1.0,0.9,np.sqrt(((xx-0.5)/0.19)**2+((yy-0.27)/0.17)**2))*(yy<0.31)*0.9)
        elif kind==2:  # marina con barco
            img=mix(np.array([0.48,0.44,0.34],np.float32)*np.ones((CHp,CWp,3)),np.array([0.20,0.22,0.22],np.float32),ss(0.45,0.0,yy)[...,None]*0.6)*(0.8+0.4*n1[...,None])
            sea=ss(0.57,0.59,yy); img=mix(img,np.array([0.09,0.12,0.12],np.float32)*(0.6+0.8*n2[...,None]),sea)
            sx=0.3+0.4*r.random(); hull=(np.abs(xx-sx)<0.08*(1-(yy-0.555)/0.03))*(yy>0.53)*(yy<0.585)
            mast=(np.abs(xx-sx)<0.004)*(yy>0.33)*(yy<0.56); sail=((xx-sx)>0.005)*((xx-sx)<0.07*(yy-0.35)/0.2)*(yy>0.35)*(yy<0.55)
            img=mix(img,np.array([0.05,0.04,0.03],np.float32),np.clip(hull+mast,0,1)); img=mix(img,np.array([0.40,0.36,0.27],np.float32),sail*0.9)
        else:          # bodegón: jarrón con flores secas
            img=np.array([0.09,0.07,0.05],np.float32)*(0.7+0.6*n1[...,None])*np.ones((CHp,CWp,3))
            vase=(np.abs(xx-0.5)<0.12*(1+0.6*np.sin((yy-0.6)/0.3*3.14)))*(yy>0.6)*(yy<0.9); img=mix(img,np.array([0.35,0.28,0.16],np.float32)*(0.7+0.5*n2[...,None]),vase)
            for t in range(9):
                cx=0.5+(r.random()-0.5)*0.5; cy=0.25+r.random()*0.3; rad=0.03+0.04*r.random()
                fl=np.sqrt((xx-cx)**2+(yy-cy)**2)/rad; img=mix(img,np.array([0.45,0.18,0.12],np.float32)*(0.6+0.6*r.random()),ss(1.1,0.8,fl))
            img=mix(img,np.array([0.25,0.22,0.15],np.float32),(yy>0.9)*0.8)
        img=img*np.array([0.95,0.85,0.62],np.float32)   # barniz amarillento
        vig=np.sqrt((xx-0.5)**2+(yy-0.5)**2); img*=(1-0.55*ss(0.25,0.75,vig))[...,None]
        img=blurw(img,1.2); img*=(0.92+0.16*noise(40,960+k,CWp,CHp,2)[...,None]); cq=craq(950+k); img*=(1-0.13*cq[...,None])
        img*=0.75
        cx_,cy_=(k%4)*CWp,(k//4)*CHp; A[cy_:cy_+CHp,cx_:cx_+CWp]=img
    hgt=blurw(lum(A),1.2)*0.6
    save('Hotel/HotelPaintings',np.clip(A,0,1),h2n(hgt,3.0),np.ones(A.shape[:2],np.float32),np.full(A.shape[:2],0.35,np.float32))
# ======================= pasillo de servicio: pared pintada (verde grisáceo, banda inferior oscura) y linóleo gastado =======================
if ONLY in ('','svc'):
    N=1024; U,V=grid(N,N); X=U*1.6; H=V*2.8
    pl=load('Plaster001','Color','L'); p=resample(pl,1.6,2.8,N,N); pn=boost(resample(load('Plaster001','NormalGL'),1.6,2.8,N,N),0.8)
    up=np.array([0.42,0.43,0.36],np.float32); lo=np.array([0.20,0.23,0.20],np.float32)
    alb=mix(np.ones((N,N,3),np.float32)*up,lo,1-ss(1.19,1.21,H))*(0.8+0.35*p[...,None])
    line=np.exp(-((H-1.2)/0.012)**2); alb=mix(alb,np.array([0.12,0.10,0.08],np.float32),line*0.6)
    grime=ss(0.5,0.0,H)*(0.5+0.5*noise(10,1001,N,N,4)); alb*=(1-0.45*grime[...,None])
    st=np.zeros((N,N),np.float32); rng=np.random.default_rng(1002)
    for i in range(6):
        cx=rng.random()*1.6; ln=0.5+rng.random()*1.5; w=0.04+0.1*rng.random()
        d=np.abs(pdist(X,cx,1.6))/w; st+=np.exp(-d**2)*np.exp(-np.clip(2.8-H,0,None)/ln)
    alb=mix(alb,alb*np.array([0.6,0.5,0.35],np.float32),np.clip(st,0,1)*0.7)
    chip=np.clip((noise(6,1003,N,N,5)-0.68)*6,0,1); alb=mix(alb,np.array([0.32,0.28,0.22],np.float32)*(0.8+0.3*p[...,None]),chip)
    scr=np.zeros((N,N),np.float32)
    for i in range(30):
        cx=rng.random()*1.6; cy=0.3+rng.random()*0.8; L=0.05+0.3*rng.random(); dx=pdist(X,cx,1.6); dy=(H-cy)
        scr+=np.exp(-(dy/0.003)**2)*np.clip(1-np.abs(dx)/L,0,1)
    alb=mix(alb,np.array([0.10,0.09,0.08],np.float32),np.clip(scr,0,1)*0.5)
    save('Hotel/HotelService',np.clip(alb*0.8,0,1),h2n(blurw(p*0.4-0.5*chip,1),1.5,pn),np.ones((N,N),np.float32),np.clip(0.30-0.2*grime-0.2*chip,0,1))
    U,V=grid(N,N); X=U*2.0; Y=V*2.0; T=0.33
    tx=np.floor(X/T); ty=np.floor(Y/T); chk=((tx+ty)%2)
    base=mix(np.array([0.36,0.33,0.27],np.float32)*np.ones((N,N,3)),np.array([0.16,0.15,0.13],np.float32),chk[...,None])
    alb=base*(0.85+0.3*noise(8,1010,N,N,4)[...,None])
    gap=np.minimum(np.abs(pdist(X,0,T)),np.abs(pdist(Y,0,T))); gl=np.exp(-(gap/0.004)**2)
    alb=mix(alb,np.array([0.06,0.05,0.04],np.float32),gl*0.7)
    wear=np.clip((noise(3,1011,N,N,5)-0.5)*2.5,0,1); alb=mix(alb,np.array([0.25,0.22,0.18],np.float32),wear*0.4)
    lift=np.clip((noise(10,1012,N,N,4)-0.72)*6,0,1); alb=mix(alb,np.array([0.13,0.11,0.09],np.float32),lift*0.8)
    save('Hotel/HotelLino',np.clip(alb,0,1),h2n(blurw(-gl*0.5+lift*0.6,1),1.5),np.clip(1-0.4*gl,0,1),np.clip(0.35-0.25*wear,0,1))
