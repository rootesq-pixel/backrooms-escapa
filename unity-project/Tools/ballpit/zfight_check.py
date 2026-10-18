# v7.1.1: busca caras coplanarias superpuestas mirando al mismo lado (parpadeo «z-fighting» y negro al hornear: la causa de
# la pared interior negra de las piscinas en v7.1.0). Uso: blender -b --factory-startup -P zfight_check.py -- <BallpitHall.bytes> [max_m2]
import sys, struct, math, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
argv = sys.argv[sys.argv.index('--') + 1:]; MAXA = float(argv[1]) if len(argv) > 1 else 0.005
with open(argv[0], 'rb') as f:
    nv, ns = struct.unpack('<ii', f.read(8)); V = np.frombuffer(f.read(nv * 40), np.float32).reshape(nv, 10)
    subs = [np.frombuffer(f.read(struct.unpack('<i', f.read(4))[0] * 4), np.int32) for _ in range(ns)]
NAMES = 'WALL FLOOR CEIL RED YEL BLU GRN PLY PLB NET EMIT BALL METAL ATLAS'.split()
P = V[:, 0:3].astype(np.float64)
tris = []; mats = []
for k, s in enumerate(subs):
    for t in s.reshape(-1, 3).tolist(): tris.append(t); mats.append(k)
bvh = BVHTree.FromPolygons([tuple(p) for p in P.tolist()], tris, epsilon=0.0)
rng = np.random.default_rng(3); cells = {}
def covered(x, nn):   # tapada por otra cara pegada en sentido contrario (contacto) -> no se ve
    return any(h[1].dot(nn) < -0.9 for h in bvh.find_nearest_range(Vector(x.tolist()) + nn * 0.0005, 0.0018))
for ti, t in enumerate(tris):
    if NAMES[mats[ti]] in ('NET', 'EMIT', 'BALL'): continue
    p = P[t]; n = np.cross(p[1] - p[0], p[2] - p[0]); a = 0.5 * np.linalg.norm(n)
    if a < 1e-7: continue
    n /= 2 * a; cnt = max(3, int(a * 400)); w = rng.random((cnt, 2)); fl = w.sum(1) > 1; w[fl] = 1 - w[fl]
    X = p[0] + w[:, :1] * (p[1] - p[0]) + w[:, 1:] * (p[2] - p[0]); nn = Vector(n.tolist())
    for x in X:
        for (loc, nor, idx, d) in bvh.find_nearest_range(Vector(x.tolist()), 0.0012):
            if idx != ti and nor.dot(nn) > 0.99 and not covered(x, nn):
                o = tris[idx]
                if len(set(o) & set(t)) >= 2: continue      # vecino de la misma cara (arista compartida)
                key = (NAMES[mats[ti]], NAMES[mats[idx]], int(math.floor(x[0] / 0.25)), int(math.floor(x[1] / 0.25)), int(math.floor(x[2] / 0.25)))
                cells[key] = cells.get(key, 0.0) + a / cnt; break
bad = sorted(((v, k) for k, v in cells.items() if v > MAXA), reverse=True)
print('ZFIGHT total %.3f m2, cells>%.3f: %d' % (sum(cells.values()), MAXA, len(bad)))
for v, k in bad[:30]: print('  %.4f m2 %s/%s ~(%.2f, %.2f, %.2f)' % (v, k[0], k[1], (k[2] + .5) * .25, (k[3] + .5) * .25, (k[4] + .5) * .25))
print('RESULT', 'FAIL' if bad else 'PASS')
sys.exit(1 if bad else 0)
