"""
Materiales procedurales por nodos (Blender) de las superficies de BackroomsEscape.
Cada funcion recibe un G (texlib) con el tamano en metros de la baldosa y devuelve
dict(albedo=Col, height=X, smooth=X, ao=X, metal=X).  Los valores de color estan en espacio "sRGB" (se escriben tal cual).
"""
from texlib import G, Col, X

# ---------------------------------------------------------------------------------------------
# utilidades
# ---------------------------------------------------------------------------------------------
def rgbf(g, c): return Col(*[g.val(x) for x in c])

def tintmix(g, col, tint, t):
    return g.cmix(col, Col(*[col.c[i] * tint[i] for i in range(3)]), t)

def tofloat(g, c):
    return c.c[0] * 0.299 + c.c[1] * 0.587 + c.c[2] * 0.114

def finish(g, col, height, smooth, ao, metal=None):
    return dict(albedo=col, height=height, smooth=smooth, ao=ao, metal=metal if metal is not None else g.val(0.0))

def cn(g, fu, fv=None, k=2.0, **kw):
    """ruido con contraste"""
    return g.contrast(g.noise(fu, fv, **kw), k)

def edge_dist(g, f):
    """distancia al borde de una celda 0..1 (0 en el borde, 0.5 en el centro)"""
    return g.minimum(f, 1.0 - f)


# ---------------------------------------------------------------------------------------------
# Papel pintado amarillo hùmedo (nivel 0)  -  baldosa 2.4 m x 3.0 m
# ---------------------------------------------------------------------------------------------
def wallpaper_yellow(g):
    u, v = g.u, g.v
    H = 3.0
    my = v * H
    f = g.fract(u * 16.0)
    band = 1.0 - g.smooth(0.49, 0.51, f)
    line = 1.0 - g.smooth(0.0, 0.035, g.abs(f - 0.5))
    edge = 1.0 - g.smooth(0.0, 0.02, edge_dist(g, f))
    col = g.cmix(rgbf(g, (0.64, 0.56, 0.29)), rgbf(g, (0.71, 0.62, 0.34)), band)
    col = g.cmul(col, 1.0 - 0.14 * line + 0.05 * edge)

    # fibra del papel (vertical) + grano fino
    fib = g.noise(420, 36, detail=1.0)
    pg = g.noise(700, 700, detail=0.0)
    pg2 = g.noise(230, 230, detail=0.0, seed=3)
    col = g.cmul(col, 1.0 + 0.18 * (pg - 0.5) + 0.12 * (pg2 - 0.5) + 0.10 * (fib - 0.5))

    # junta entre rollos (cada 0.6 m): fina linea de sombra y un leve despegue
    su = g.fract(u * 4.0)
    seam = 1.0 - g.smooth(0.0, 0.006, edge_dist(g, su))
    col = g.cmul(col, 1.0 - 0.10 * seam)

    # moteado y manchas de humedad con chorreones
    blot = cn(g, 3, 3, 1.8, detail=4.0, seed=1)
    col = g.cmul(col, 0.86 + 0.28 * blot)
    s = cn(g, 4, 4, 2.0, detail=5.0, rough=0.55, seed=2)
    st = cn(g, 22, 2.5, 2.2, detail=4.0, rough=0.5, seed=4)
    mix = s * 0.55 + st * 0.45
    leak = g.smooth(0.50, 0.74, mix) * (0.3 + 0.7 * g.smooth(0.35, 1.0, v))
    col = tintmix(g, col, (0.80, 0.66, 0.38), leak * 0.85)
    tide = g.band(mix, 0.50, 0.53, 0.012) * (0.3 + 0.7 * g.smooth(0.35, 1.0, v))   # borde de la mancha (marea)
    col = tintmix(g, col, (0.72, 0.52, 0.30), tide * 0.7)

    # humedad ascendente en la base, moho
    damp = (1.0 - g.smooth(0.0, 0.38, v)) * (0.45 + 0.55 * blot)
    col = tintmix(g, col, (0.62, 0.55, 0.45), damp * 0.75)
    mo = cn(g, 20, 20, 2.0, detail=3.0, rough=0.6, seed=5)
    mold = g.smooth(0.62, 0.74, mo) * (1.0 - g.smooth(0.0, 0.5, v))
    col = g.cmix(col, rgbf(g, (0.10, 0.12, 0.07)), mold * 0.8)

    # papel despegado (se ve el yeso)
    p = cn(g, 3, 3, 2.0, detail=5.0, rough=0.55, seed=6)
    peel = g.smooth(0.74, 0.76, p)
    peel_edge = g.clamp(g.smooth(0.71, 0.74, p) - g.smooth(0.74, 0.78, p))
    plaster = g.cmul(rgbf(g, (0.76, 0.69, 0.52)), (0.92 + 0.16 * pg) * (0.8 + 0.4 * s))
    col = g.cmix(col, plaster, peel)
    col = g.cadd(col, Col(peel_edge * 0.13, peel_edge * 0.12, peel_edge * 0.08))

    # rodapie (14 cm) con ranura de sombra
    boardMask = 1.0 - g.smooth(0.135, 0.142, my)
    groove = g.smooth(0.14, 0.146, my) * (1.0 - g.smooth(0.152, 0.162, my))
    scuff = 0.8 + 0.35 * g.noise(40, 12, detail=2.0, seed=7)
    board = g.cmul(rgbf(g, (0.56, 0.50, 0.38)), scuff * (0.7 + 0.3 * (1.0 - damp)))
    col = g.cmix(col, board, boardMask)
    col = g.cmul(col, 1.0 - 0.5 * groove)

    # oclusion horneada en esquinas y cornisa
    ao = g.lerp(0.70, 1.0, g.smooth(0.0, 0.3, v)) * g.lerp(0.80, 1.0, 1.0 - g.smooth(0.82, 1.0, v))
    col = g.cmul(col, 0.82 + 0.18 * ao)

    height = (0.5 + 0.10 * band - 0.06 * line + 0.05 * (pg - 0.5) + 0.04 * (fib - 0.5) - 0.45 * peel + 0.10 * peel_edge
              + 0.38 * boardMask - 0.3 * groove - 0.05 * seam)
    smooth = 0.10 + 0.22 * leak + 0.18 * damp + 0.3 * boardMask - 0.06 * peel
    return finish(g, col, height, g.clamp(smooth), g.clamp(ao - 0.25 * peel_edge))


