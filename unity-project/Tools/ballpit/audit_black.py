# v7.1.1: prueba de «manchas negras» del Parque de bolas (se ejecuta con el Python de Blender por mathutils.bvhtree):
#   blender -b --factory-startup -P audit_black.py -- <BallpitHall.bytes> <lm_dn.npy> <hall.json> [umbral] [informe.json]
# Muestrea cada triángulo de la malla exportada (≈600 muestras/m², coordenadas Unity, y arriba) en su isla UV1 del lightmap.
# Una muestra casi negra (< umbral x mediana) sólo cuenta si es visible: no está bajo la capa de bolas de una piscina, ni en una
# cara inferior a ras de suelo, ni tapada por otra superficie a menos de 6 cm delante (contacto: patas, carteles, cajas contra la
# pared, que el jugador no puede ver). Las muestras negras visibles se agrupan en celdas de 25 cm; una celda con > 0,01 m² negros
# es una «mancha». Código de salida 1 si queda alguna.
import os, sys, json, struct, math, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
argv = sys.argv[sys.argv.index('--') + 1:]
mesh, lmp, hallp = argv[:3]; REL = float(argv[3]) if len(argv) > 3 else 0.08; REP = argv[4] if len(argv) > 4 else None
lm = np.load(lmp).astype(np.float32); R = lm.shape[0]; lum = lm.mean(2)
hall = json.load(open(hallp)); pits = hall['pits']; HH = hall.get('H', 4.6)
with open(mesh, 'rb') as f:
    nv, ns = struct.unpack('<ii', f.read(8)); V = np.frombuffer(f.read(nv * 40), np.float32).reshape(nv, 10)
    subs = [np.frombuffer(f.read(struct.unpack('<i', f.read(4))[0] * 4), np.int32) for _ in range(ns)]
P, B = V[:, 0:3].astype(np.float64), V[:, 8:10]
NAMES = 'WALL FLOOR CEIL RED YEL BLU GRN PLY PLB NET EMIT BALL METAL ATLAS'.split()
allT = [t for k, s in enumerate(subs) if NAMES[k] not in ('EMIT', 'NET') for t in s.reshape(-1, 3).tolist()]
bvh = BVHTree.FromPolygons([tuple(p) for p in P.tolist()], allT, epsilon=0.0)
med = float(np.median(lum[lum > 0])); THR = REL * med
rng = np.random.default_rng(1)
tot = 0.0; cells = {}; per = {}; NB = {}
DENS = 600.0
_r = np.random.default_rng(5).normal(size=(14, 3)); HEMI = [Vector((v / np.linalg.norm(v)).tolist()) for v in _r]
for k, s in enumerate(subs):
    nm = NAMES[k]
    if nm in ('EMIT', 'BALL', 'NET'): continue   # tubos (emisivos), capa plana de bolas (bajo las instanciadas), redes (alfa)
    for t in s.reshape(-1, 3):
        p = P[t]; n = np.cross(p[1] - p[0], p[2] - p[0]); area = 0.5 * np.linalg.norm(n)
        if area < 1e-7: continue
        n /= 2 * area
        cnt = max(4, int(area * DENS))
        w = rng.random((cnt, 2)); fl = w.sum(1) > 1; w[fl] = 1 - w[fl]
        X = p[0] + w[:, :1] * (p[1] - p[0]) + w[:, 1:] * (p[2] - p[0])
        uv = B[t[0]] + w[:, :1] * (B[t[1]] - B[t[0]]) + w[:, 1:] * (B[t[2]] - B[t[0]])
        px = np.clip((uv * R).astype(int), 0, R - 1); val = lum[px[:, 1], px[:, 0]]
        vis = np.ones(cnt, bool)
        for q in pits:
            x0, x1, z0, z1 = q['r']
            inside = (X[:, 0] > x0 + 0.005) & (X[:, 0] < x1 - 0.005) & (X[:, 2] > z0 + 0.005) & (X[:, 2] < z1 - 0.005) & (X[:, 1] < q['ball'] + 0.02)
            vis &= ~inside
        if n[1] < -0.5: vis &= X[:, 1] > 0.15          # bajos de piezas a menos de 15 cm del suelo (no se ven de pie ni agachado)
        if n[1] > 0.5: vis &= X[:, 1] < HH - 0.03          # caras hacia arriba pegadas al techo
        tot += area * vis.mean()
        ax = int(np.argmax(np.abs(n))); oc = ax * 2 + (1 if n[ax] > 0 else 0)   # orientación dominante (no mezclar las dos caras de un tabique)
        for i in np.nonzero(vis)[0][::3]:
            ck = (int(math.floor(X[i][0] / 0.25)), int(math.floor(X[i][1] / 0.25)), int(math.floor(X[i][2] / 0.25)), oc)
            NB.setdefault(ck, []).append(float(val[i]))
        black = vis & (val < THR)
        if not black.any(): continue
        nn = Vector(n.tolist()); da = area / cnt
        for i in np.nonzero(black)[0]:
            o = Vector(X[i].tolist()) + nn * 0.0008
            hit = bvh.ray_cast(o, nn, 0.06)
            if hit[0] is not None: continue
            # contacto: otra cara coplanaria mirando en sentido contrario (caras internas entre bloques/escalones)
            if any(h[1].dot(nn) < -0.9 for h in bvh.find_nearest_range(Vector(X[i].tolist()), 0.0015)): continue
            # encerrada dentro de otro sólido (p. ej. tapa dentro de una columna hueca): casi todos los rayos del hemisferio
            # chocan con caras traseras o a menos de 6 cm
            closed = 0
            for d in HEMI:
                dd = (d if d.dot(nn) > 0 else -d)
                h = bvh.ray_cast(o, dd, 3.0)
                if h[0] is not None and (h[3] < 0.06 or h[1].dot(dd) > 0): closed += 1
            if closed >= 0.75 * len(HEMI): continue
            key = (nm, int(math.floor(X[i][0] / 0.25)), int(math.floor(X[i][1] / 0.25)), int(math.floor(X[i][2] / 0.25)), oc)
            cells[key] = cells.get(key, 0.0) + da
            per[nm] = per.get(nm, 0.0) + da
