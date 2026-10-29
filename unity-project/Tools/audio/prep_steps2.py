# v7.10.3 ronda 2 (pt524u): rehace SOLO agua, arena, madera (Grass = casas del Campo) y metal.
# Moqueta y hormigón (aprobados) no se tocan. Salida: <OUT>/<Tipo>_<i>.wav (mono, 22050 Hz, 16 bit)
# Fuentes (descargar en /workspace/v7103/src2): https://opengameart.org/sites/default/files/footsteps_0.zip (congusbongus, CC-BY 3.0) y
#   https://opengameart.org/sites/default/files/Fantozzi-footsteps.7z (Fantozzi, CC0); Kenney «Impact Sounds» en /workspace/v7103/kenney.
import sys, os, numpy as np, soundfile as sf
from scipy.signal import butter, sosfilt, lfilter, resample_poly, resample
sys.path.insert(0,'/workspace/v7103'); from an import load; from lufs import kweight
SR=22050; K='/workspace/v7103/kenney/'; S2='/workspace/v7103/src2/'
OUT=sys.argv[1] if len(sys.argv)>1 else '/workspace/v7103/steps2'; os.makedirs(OUT,exist_ok=True)
def rd(p):
    if p.endswith('.flac') or p.endswith('.wav'):
        d,sr=sf.read(p); d=d.mean(1) if d.ndim>1 else d
    else: d,sr=load(p,44100)
    d=d-d.mean(); return resample_poly(d,SR,sr) if sr!=SR else d
def hp(d,f,o=2): return sosfilt(butter(o,f,'hp',fs=SR,output='sos'),d)
def lp(d,f,o=4): return sosfilt(butter(o,f,'lp',fs=SR,output='sos'),d)
def rate(d,r): return resample(d,int(len(d)/r))
def onset(d):
    e=np.abs(d); i=np.argmax(e>e.max()*0.05); return d[max(0,i-int(0.002*SR)):]
def mix(*xs):
    n=max(len(x)+int(o*SR) for x,g,o in xs); y=np.zeros(n)
    for x,g,o in xs: i=int(o*SR); y[i:i+len(x)]+=g*x
    return y
def modes(x, fr, tau, g):   # cuerpo del suelo: resonadores de 2 polos excitados por la propia pisada (decaen en decenas de ms: sin crujido)
    y=np.zeros(len(x)+int(0.25*SR)); xx=np.concatenate([x,np.zeros(int(0.25*SR))])
    for f,t,a in zip(fr,tau,g):
        r=np.exp(-1/(t*SR)); w=2*np.pi*f/SR
        y+=a*lfilter([1-r],[1,-2*r*np.cos(w),r*r],xx)
    return y
def short(d):
    k=kweight(d,SR); n=int(0.1*SR); w=np.convolve(np.concatenate([k,np.zeros(n)])**2,np.ones(n)/n,'same'); return -0.691+10*np.log10(w.max()+1e-12)
def limit(d,thr):
    need=np.minimum(1,thr/np.maximum(np.abs(d),1e-9)); la=int(0.0005*SR)
    need=np.array([need[i:i+la+1].min() for i in range(len(d))]); g=np.ones_like(d); cur=1.0; rel=np.exp(-1/(0.030*SR))
    for i in range(len(d)): cur=need[i] if need[i]<cur else min(need[i],1-(1-cur)*rel); g[i]=cur
    return d*g
def finish(d,target,pk=0.9,fade_in=0.0,lpf=7000):
    d=hp(d,45); d=lp(d,lpf)
    e=np.abs(d); idx=np.where(e>e.max()*10**(-50/20))[0]; d=d[:min(len(d),idx[-1]+int(0.02*SR))]
    f=int(0.015*SR); d[-f:]*=np.linspace(1,0,f)**2
    if fade_in>0: n=int(fade_in*SR); d[:n]*=np.sin(np.linspace(0,np.pi/2,n))**2
    d=np.concatenate([np.zeros(int(0.002*SR)),d])
    for _ in range(5): d=d*10**((target-short(d))/20); d=limit(d,pk)
    return d
T={'Water':-18.0,'Sand':-18.5,'Grass':-17.5,'Metal':-18.5}
rep=[]
sand=[S2+f'fantozzi/Fantozzi-footsteps/flac/Fantozzi-Sand{s}.flac' for s in ['L1','R1','L2','R3','L3']]
for i in range(5):
    out={}
    # AGUA (Poolrooms/Talasofobia): pisada real en charco (C-Dogs «water», derivada de EminYILDIRIM + swuing, CC-BY 3.0)
    w=rd(S2+f'footsteps/water/{i}.ogg'); w=hp(w,90)
    out['Water']=(onset(w),dict(pk=0.55,fade_in=0.004,lpf=5000))       # sin pico fuerte: ataque de 4 ms y pico limitado
    # ARENA (desierto 114): pisada real en arena/tierra (Fantozzi, CC0), apagada y más pesada: sin siseo ni golpe
    s=rd(sand[i]); s=rate(s,0.88); s=hp(s,110)
    s=lp(s,2000,4)+1.3*lp(s,600,2)                                       # granos suaves arriba + compresión sorda abajo
    out['Sand']=(onset(s),dict(pk=0.6,fade_in=0.010,lpf=3500))
    # MADERA (casas del Campo): golpe de tacón (pisada de hormigón aprobada) + golpe grave real de tarima (swuing, CC-BY 3.0)
    #   + cuerpo hueco del entarimado (modos 120–520 Hz que se apagan en 30–60 ms: sin crujidos)
    c=onset(rd(K+f'footstep_concrete_{i:03d}.ogg')); wb=onset(hp(rd(S2+f'footsteps/wood/{(i*2)%9}.ogg'),70))
    body=modes(lp(c,1500),[118,176,251,342,515],[0.050,0.040,0.034,0.028,0.020],[0.55,0.45,0.35,0.22,0.12])
    body=body/np.abs(body).max()*np.abs(c).max()
    knock=modes(hp(c,300),[640,905,1330],[0.016,0.012,0.009],[0.6,0.45,0.3]); knock=knock/np.abs(knock).max()*np.abs(c).max()   # «toc» seco de la tabla (madera dura), corto
    out['Grass']=(mix((lp(c,4500),0.6,0),(wb/np.abs(wb).max()*np.abs(c).max(),0.35,0.002),(body,0.6,0),(knock,0.45,0)),dict(pk=0.9,fade_in=0.0,lpf=6000))
    # METAL (suelo industrial): pisada de hormigón aprobada + chapa apoyada (modos graves muy amortiguados) y un roce metálico mínimo y corto
    body=modes(lp(c,1500),[165,240,355,520,780],[0.030,0.026,0.020,0.016,0.012],[0.5,0.4,0.3,0.2,0.12]); body=body/np.abs(body).max()*np.abs(c).max()
    ring=modes(hp(c,800),[1240,1710],[0.012,0.009],[0.5,0.35]); ring=ring/np.abs(ring).max()*np.abs(c).max()
    out['Metal']=(mix((c,0.85,0),(body,0.6,0),(ring,0.13,0)),dict(pk=0.9,fade_in=0.0,lpf=5000))
    for k,(d,kw) in out.items():
        d=finish(d,T[k],**kw); sf.write(f'{OUT}/{k}_{i}.wav',d.astype(np.float32),SR,subtype='PCM_16')
        rep.append((k,i,len(d)/SR,np.abs(d).max(),short(d)))
for r in sorted(rep): print("%s_%d dur=%.3f pk=%.2f corto=%.1f"%r)