# ---------------------------------------------------------------------------------------------
# Papel de hotel (damasco) con zocalo de madera  -  baldosa 2.0 m x 2.8 m
# ---------------------------------------------------------------------------------------------
def wallpaper_hotel(g):
    u, v = g.u, g.v
    H = 2.8
    my = v * H
    grain = g.noise(600, 600, detail=0.0, seed=1) - 0.5
    wg = g.noise(110, 3, detail=3.0, seed=2)
    wg2 = g.noise(26, 2.2, detail=4.0, seed=3)
    s = cn(g, 4, 4, 2.0, detail=5.0, seed=4)
    st = cn(g, 20, 3, 2.2, detail=4.0, seed=5)

    wainEnd, railEnd = 0.36, 0.405
    # --- zocalo de madera con paneles hundidos (2 por baldosa)
    pu = g.fract(u * 2.0)
    pvv = my / (H * wainEnd)
    dEdge = g.minimum(g.minimum(pu - 0.08, 0.92 - pu), g.minimum(pvv - 0.10, 0.90 - pvv))
    inside = g.smooth(-0.01, 0.03, dEdge)
    bevel = g.smooth(0.0, 0.04, dEdge) * (1.0 - g.smooth(0.04, 0.09, dEdge))
    w = 0.65 + 0.35 * wg + 0.25 * wg2
    wood = Col(w * 0.22, w * 0.115, w * 0.065)
    shade = g.lerp(1.0, 0.82, inside) + 0.18 * bevel
    wood = g.cmul(wood, shade)
    bd = 1.0 - g.smooth(0.03, 0.036, v)
    wood = g.cmix(wood, rgbf(g, (0.14, 0.075, 0.045)), bd * 0.5)
    wood_h = 0.5 - 0.35 * inside + 0.1 * bevel + 0.04 * grain + 0.03 * (wg - 0.5)
    wood_s = 0.42 + 0.1 * wg

    # --- moldura superior
    t = (v - wainEnd) / (railEnd - wainEnd)
    hi = 1.0 - g.abs(t - 0.5) * 2.0
    wr = 0.8 + 0.4 * wg
    rail = Col(0.34 * wr, 0.19 * wr, 0.10 * wr)
    rail_h = 0.7 + 0.25 * hi
    rail_s = g.val(0.5)

    # --- papel damasco burdeos con ovalos dorados
    cellx = g.floor(u * 8.0)
    parity = g.fract(cellx * 0.5) * 2.0
    f = g.fract(u * 8.0)
    yy = my / 0.45 + parity * 0.5
    oy = g.fract(yy) - 0.5
    ox = f - 0.5
    e = (ox * ox) / (0.26 * 0.26) + (oy * oy) / (0.30 * 0.30)
    inner = 1.0 - g.smooth(0.85, 1.0, e)
    ring = g.clamp(g.smooth(0.8, 0.95, e) - g.smooth(1.0, 1.2, e))
    pin = (1.0 - g.smooth(0.0, 0.03, g.abs(f - 0.5))) * g.smooth(1.25, 1.35, e)
    paper = rgbf(g, (0.27, 0.055, 0.075))
    paper = g.cmix(paper, rgbf(g, (0.37, 0.075, 0.10)), inner * 0.8)
    paper = g.cmix(paper, rgbf(g, (0.46, 0.34, 0.15)), ring * 0.7)
    paper = g.cmix(paper, rgbf(g, (0.40, 0.30, 0.13)), pin * 0.6)
    # relieve de terciopelo: motivos con "pelo" (ruido fino solo dentro del ovalo)
    vel = g.noise(380, 380, detail=0.0, seed=8) - 0.5
    paper = g.cmul(paper, 1.0 + 0.12 * grain + 0.20 * vel * inner)
    paper_h = 0.5 + 0.15 * inner + 0.2 * ring + 0.1 * pin + 0.04 * grain
    paper_s = 0.10 + 0.1 * ring + 0.1 * inner * g.smooth(0.4, 0.6, vel + 0.5)
    corn = g.smooth(0.93, 0.95, v)
    paper = g.cmix(paper, rgbf(g, (0.30, 0.24, 0.16)), corn)
    paper_h = paper_h + 0.3 * corn

    mw = 1.0 - g.smooth(wainEnd - 0.002, wainEnd + 0.002, v)
    mr = g.smooth(wainEnd - 0.002, wainEnd + 0.002, v) * (1.0 - g.smooth(railEnd - 0.002, railEnd + 0.002, v))
    col = g.cmix(g.cmix(paper, rail, mr), wood, mw)
    height = g.lerp(g.lerp(paper_h, rail_h, mr), wood_h, mw)
    smooth = g.lerp(g.lerp(paper_s, rail_s, mr), wood_s, mw)

    leak = g.smooth(0.52, 0.76, s * 0.5 + st * 0.5) * (0.3 + 0.7 * v)
    col = Col(col.c[0] * (1.0 - 0.45 * leak), col.c[1] * (1.0 - 0.5 * leak), col.c[2] * (1.0 - 0.5 * leak))
    # polvo / arañazos suaves en la madera
    scr = g.noise(4, 90, detail=1.0, seed=9)
    col = g.cmul(col, 1.0 - 0.10 * g.smooth(0.62, 0.8, scr) * mw)
    ao = g.lerp(0.65, 1.0, g.smooth(0.0, 0.25, v)) * g.lerp(0.75, 1.0, 1.0 - g.smooth(0.85, 1.0, v))
    col = g.cmul(col, 0.8 + 0.2 * ao)
    return finish(g, col, height, g.clamp(smooth + 0.15 * leak), ao)