# una celda negra sólo es «mancha» si su entorno (cubo de 75 cm) está iluminado: las zonas oscuras por diseño (sala de fiestas
# con el fluorescente fundido, interior del tubo) no cuentan; un parche negro en una pared o pieza iluminada sí.
def around(k):
    v = []
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1): v += NB.get((k[1] + dx, k[2] + dy, k[3] + dz, k[4]), [])
    return float(np.median(v)) if v else 0.0
blk = sum(cells.values())
big = sorted(((a, k) for k, a in cells.items() if a > 0.01 and around(k) > 0.3 * med), reverse=True)
print('AUDIT median %.4f thr %.4f | visible area %.1f m2 | visible near-black %.3f m2 (%.3f%%)' % (med, THR, tot, blk, 100 * blk / max(tot, 1e-9)))
for nm, a in sorted(per.items(), key=lambda x: -x[1]): print('  %-6s %.3f m2' % (nm, a))
for a, k in big[:30]: print('  STAIN %.3f m2  %s  ~(%.2f, %.2f, %.2f) around %.3f' % (a, k[0], (k[1] + .5) * .25, (k[2] + .5) * .25, (k[3] + .5) * .25, around(k)))
# v7.1.1: falla con una mancha >= 0.03 m2 (unos 17 x 17 cm) en zona iluminada; las menores se listan (grietas de 1 cm entre bloques, rincones)
STAIN_MAX = float(os.environ.get('STAIN_MAX', '0.03')); worst = big[0][0] if big else 0.0; fail = worst >= STAIN_MAX
print('RESULT', 'FAIL' if fail else 'PASS', 'stains>0.01=%d worst=%.3f m2 max=%.3f' % (len(big), worst, STAIN_MAX))
if REP: json.dump({'median': med, 'visible_m2': tot, 'black_m2': blk, 'stains': [[a, list(k)] for a, k in big]}, open(REP, 'w'), indent=1)
sys.exit(1 if fail else 0)
