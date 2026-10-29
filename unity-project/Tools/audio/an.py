import numpy as np, subprocess, sys, soundfile as sf
def load(p, sr=44100):
    if p.endswith('.ogg'):
        b=subprocess.run(['ffmpeg','-v','error','-i',p,'-ac','1','-ar',str(sr),'-f','f32le','-'],capture_output=True).stdout
        return np.frombuffer(b,np.float32).copy(), sr
    d,s=sf.read(p,dtype='float32'); 
    if d.ndim>1: d=d.mean(1)
    return d,s
def stats(d,sr):
    S=np.abs(np.fft.rfft(d*np.hanning(len(d)),n=max(4096,len(d))))**2; f=np.fft.rfftfreq(max(4096,len(d)),1/sr)
    tot=S.sum()+1e-20
    return dict(dur=len(d)/sr, peak=float(np.abs(d).max()), rms_db=20*np.log10(np.sqrt((d**2).mean())+1e-9),
      cent=float((S*f).sum()/tot), lo150=float(S[f<150].sum()/tot), hi4k=float(S[f>4000].sum()/tot), hi8k=float(S[f>8000].sum()/tot))
if __name__=='__main__':
    for p in sys.argv[1:]:
        d,sr=load(p); s=stats(d,sr); print(p.split('/')[-1], ' '.join(f"{k}={v:.3f}" for k,v in s.items()))