# ---------------------------------------------------------------------------------------------
# Moqueta (marron desgastada / roja con rombos dorados)  -  baldosa 2 m x 2 m
# ---------------------------------------------------------------------------------------------
def carpet(g, red):
    u, v = g.u, g.v
    sd = 40 if red else 50
    # hebras: celdas Voronoi pequenas (penachos) + grano fino
    d1, c1 = g.voronoi(150, 150, seed=sd)
    tuft_v = 1.0 - g.smooth(0.0, 0.62, d1)
    fibre = g.noise(320, 320, detail=0.0, seed=sd + 1)
    t1 = g.noise(780, 780, detail=0.0, seed=sd + 2)
    tuft = 0.55 * tuft_v + 0.35 * fibre + 0.30 * t1 + 0.18 * c1
    patches = cn(g, 6, 6, 1.8, detail=4.0, seed=sd + 3)
    stains = cn(g, 5, 5, 2.2, detail=5.0, seed=sd + 4)
    wear = cn(g, 3, 3, 1.8, detail=3.0, seed=sd + 5)

    base = rgbf(g, (0.40, 0.055, 0.065)) if red else rgbf(g, (0.44, 0.37, 0.26))
    # variacion de color por penacho (hebras de tonos distintos)
    shade = 0.72 + 0.55 * tuft
    pk = 0.88 + 0.24 * patches
    col = g.cmul(base, shade * pk)
    height = tuft * 0.8
    if red:
        du = g.abs(g.fract(u * 4.0) - 0.5)
        dv = g.abs(g.fract(v * 4.0) - 0.5)
        d = du + dv
        lineMask = 1.0 - g.smooth(0.008, 0.02, g.abs(d - 0.34))
        dot = 1.0 - g.smooth(0.04, 0.07, d)
        inner = 1.0 - g.smooth(0.02, 0.035, g.abs(d - 0.2))
        gold = g.clamp(lineMask + dot * 0.9 + inner * 0.6)
        gk = 0.65 + 0.5 * tuft
        col = g.cmix(col, Col(0.55 * gk, 0.40 * gk, 0.14 * gk), gold * 0.85)
        height = height + gold * 0.12
    # zonas de paso y manchas oscuras con borde (derrames)
    worn = g.smooth(0.55, 0.8, wear) * 0.12
    col = g.cadd(col, Col(worn * 0.5, worn * 0.45, worn * 0.3))
    sm = g.smooth(0.62, 0.74, stains)
    col = tintmix(g, col, (0.72, 0.68, 0.66), sm * 0.6)
    rim = g.band(stains, 0.60, 0.64, 0.012)
    col = tintmix(g, col, (0.80, 0.75, 0.65), rim * 0.45)
    ao = 0.45 + 0.55 * g.clamp(tuft)
    return finish(g, col, height, 0.04 + 0.05 * sm + g.val(0.0), g.clamp(ao))


