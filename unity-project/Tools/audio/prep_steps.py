# v7.10.3: pasos reales CC0 (Kenney «Impact Sounds») -> Assets/Resources/Steps/<Tipo>_<i>.wav (mono, 22050 Hz, 16 bit)
import sys, numpy as np, soundfile as sf
from scipy.signal import butter, sosfilt, resample_poly
sys.path.insert(0,'/workspace/v7103'); from an import load; from lufs import lufs_active
K='/workspace/v7103/kenney/'; OUT=sys.argv[1] if len(sys.argv)>1 else '/workspace/v7103/steps_out'
SR=22050
import os; os.makedirs(OUT,exist_ok=True)
def get(name,i):
    d,sr=load(K+f'{name}_{i:03d}.ogg',44100); d=d-d.mean()
    return resample_poly(d,1,2)   # 22050
def hp(d,f,o=2): return sosfilt(butter(o,f,'hp',fs=SR,output='sos'),d)
def lp(d,f,o=4): return sosfilt(butter(o,f,'lp',fs=SR,output='sos'),d)
def pitch(d,r):  # r<1 = más grave
    from scipy.signal import resample
    return resample(d,int(len(d)/r))
def mix(*xs):
    n=max(len(x) for x,_ in xs); y=np.zeros(n)
    for x,g in xs: y[:len(x)]+=g*x
    return y
def finish(d, target):
    d=hp(d,45)              # sin retumbo infrasónico (< 45 Hz)
    d=lp(d,7000)            # sin siseo: nada por encima de 7 kHz
    # recorta la cola por debajo de -50 dB del pico y deja un fundido de 15 ms
    e=np.abs(d); pk=e.max(); idx=np.where(e>pk*10**(-50/20))[0]; end=min(len(d),idx[-1]+int(0.02*SR))
    d=d[:end]; f=int(0.015*SR); d[-f:]*=np.linspace(1,0,f)**2
    d=np.concatenate([np.zeros(int(0.002*SR)),d])
    for _ in range(4):      # normaliza al objetivo y limita el pico del golpe a 0,9 (limitador suave: ataque 0,5 ms, suelta 30 ms)
        g=10**((target-lufs_active(d,SR))/20); d=d*g
        d=limit(d,0.9)
    return d
def limit(d,thr):
    need=np.minimum(1,thr/np.maximum(np.abs(d),1e-9)); g=np.ones_like(d); cur=1.0
    rel=np.exp(-1/(0.030*SR)); la=int(0.0005*SR)
    need=np.array([need[max(0,i):i+la+1].min() for i in range(len(d))])
    for i in range(len(d)):
        cur=need[i] if need[i]<cur else min(need[i], 1-(1-cur)*rel); g[i]=cur
    return d*g
# objetivo: el mismo volumen activo (K) que los pasos procedurales de antes de cada tipo
T={'Carpet':-20.5,'Concrete':-16.5,'Metal':-19.0,'Grass':-17.5,'Sand':-21.0}   # ≈ 1–1,5 dB por debajo de los pasos de antes (medido en LUFS en el juego)
rep=[]
for i in range(5):
    S={}
    S['Carpet']=get('footstep_carpet',i)
    S['Concrete']=get('footstep_concrete',i)
    S['Grass']=get('footstep_wood',i)                    # suelo de las casas del Campo (madera: golpe sordo, sin crujido)
    c=get('footstep_concrete',i); pl=hp(get('impactPlate_light',i),180); mt=lp(get('impactMetal_light',i),4500)
    S['Metal']=mix((c,1.0),(pl,0.35),(mt,0.08))         # chapa del nivel industrial: pisada + vibración de la plancha
    sn=lp(get('footstep_snow',i),2200); ca=get('footstep_carpet',(i+2)%5)
    S['Sand']=mix((pitch(sn,0.9),0.8),(ca,0.5))          # arena: compresión blanda y granulada, apagada
    for k,d in S.items():
        d=finish(d,T[k]); sf.write(f'{OUT}/{k}_{i}.wav',d.astype(np.float32),SR,subtype='PCM_16')
        rep.append((k,i,len(d)/SR,np.abs(d).max(),lufs_active(d,SR)))
for r in rep: print("%s_%d dur=%.3f pk=%.2f lufs_act=%.1f"%r)
