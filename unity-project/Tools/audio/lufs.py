import numpy as np
from scipy.signal import bilinear, lfilter
def kweight(x, sr):
    # BS.1770 etapa 1 (shelf) y 2 (paso alto), diseñados por bilinear para cualquier sr
    import math
    f0=1681.974450955533; G=3.999843853973347; Q=0.7071752369554196
    K=math.tan(math.pi*f0/sr); Vh=10**(G/20); Vb=Vh**0.4996667741545416
    a0=1+K/Q+K*K; b=[(Vh+Vb*K/Q+K*K)/a0, 2*(K*K-Vh)/a0, (Vh-Vb*K/Q+K*K)/a0]; a=[1,2*(K*K-1)/a0,(1-K/Q+K*K)/a0]
    y=lfilter(b,a,x)
    f0=38.13547087602444; Q=0.5003270373238773; K=math.tan(math.pi*f0/sr)
    a=[1,2*(K*K-1)/(1+K/Q+K*K),(1-K/Q+K*K)/(1+K/Q+K*K)]; b=[1,-2,1]
    return lfilter(b,a,y)
def lufs(x,sr):
    y=kweight(np.asarray(x,float),sr); return -0.691+10*np.log10((y**2).mean()+1e-12)
def lufs_active(x,sr,win=0.1):
    y=kweight(np.asarray(x,float),sr); n=int(win*sr); w=np.convolve(y**2,np.ones(n)/n,'same'); m=w>w.max()*0.01
    return -0.691+10*np.log10(w[m].mean()+1e-12)