# ---------------------------------------------------------------------------------------------
# Techo de placas acusticas  -  baldosa 2.4 m (2x2 placas de 1.2 m)
# ---------------------------------------------------------------------------------------------
def ceiling(g, color, joints):
    u, v = g.u, g.v
    bl = cn(g, 8, 8, 1.6, detail=3.0, seed=61)
    st = cn(g, 3, 3, 2.0, detail=5.0, seed=62)
    grain = g.noise(650, 650, detail=0.0, seed=63) - 0.5
    k = 0.93 + 0.2 * (bl - 0.5) + 0.10 * grain
    col = Col(*[g.val(color[i]) * k for i in range(3)])
    height = 0.5 + 0.06 * grain

    # perforaciones (celdas pequenas aleatorias) y fisuras (ruido "ridged")
    dv_, cell = g.voronoi(170, 170, seed=64)
    holes = (cell < 0.09) * (1.0 - g.smooth(0.10, 0.20, dv_))
    holes2 = (cell > 0.93) * (1.0 - g.smooth(0.05, 0.12, dv_))
    ridge = g.noise(14, 14, detail=3.0, rough=0.55, kind='RIDGED_MULTIFRACTAL', seed=65)
    fis = g.smooth(0.80, 0.9, ridge) * g.smooth(0.45, 0.6, bl)
    hole = g.clamp(holes + holes2 + fis * 0.8)
    col = g.cmul(col, 1.0 - 0.55 * hole)
    height = height - 0.5 * hole
    ao = 1.0 - 0.35 * hole
    if joints:
        du = edge_dist(g, g.fract(u * 2.0))
        dv2 = edge_dist(g, g.fract(v * 2.0))
        d = g.minimum(du, dv2)
        groove = 1.0 - g.smooth(0.0005, 0.007, d)
        lip = g.smooth(0.006, 0.010, d) * (1.0 - g.smooth(0.010, 0.016, d))
        col = g.cmul(col, 1.0 - 0.65 * groove)
        col = g.cadd(col, 0.03 * lip)
        height = height - 0.8 * groove + 0.1 * lip
        ao = ao - 0.5 * groove
    # manchas amarillo-marron por humedad con aro
    sm = g.smooth(0.62, 0.78, st)
    col = tintmix(g, col, (0.82, 0.68, 0.42), sm)
    ring = g.band(st, 0.58, 0.62, 0.012)
    col = tintmix(g, col, (0.88, 0.82, 0.75), ring * 0.8)
    return finish(g, col, height, 0.05 + 0.1 * sm, g.clamp(ao))


