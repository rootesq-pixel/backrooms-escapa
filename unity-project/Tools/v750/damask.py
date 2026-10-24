# v7.5.0: motivo de damasco propio (dibujado con curvas de Bézier): medallón central con hojas de acanto dentadas, palmeta en abanico,
# capullo y remate, volutas en C, marco de ogiva con hojitas (enlaza con las repeticiones vecinas) y flor de lis pequeña en las
# esquinas (repetición a media altura). Simétrico respecto al eje vertical. Devuelve una máscara 0..1 de una repetición.
import numpy as np
from PIL import Image, ImageDraw
def bez(p0,p1,p2,p3,n=40):
    t=np.linspace(0,1,n)[:,None]; p0,p1,p2,p3=map(np.array,(p0,p1,p2,p3))
    return (1-t)**3*p0+3*(1-t)**2*t*p1+3*(1-t)*t**2*p2+t**3*p3
def normals(c):
    d=np.gradient(c,axis=0); n=np.stack([-d[:,1],d[:,0]],1); return n/(np.linalg.norm(n,axis=1,keepdims=True)+1e-9)
def tile(TW=0.40, TH=0.53, ppm=2400):
    W,H=int(TW*ppm),int(TH*ppm)
    img=Image.new('L',(W,H),0); d=ImageDraw.Draw(img)
    def P(x,y,cx,cy,m): return ((cx+m*x)*ppm, (TH-(cy+y))*ppm)   # metros -> píxeles (y hacia arriba)
    def poly(pts,cx,cy,m,fill=255):
        for mm in ((m,) if m==0 else (1,-1)):
            d.polygon([P(x,y,cx,cy,mm) for x,y in pts],fill=fill)
    def stroke(pts,wd,cx,cy,mirror=True,fill=255):
        for mm in ((1,-1) if mirror else (1,)):
            q=[P(x,y,cx,cy,mm) for x,y in pts]; d.line(q,fill=fill,width=max(1,int(wd*ppm)),joint='curve')
            r=wd*ppm/2
            for e in (q[0],q[-1]): d.ellipse([e[0]-r,e[1]-r,e[0]+r,e[1]+r],fill=fill)
    def leaf(c1,c2,cx,cy,mirror=True,fill=255):   # hoja entre dos curvas con el mismo inicio y fin
        pts=np.concatenate([c1,c2[::-1]]); 
        for mm in ((1,-1) if mirror else (1,)): d.polygon([P(x,y,cx,cy,mm) for x,y in pts],fill=fill)
    def serrated(c,amp,lobes):
        n=normals(c); t=np.linspace(0,1,len(c)); s=np.abs(np.sin(t*np.pi*lobes))**0.6*amp*np.sin(t*np.pi)
        return c+n*s[:,None]
    def spiral(cx0,cy0,r0,turns,wd,cx,cy,a0=0,dirn=1,mirror=True):
        th=np.linspace(0,turns*2*np.pi,120); r=r0*(1-th/(turns*2*np.pi)*0.85)
        pts=np.stack([cx0+r*np.cos(a0+dirn*th), cy0+r*np.sin(a0+dirn*th)],1); stroke(pts,wd,cx,cy,mirror)
    def main(cx,cy):
        # piña central con rombos calados
        body_o=bez((0,-0.095),(0.06,-0.08),(0.05,0.05),(0,0.085)); leaf(body_o,body_o*[0,1],cx,cy)
        for k in range(-3,4):
            for j in (-1,0,1):
                x=j*0.017+(0.0085 if k%2 else 0); y=k*0.019-0.004
                if abs(x)>0.032-abs(y)*0.25: continue
                q=[P(x+dx,y+dy,cx,cy,1) for dx,dy in ((0,0.0065),(0.0055,0),(0,-0.0065),(-0.0055,0))]; d.polygon(q,fill=0)
        # hojas de la base, hacia abajo y afuera, dentadas, con voluta
        o=serrated(bez((0.01,-0.085),(0.06,-0.19),(0.16,-0.20),(0.175,-0.12)),0.010,5)
        i=bez((0.01,-0.085),(0.07,-0.13),(0.13,-0.14),(0.175,-0.12)); leaf(o,i,cx,cy)
        spiral(0.158,-0.125,0.02,1.25,0.0065,cx,cy,a0=0.3,dirn=1)
        o=bez((0.0,-0.09),(0.025,-0.12),(0.03,-0.16),(0.0,-0.215)); leaf(o,o*[0,1],cx,cy)
        o=bez((0.0,-0.115),(0.012,-0.13),(0.014,-0.16),(0.0,-0.19)); leaf(o,o*[0,1],cx,cy,fill=0)
        o=bez((0.005,-0.2),(0.03,-0.21),(0.05,-0.24),(0.07,-0.235)); i=bez((0.005,-0.2),(0.03,-0.22),(0.045,-0.23),(0.07,-0.235)); leaf(o,i,cx,cy)
        # grandes acantos en llama: salen de la piña, se abren y suben curvándose hacia dentro
        o=serrated(bez((0.03,-0.05),(0.19,-0.07),(0.20,0.10),(0.115,0.175)),0.014,6)
        i=bez((0.03,-0.05),(0.10,-0.02),(0.14,0.08),(0.115,0.175)); leaf(o,i,cx,cy)
        stroke(bez((0.04,-0.045),(0.12,-0.04),(0.155,0.06),(0.12,0.16)),0.0045,cx,cy,fill=0)
        for t in (0.25,0.45,0.65):
            c_=bez((0.04,-0.045),(0.12,-0.04),(0.155,0.06),(0.12,0.16)); p=c_[int(t*39)]; n=normals(c_)[int(t*39)]
            stroke(np.array([p,p-n*0.022]),0.003,cx,cy,fill=0)
        spiral(0.097,0.165,0.022,1.35,0.0065,cx,cy,a0=0.2,dirn=1)
        # hoja interior pequeña
        o=bez((0.02,0.04),(0.07,0.04),(0.09,0.09),(0.06,0.13)); i=bez((0.02,0.04),(0.04,0.07),(0.05,0.1),(0.06,0.13)); leaf(o,i,cx,cy)
        # corona: flor de tres pétalos sobre la piña
        c1=bez((0,0.08),(0.03,0.12),(0.025,0.18),(0,0.235)); leaf(c1,c1*[0,1],cx,cy)
        c2=bez((0,0.12),(0.012,0.15),(0.01,0.18),(0,0.205)); leaf(c2,c2*[0,1],cx,cy,fill=0)
        a_=bez((0.01,0.10),(0.05,0.11),(0.07,0.15),(0.05,0.19)); b_=bez((0.01,0.10),(0.03,0.13),(0.04,0.16),(0.05,0.19)); leaf(a_,b_,cx,cy)
        spiral(0.045,0.19,0.012,1.1,0.004,cx,cy,a0=0,dirn=1)
        e=P(0,0.25,cx,cy,1); r=0.008*ppm; d.ellipse([e[0]-r,e[1]-r,e[0]+r,e[1]+r],fill=255)
        # marco de ogiva foliado (enlaza con las repeticiones vecinas)
        for sgn in (1,-1):
            og=bez((0.0,0.265*sgn),(0.07,0.25*sgn),(0.205,0.17*sgn),(0.2,0.0)); og=og*[1,1]
            stroke(og,0.0065,cx,cy)
            nn=normals(og)
            for k,t in enumerate(np.linspace(0.12,0.88,7)):
                j=int(t*39); p=og[j]; n=nn[j]*(1 if k%2 else -1)*sgn; tg=og[min(39,j+3)]-og[max(0,j-3)]; tg/=np.linalg.norm(tg)
                tip=p+n*0.026+tg*0.012
                l1=bez(p,p+n*0.01+tg*0.012,tip-tg*0.002+n*0.004,tip); l2=bez(p,p+n*0.014-tg*0.004,tip-tg*0.01,tip); leaf(l1,l2,cx,cy)
    def fleur(cx,cy,s=1.0):
        # roseta de cuatro pétalos con hojitas en aspa (en los cruces de las ogivas)
        for ang in (0,90,180,270):
            a_=np.radians(ang); dv=np.array([np.cos(a_),np.sin(a_)]); nv=np.array([-dv[1],dv[0]])
            c1=bez((0,0),dv*0.02+nv*0.016,dv*0.04+nv*0.012,dv*0.05); c2=bez((0,0),dv*0.02-nv*0.016,dv*0.04-nv*0.012,dv*0.05)
            leaf(c1*s,c2*s,cx,cy,mirror=False)
        for ang in (45,135,225,315):
            a_=np.radians(ang); dv=np.array([np.cos(a_),np.sin(a_)]); nv=np.array([-dv[1],dv[0]])
            c1=bez(dv*0.015,dv*0.03+nv*0.008,dv*0.045+nv*0.006,dv*0.06); c2=bez(dv*0.015,dv*0.03-nv*0.008,dv*0.045-nv*0.006,dv*0.06)
            leaf(c1*s,c2*s,cx,cy,mirror=False)
        e=P(0,0,cx,cy,1); r=0.012*s*ppm; d.ellipse([e[0]-r,e[1]-r,e[0]+r,e[1]+r],fill=0); r=0.006*s*ppm; d.ellipse([e[0]-r,e[1]-r,e[0]+r,e[1]+r],fill=255)
    main(TW/2, TH/2)
    for (x,y) in ((0,0),(TW,0),(0,TH),(TW,TH)): fleur(x,y,1.0)
    m=np.asarray(img,np.float32)/255
    return m
if __name__=='__main__':
    m=tile(); print(m.shape, m.mean())
    t=np.tile(m,(2,4)); Image.fromarray((t*255).astype(np.uint8)).resize((t.shape[1]//3,t.shape[0]//3),Image.LANCZOS).save('/tmp/damask_preview.png')
