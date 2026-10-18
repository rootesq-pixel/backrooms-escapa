# v7.1.1: termina un lightmap crudo horneado en la GPU (Tools/gpu/run.ps1 + pack_raw.py en el PC):
#   lm_raw.npy | lm_raw16.npz  ->  OIDN RT_LIGHTMAP calidad HIGH  ->  escala opcional  ->  dilatación  ->  lm_dn.npy
# Uso: <oidnenv>/bin/python finish_lm.py <carpeta> [escala] [px_dilatacion]
import sys, os, time, numpy as np, pyoidn as o
D = sys.argv[1].rstrip('/') + '/'; S = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0; DIL = int(sys.argv[3]) if len(sys.argv) > 3 else 8
t0 = time.time()
a = (np.load(D + 'lm_raw.npy') if os.path.exists(D + 'lm_raw.npy') else np.load(D + 'lm_raw16.npz')['lm']).astype(np.float32)
a = np.nan_to_num(a, nan=0.0, posinf=0.0, neginf=0.0); a[a < 0] = 0
m = a.sum(2) > 0
dev = o.Device(); dev.commit(); f = o.Filter(dev, o.OIDN_FILTER_TYPE_RT_LIGHTMAP)
inp = np.ascontiguousarray(a); out = np.zeros_like(inp)
f.set_image(o.OIDN_IMAGE_COLOR, inp, o.OIDN_FORMAT_FLOAT3); f.set_image(o.OIDN_IMAGE_OUTPUT, out, o.OIDN_FORMAT_FLOAT3)
try: f.set_int('quality', o.OIDN_QUALITY_HIGH)
except Exception as e: print('quality', e)
f.commit(); f.execute()
out = np.maximum(out, 0) * S; out[~m] = 0
for _ in range(DIL):
    mm = out.sum(2) > 0; acc = np.zeros_like(out); cnt = np.zeros(m.shape, np.float32)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        s = np.roll(out, (dy, dx), (0, 1)); sm = np.roll(mm, (dy, dx), (0, 1)); acc += s * sm[..., None]; cnt += sm
    fill = (~mm) & (cnt > 0); out[fill] = acc[fill] / cnt[fill][:, None]
np.save(D + 'lm_dn.npy', out.astype(np.float32))
print('finish_lm', a.shape, 'scale', S, 'p5/50/95', np.percentile(out[m], [5, 50, 95]).round(4), '%.1fs' % (time.time() - t0))