# ---------------------------------------------------------------------------------------------
# Hormigon  (mode 0 pared 3.0 x 3.6 m, 1 suelo de parking 2.7 x 5.4 m, 2 techo 3 x 3 m)
# ---------------------------------------------------------------------------------------------
def concrete(g, mode):
    u, v = g.u, g.v
    Wm, Hm = g.W, g.H
    sd = 70 + mode * 10
    mott = cn(g, 6, 6, 1.8, detail=6.0, rough=0.55, seed=sd + 1)
    mott2 = cn(g, 17, 17, 1.6, detail=4.0, seed=sd + 7)
    fine = g.noise(560, 560, detail=1.0, seed=sd + 2) - 0.5
    k = 0.52 + 0.20 * (mott - 0.5) + 0.12 * (mott2 - 0.5) + 0.12 * fine
    col = Col(k, k, k * 0.97)
    height = 0.5 + 0.35 * fine + 0.10 * (mott2 - 0.5)
    ao = g.val(1.0)
    smooth = 0.12 + 0.10 * (mott - 0.5)

    # poros / aire ocluido
    dp, cp = g.voronoi(120, 120, seed=sd + 3)
    pore = (cp < 0.10) * (1.0 - g.smooth(0.05, 0.22, dp))
    col = g.cmul(col, 1.0 - 0.35 * pore)
    height = height - 0.35 * pore
    ao = ao - 0.25 * pore
    # grietas finas (bordes de celdas Voronoi grandes, distorsionadas con ruido)
    de, _c = g.voronoi(3, 3.0 * Hm / Wm, feature='DISTANCE_TO_EDGE', seed=sd + 4)
    wob = g.noise(40, 40, detail=3.0, seed=sd + 5) - 0.5
    crackm = (1.0 - g.smooth(0.0, 0.012 + 0.02 * g.abs(wob), de)) * g.smooth(0.60, 0.74, mott2) * 0.9
    col = g.cmul(col, 1.0 - 0.55 * crackm)
    height = height - 0.5 * crackm
    ao = ao - 0.3 * crackm

    if mode == 0:
        my = v * Hm
        # juntas de encofrado: paneles de 1.5 m x 1.2 m + agujeros de tirantes
        pu = g.fract(u * 2.0)
        pv = g.fract(my / 1.2)
        jd = g.minimum(edge_dist(g, pu) * 1.5, edge_dist(g, pv) * 1.2)       # metros al borde del panel
        joint = 1.0 - g.smooth(0.0, 0.008, jd)
        lipj = g.band(jd, 0.008, 0.016, 0.006)
        col = g.cmul(col, 1.0 - 0.45 * joint + 0.04 * lipj)
        height = height - 0.6 * joint + 0.1 * lipj
        ao = ao - 0.35 * joint
        dx = g.abs(g.abs(pu - 0.5) - 0.30) * 1.5
        dy = g.abs(g.abs(pv - 0.5) - 0.25) * 1.2
        hd = g.sqrt(dx * dx + dy * dy)
        tie = 1.0 - g.smooth(0.012, 0.022, hd)
        tie_ring = g.band(hd, 0.022, 0.035, 0.01)
        col = g.cmul(col, 1.0 - 0.7 * tie - 0.12 * tie_ring)
        height = height - 0.7 * tie + 0.1 * tie_ring
        ao = ao - 0.5 * tie
        # chorreones oscuros y mugre en la base (y oxido en los tirantes)
        streak = cn(g, 14, 2.0, 2.2, detail=4.0, rough=0.5, seed=sd + 8)
        drip = g.smooth(0.55, 0.85, streak) * (0.25 + 0.75 * g.smooth(0.3, 1.0, v))
        col = tintmix(g, col, (0.68, 0.66, 0.62), drip * 0.7)
        rustd = g.smooth(0.0, 0.18, 1.0 - g.smooth(0.022, 0.20, hd)) * g.smooth(0.55, 0.8, streak)
        col = tintmix(g, col, (1.1, 0.72, 0.50), rustd * 0.7)
        base = (1.0 - g.smooth(0.0, 0.30, v)) * (0.5 + 0.5 * mott)
        col = tintmix(g, col, (0.62, 0.62, 0.60), base * 0.75)
        smooth = smooth + 0.05 * drip
        ao = ao * g.lerp(0.78, 1.0, g.smooth(0.0, 0.25, v))
    elif mode == 1:
        # suelo de parking: juntas de dilatacion, manchas de aceite, lineas de plaza, huellas de neumaticos
        du = edge_dist(g, g.fract(u))
        dv = edge_dist(g, g.fract(v))
        jd = g.minimum(du * Wm, dv * Hm)
        joint = 1.0 - g.smooth(0.0, 0.012, jd)
        col = g.cmul(col, 1.0 - 0.5 * joint)
        height = height - 0.6 * joint
        ao = ao - 0.4 * joint
        oil = cn(g, 4, 6, 2.4, detail=4.0, rough=0.6, seed=sd + 9)
        oilm = g.smooth(0.66, 0.80, oil)
        oil_rim = g.band(oil, 0.64, 0.68, 0.015)
        col = tintmix(g, col, (0.42, 0.42, 0.44), oilm * 0.85)
        col = tintmix(g, col, (0.75, 0.72, 0.70), oil_rim * 0.35)
        smooth = smooth + 0.5 * oilm
        # huellas de neumaticos (bandas largas en V)
        tyre = g.noise(3, 40, detail=3.0, seed=sd + 10)
        tr = (g.band(g.fract(u + 0.5 * (tyre - 0.5) * 0.2), 0.20, 0.30, 0.04) + g.band(g.fract(u + 0.5 * (tyre - 0.5) * 0.2), 0.68, 0.78, 0.04)) * g.smooth(0.42, 0.68, tyre)
        col = tintmix(g, col, (0.62, 0.62, 0.64), tr * 0.55)
        # lineas de plaza (amarillo desgastado)
        lw = 0.10 / Wm
        line_u = (1.0 - g.smooth(lw * 0.9, lw, g.minimum(g.fract(u), 1.0 - g.fract(u))))
        line_v = g.band(v, 0.0, 0.10 / Hm, 0.004)
        paint = g.clamp(line_u + line_v)
        pwear = g.smooth(0.44, 0.72, g.noise(30, 30, detail=5.0, rough=0.6, seed=sd + 11)) * 0.85
        paint = paint * (1.0 - pwear)
        col = g.cmix(col, Col(0.80 * (0.9 + 0.2 * fine), 0.72 * (0.9 + 0.2 * fine), 0.30), paint)
        smooth = smooth - 0.05 * paint
        ao = ao * (0.88 + 0.12 * g.smooth(0.0, 0.6, mott))
        height = height + 0.04 * paint
    else:
        du = edge_dist(g, g.fract(u * 2.0))
        dv = edge_dist(g, g.fract(v * 2.0))
        jd = g.minimum(du * Wm * 0.5, dv * Hm * 0.5)
        joint = 1.0 - g.smooth(0.0, 0.015, jd)
        col = g.cmul(col, 1.0 - 0.5 * joint)
        height = height - 0.6 * joint
        stn = cn(g, 4, 4, 2.2, detail=4.0, seed=sd + 12)
        col = tintmix(g, col, (0.75, 0.70, 0.60), g.smooth(0.62, 0.8, stn) * 0.7)
        ao = ao * (0.85 + 0.15 * g.smooth(0.0, 0.5, mott))
    return finish(g, col, height, g.clamp(smooth), g.clamp(ao))


