# Tras el horneado: irradiancia media de la superficie de bolas de cada piscina (para el shader de bolas instanciadas)
# y datos de la sala para Unity (Resources/Ballpit/BallpitHall.json). Uso: python3 post.py
import json, struct, numpy as np
B = '/workspace/ballpit/bake/'
lm = np.load(B + 'lm_dn.npy'); R = lm.shape[0]
hall = json.load(open(B + 'hall.json'))
with open(B + 'BallpitHall.bytes', 'rb') as f:
    nv, ns = struct.unpack('<ii', f.read(8))
    V = np.frombuffer(f.read(nv * 40), np.float32).reshape(nv, 10)
    subs = []
    for k in range(ns):
        c = struct.unpack('<i', f.read(4))[0]; subs.append(np.frombuffer(f.read(c * 4), np.int32))
M_BALL = 11
idx = np.unique(subs[M_BALL])
pos = V[idx, 0:3]; uv2 = V[idx, 8:10]
out = {'rim': hall['rim'], 'H': hall['H'], 'pits': []}
for p in hall['pits']:
    x0, x1, y0, y1 = p['r']
    sel = (pos[:, 0] > x0 - 0.01) & (pos[:, 0] < x1 + 0.01) & (pos[:, 2] > y0 - 0.01) & (pos[:, 2] < y1 + 0.01)
    # muestreo denso en el triángulo: aproximamos con una rejilla sobre el rectángulo UV de esos vértices
    u = uv2[sel]; umin, umax = u.min(0), u.max(0)
    px0, px1 = int(umin[0] * R), int(umax[0] * R); py0, py1 = int(umin[1] * R), int(umax[1] * R)
    patch = lm[py0:py1 + 1, px0:px1 + 1].reshape(-1, 3)
    patch = patch[patch.sum(1) > 0]
    irr = patch.mean(0).tolist() if len(patch) else [0.5, 0.5, 0.5]
    out['pits'].append(dict(x0=x0, x1=x1, z0=y0, z1=y1, floor=p['floor'], ball=p['ball'], rim=p['rim'], irr=[round(v, 4) for v in irr],
                            open=p['open'][0], oa=p['open'][1], ob=p['open'][2]))
    print('pit', p['r'], 'irr', irr)
out['lights'] = [dict(x=l[0], z=l[1], dead=int(bool(l[2]))) for l in hall['lights']]
out['party'] = hall.get('party', [])
json.dump(out, open(B + 'BallpitHall.json', 'w'), indent=1)
probes = hall['probes']
for pr in probes: pr['s'] = [round(v, 6) for s in pr['s'] for v in s]
json.dump({'probes': probes}, open(B + 'BallpitProbes.json', 'w'), separators=(',', ':'))
print('ok')