# ---------------------------------------------------------------------------------------------
# Azulejos de piscina (25 cm: 8x8 por baldosa de 2 m): juntas, biselado, algas, humedad
# ---------------------------------------------------------------------------------------------
def pool_tile(g, wall):
    u, v = g.u, g.v
    gu, gv = g.fract(u * 8.0), g.fract(v * 8.0)
    ix, iy = g.floor(u * 8.0), g.floor(v * 8.0)
    # distancia al borde del azulejo en celdas (0 = junta)
    de = g.minimum(edge_dist(g, gu), edge_dist(g, gv))
    grout = 1.0 - g.smooth(0.020, 0.040, de)
    bevel = g.smooth(0.040, 0.10, de) * (1.0 - g.smooth(0.10, 0.20, de))
    rnd = g.cellrand(ix, iy, seed=3)
    rnd2 = g.cellrand(ix, iy, seed=4)
    if wall:
        base = rgbf(g, (0.70, 0.84, 0.85))
        stripe = (iy > 2.5) * (iy < 3.5)
        base = g.cmix(base, rgbf(g, (0.20, 0.50, 0.68)), stripe)
    else:
        base = rgbf(g, (0.62, 0.74, 0.76))
    col = g.cmul(base, 0.90 + 0.14 * rnd)
    # degradado esmaltado dentro de cada azulejo
    glaze = 0.92 + 0.10 * (gu * 0.5 + gv * 0.5) * (1.0 - bevel * 0.3)
    col = g.cmul(col, glaze)
    # azulejos perdidos / desportillados (se ve cemento)
    missing = (rnd2 < 0.008) * (1.0 - grout)
    chip = (rnd2 > 0.972) * g.smooth(0.55, 0.9, g.noise(60, 60, detail=3.0, seed=5)) * (1.0 - grout)
    cem = g.cmul(rgbf(g, (0.27, 0.28, 0.26)), 0.8 + 0.4 * g.noise(300, 300, detail=1.0, seed=6))
    col = g.cmix(col, cem, g.clamp(missing + chip * 0.8))
    # algas y suciedad (mas en las juntas y abajo)
    alg = cn(g, 5, 5, 2.2, detail=5.0, seed=7)
    algm = g.smooth(0.60, 0.78, alg) * (0.5 + 0.8 * grout) * (1.0 - 0.4 * g.smooth(0.3, 1.0, v) if wall else g.val(1.0))
    col = g.cmix(col, rgbf(g, (0.28, 0.42, 0.22)), g.clamp(algm) * 0.7)
    # grietas del esmalte (aristas de Voronoi) y manchas de cal
    dcr, _c = g.voronoi(7, 7, feature='DISTANCE_TO_EDGE', seed=8)
    crack = (1.0 - g.smooth(0.0, 0.006, dcr)) * g.smooth(0.55, 0.75, g.noise(10, 10, detail=3.0, seed=9)) * (1.0 - grout)
    col = g.cmul(col, 1.0 - 0.45 * crack)
    lime = g.smooth(0.66, 0.82, cn(g, 9, 3, 2.0, detail=4.0, seed=10)) * 0.5
    col = g.cmix(col, rgbf(g, (0.86, 0.88, 0.84)), lime * (1.0 - grout))
    # junta: mortero sucio
    gk = 0.50 + 0.15 * g.noise(260, 260, detail=1.0, seed=11) - 0.2 * algm
    col = g.cmix(col, Col(gk * 0.95, gk * 0.95, gk * 0.90), grout)
    wet = g.smooth(0.35, 0.7, g.noise(4, 4, detail=4.0, seed=12))
    smooth = g.lerp(0.50 + 0.35 * wet, 0.12, grout) - 0.2 * algm - 0.3 * g.clamp(missing + chip)
    height = 0.5 + 0.35 * bevel * (1 - grout) - 0.5 * grout - 0.4 * missing - 0.2 * crack + 0.02 * g.noise(400, 400, detail=0.0, seed=13)
    ao = 1.0 - 0.45 * grout - 0.3 * missing
    return finish(g, col, height, g.clamp(smooth), g.clamp(ao))


# ---------------------------------------------------------------------------------------------
# Chapa pintada y oxidada con remaches (paneles de 1.2 m, baldosa de 2.4 m)
# ---------------------------------------------------------------------------------------------
def metal_wall(g):
    u, v = g.u, g.v
    ax = g.fract(u * 2.0) * 1.2          # metros dentro del panel (horizontal)
    ay = g.fract(v * 2.0) * 1.2
    # costuras de los paneles y remaches (cada 15 cm a 5.5 cm del borde)
    edgeX = g.minimum(ax, 1.2 - ax)
    edgeY = g.minimum(ay, 1.2 - ay)
    ed = g.minimum(edgeX, edgeY)
    seam = 1.0 - g.smooth(0.0, 0.006, ed)
    lip = g.band(ed, 0.006, 0.020, 0.008)
    # remaches a lo largo de los 4 bordes
    ry = g.abs(g.fract(ay / 0.15) - 0.5) * 0.15
    rx = g.abs(g.fract(ax / 0.15) - 0.5) * 0.15
    dV = g.sqrt((edgeX - 0.055) * (edgeX - 0.055) + ry * ry)
    dHh = g.sqrt((edgeY - 0.055) * (edgeY - 0.055) + rx * rx)
    rivet_d = g.minimum(dV, dHh)
    # solo si estan cerca del borde correspondiente
    rivet = (1.0 - g.smooth(0.011, 0.016, rivet_d))
    rivet_hi = g.band(rivet_d, 0.004, 0.012, 0.004) * 0.0 + (1.0 - g.smooth(0.0, 0.009, rivet_d)) * 0.5

    # pintura: verde grisaceo con variacion
    mott = cn(g, 4, 4, 1.8, detail=5.0, seed=101)
    fine = g.noise(500, 500, detail=1.0, seed=102) - 0.5
    k = 0.85 + 0.25 * (mott - 0.5) + 0.12 * fine
    paint = Col(0.30 * k, 0.36 * k, 0.33 * k)
    # desconchado -> oxido; metal desnudo en el borde
    ch = g.noise(5, 5, detail=6.0, rough=0.6, seed=103)
    ch = ch + 0.12 * (g.noise(18, 18, detail=3.0, seed=104) - 0.5) + 0.25 * (1.0 - g.smooth(0.0, 0.25, ed)) * (g.noise(30, 30, detail=2.0, seed=105))
    rust = g.smooth(0.63, 0.67, ch)
    bare = g.clamp(g.smooth(0.615, 0.63, ch) - g.smooth(0.63, 0.645, ch))
    rust_tex = g.noise(26, 26, detail=5.0, rough=0.62, seed=106)
    rg = 0.7 + 0.7 * rust_tex
    rust_col = g.ramp(rust_tex, [(0.25, (0.20, 0.08, 0.04)), (0.55, (0.46, 0.22, 0.09)), (0.80, (0.62, 0.32, 0.10))])
    col = g.cmix(paint, rust_col, rust)
    col = g.cmix(col, rgbf(g, (0.55, 0.55, 0.56)), bare)
    # chorreones de oxido desde remaches y costuras
    dr = g.noise(26, 2.2, detail=4.0, seed=107)
    drips = g.smooth(0.60, 0.82, dr) * (g.smooth(0.0, 0.5, g.noise(5, 5, detail=3.0, seed=108)))
    col = tintmix(g, col, (1.0, 0.62, 0.36), drips * 0.55 * (1.0 - rust))
    # aranazos horizontales
    scr = g.smooth(0.70, 0.88, g.noise(4, 90, detail=2.0, seed=109))
    col = g.cmix(col, rgbf(g, (0.56, 0.56, 0.57)), scr * 0.35 * (1.0 - rust))
    # mugre en costuras
    col = g.cmul(col, 1.0 - 0.45 * seam - 0.25 * lip)
    col = g.cmul(col, 1.0 - 0.5 * rivet + 0.15 * rivet_hi)
    smooth = 0.30 + 0.10 * (mott - 0.5) - 0.18 * rust + 0.28 * bare - 0.1 * seam
    metal = bare * 0.85 + rust * 0.08 + rivet * 0.25
    height = 0.5 + 0.03 * fine - 0.35 * rust - 0.45 * seam + 0.15 * lip + 0.6 * rivet + 0.1 * bare
    ao = 1.0 - 0.45 * seam - 0.3 * rivet - 0.15 * rust
    return finish(g, col, height, g.clamp(smooth), g.clamp(ao), g.clamp(metal))


# ---------------------------------------------------------------------------------------------
# Chapa lagrimada de suelo industrial  -  baldosa 1 m
# ---------------------------------------------------------------------------------------------
def diamond_plate(g):
    u, v = g.u, g.v
    N = 14.0
    cu, cv = g.fract(u * N), g.fract(v * N)
    ix, iy = g.floor(u * N), g.floor(v * N)
    parity = g.fract((ix + iy) * 0.5) * 2.0
    x, y = cu - 0.5, cv - 0.5
    a = g.lerp(x + y, x - y, parity)        # perpendicular a la barra
    b = g.lerp(x - y, x + y, parity)        # a lo largo de la barra
    e = (a * a) / (0.075 * 0.075) + (b * b) / (0.36 * 0.36)
    bar = 1.0 - g.smooth(0.85, 1.0, e)
    barcore = 1.0 - g.smooth(0.0, 0.7, e)
    fine = g.noise(400, 400, detail=1.0, seed=201) - 0.5
    mott = cn(g, 4, 4, 1.8, detail=5.0, seed=202)
    k = 0.40 + 0.10 * (mott - 0.5) + 0.06 * fine
    col = Col(k, k * 1.02, k * 0.98)
    col = g.cadd(col, 0.12 * bar)
    # oxido y suciedad acumulada entre las barras
    rn = g.noise(5, 5, detail=6.0, rough=0.62, seed=203)
    rust = g.smooth(0.58, 0.66, rn + 0.15 * (g.noise(25, 25, detail=3.0, seed=204) - 0.5))
    rust_tex = g.noise(30, 30, detail=5.0, seed=205)
    rust_col = g.ramp(rust_tex, [(0.25, (0.20, 0.08, 0.04)), (0.55, (0.42, 0.20, 0.08)), (0.8, (0.58, 0.30, 0.10))])
    col = g.cmix(col, rust_col, rust * 0.8)
    grime = g.smooth(0.45, 0.8, g.noise(9, 9, detail=4.0, seed=206)) * (1.0 - bar * 0.7)
    col = tintmix(g, col, (0.55, 0.52, 0.48), grime * 0.7)
    # desgaste: barras pulidas por el paso
    polish = g.smooth(0.5, 0.75, g.noise(3, 3, detail=3.0, seed=207)) * bar * (1.0 - rust)
    col = g.cadd(col, 0.08 * polish)
    smooth = 0.34 + 0.20 * bar + 0.25 * polish - 0.25 * rust - 0.15 * grime
    metal = (0.55 + 0.25 * polish) * (1.0 - rust) * (1.0 - 0.5 * grime)
    height = 0.35 + 0.65 * bar * (0.5 + 0.5 * barcore) + 0.03 * fine
    ao = 0.50 + 0.50 * g.clamp(0.2 + bar) - 0.15 * grime
    return finish(g, col, height, g.clamp(smooth), g.clamp(ao), g.clamp(metal))


# ---------------------------------------------------------------------------------------------
# Registro: nombre -> (funcion, ancho m, alto m, resolucion albedo, resolucion datos, altura de pared horneada)
# ---------------------------------------------------------------------------------------------
STYLES = {
    'WallpaperYellow': (lambda g: wallpaper_yellow(g), 2.4, 3.0, 2048, 1024, 3.0),
    'WallpaperHotel':  (lambda g: wallpaper_hotel(g),  2.0, 2.8, 1024, 1024, 2.8),
    'CarpetBrown':     (lambda g: carpet(g, False),    2.0, 2.0, 2048, 1024, 0.0),
    'CarpetRed':       (lambda g: carpet(g, True),     2.0, 2.0, 1024, 1024, 0.0),
    'CeilingBeige':    (lambda g: ceiling(g, (0.84, 0.81, 0.68), True), 2.4, 2.4, 1024, 1024, 0.0),
    'CeilingPool':     (lambda g: ceiling(g, (0.72, 0.80, 0.80), True), 2.4, 2.4, 1024, 1024, 0.0),
    'CeilingDark':     (lambda g: ceiling(g, (0.25, 0.23, 0.21), False), 2.4, 2.4, 1024, 1024, 0.0),
    'ConcreteWall':    (lambda g: concrete(g, 0), 3.0, 3.6, 1024, 1024, 3.6),
    'ConcreteFloor':   (lambda g: concrete(g, 1), 2.7, 5.4, 1024, 1024, 0.0),
    'ConcreteCeiling': (lambda g: concrete(g, 2), 3.0, 3.0, 1024, 1024, 0.0),
    'PoolWall':        (lambda g: pool_tile(g, True),  2.0, 2.0, 1024, 1024, 0.0),
    'PoolFloor':       (lambda g: pool_tile(g, False), 2.0, 2.0, 1024, 1024, 0.0),
    'MetalWall':       (lambda g: metal_wall(g),   2.4, 2.4, 1024, 1024, 0.0),
    'MetalFloor':      (lambda g: diamond_plate(g), 1.0, 1.0, 1024, 1024, 0.0),
}
