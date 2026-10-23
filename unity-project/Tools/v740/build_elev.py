# v7.4.0: cabina de ascensor realista (geometría propia), construida en coordenadas locales de Unity (x derecha, y arriba, z hacia la puerta)
# y exportada a FBX. Grupos de material por nombre de objeto (ElevatorCabin los asigna en Unity).
import bpy, bmesh, math
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
G = {}   # nombre -> (bmesh, uv scale)
SC = {'Car_Wood2':0.9,'Car_Grime':1.0,'Car_Steel':1.0,'Car_SteelDark':1.0,'Car_Wood':0.9,'Car_Floor':0.9,'Car_Ceiling':1.0,'Car_LightFrame':1.0,'Car_LightDiffuser':1.0,
      'Car_Rail':1.0,'Car_Panel':0.5,'Car_Buttons':1.0,'Car_Display':1.0,'Car_Plate':1.0,'Car_Facade':1.0,'Car_Rubber':1.0,'Car_Grille':0.25,
      'Door_L':1.0,'Door_R':1.0,'Car_BtnLit0':1,'Car_BtnLit1':1,'Car_BtnLit2':1,'Car_BtnLit3':1,'Car_BtnLit4':1}
def bm_of(n):
    if n not in G: G[n]=bmesh.new()
    return G[n]
def U2B(p): return Vector((-p[0], -p[2], p[1]))
def uvset(bm, faces, n):
    s = SC.get(n,1.0); uvl = bm.loops.layers.uv.verify()
    for f in faces:
        nn = f.normal  # en coords de Unity (construimos en Unity y convertimos al final)
        ax = max(range(3), key=lambda i: abs(nn[i]))
        for l in f.loops:
            p = l.vert.co
            if ax == 0: u, v = p[2]*(1 if nn[0]<0 else -1), p[1]
            elif ax == 2: u, v = p[0]*(1 if nn[2]>0 else -1), p[1]
            else: u, v = p[0], p[2]
            l[uvl].uv = (u/s, v/s)
def box(n, a, b, step=0.32):
    # caja con caras subdivididas (rejilla de ~0.32 m) para que la luz por vértice y la niebla no marquen diagonales
    bm = bm_of(n)
    x0,y0,z0 = a; x1,y1,z1 = b
    lo = Vector((min(x0,x1),min(y0,y1),min(z0,z1))); hi = Vector((max(x0,x1),max(y0,y1),max(z0,z1)))
    fs = []
    for ax in range(3):
        o1, o2 = [i for i in range(3) if i != ax]
        for sgn in (-1, 1):
            c = hi[ax] if sgn > 0 else lo[ax]
            n1 = max(1, int(math.ceil((hi[o1]-lo[o1])/step))); n2 = max(1, int(math.ceil((hi[o2]-lo[o2])/step)))
            grid = {}
            for i in range(n1+1):
                for j in range(n2+1):
                    p = Vector((0,0,0)); p[ax] = c; p[o1] = lo[o1] + (hi[o1]-lo[o1])*i/n1; p[o2] = lo[o2] + (hi[o2]-lo[o2])*j/n2
                    grid[i,j] = bm.verts.new(p)
            for i in range(n1):
                for j in range(n2):
                    q = [grid[i,j], grid[i+1,j], grid[i+1,j+1], grid[i,j+1]]
                    f = bm.faces.new(q); f.normal_update()
                    want = Vector((0,0,0)); want[ax] = sgn
                    if f.normal.dot(want) < 0: f.normal_flip()
                    fs.append(f)
    uvset(bm, fs, n)
def cyl(n, p0, p1, r, seg=16, caps=True):
    bm = bm_of(n); p0=Vector(p0); p1=Vector(p1); d=(p1-p0); L=d.length; d.normalize()
    t = Vector((0,1,0)) if abs(d.y)<0.9 else Vector((1,0,0)); u = d.cross(t).normalized(); w = d.cross(u).normalized()
    r0=[];r1=[]
    for i in range(seg):
        a=2*math.pi*i/seg; o=(u*math.cos(a)+w*math.sin(a))*r
        r0.append(bm.verts.new(p0+o)); r1.append(bm.verts.new(p1+o))
    fs=[]
    for i in range(seg):
        j=(i+1)%seg; f=bm.faces.new((r0[i],r0[j],r1[j],r1[i])); fs.append(f)
    if caps:
        fs.append(bm.faces.new(list(reversed(r0)))); fs.append(bm.faces.new(r1))
    bm.normal_update()
    c=(p0+p1)/2
    for f in fs[:seg]:
        if f.normal.dot(f.calc_center_median()-c - d*d.dot(f.calc_center_median()-c))<0: f.normal_flip()
    if caps:
        if fs[seg].normal.dot(-d)<0: fs[seg].normal_flip()
        if fs[seg+1].normal.dot(d)<0: fs[seg+1].normal_flip()
    uvl = bm.loops.layers.uv.verify()
    for k,f in enumerate(fs):
        for l in f.loops:
            q=l.vert.co-p0; l[uvl].uv=(q.dot(d)/1.0, (math.atan2(q.dot(w),q.dot(u))/(2*math.pi))*0.2)
    for f in fs[:seg]: f.smooth=True

# ---------------- medidas (m, local Unity) ----------------
FL = 0.14            # suelo acabado
CE = 2.26            # techo (cara inferior)
XI = 1.04            # cara interior paredes laterales
ZB = -1.04           # cara interior del fondo
ZF = 1.26            # cara interior del frente (paneles de retorno)
XO = 1.30            # exterior lateral
ZBO = -1.30
OW = 0.75            # media anchura del hueco de puerta
DT = 2.05            # altura del hueco
# suelo
box('Car_SteelDark', (-XO,0.0,ZBO), (XO,0.12,1.44))                  # bastidor
box('Car_Floor', (-XI,0.12,ZB), (XI,FL,ZF))                            # goma moteada
box('Car_SteelDark', (-OW-0.02,0.12,ZF), (OW+0.02,FL+0.004,1.42))      # umbral de aluminio
for k in range(-14,15):                                                # ranuras del umbral
    x = k*0.05
    box('Car_Rubber', (x-0.006,FL+0.004,ZF+0.03), (x+0.006,FL+0.006,1.40))
# paredes: estructura exterior pintada (vista desde fuera) + revestimiento interior
box('Car_Facade', (-XO,0.12,ZBO), (-XI-0.03,CE+0.12,ZF+0.02))
box('Car_Facade', (XI+0.03,0.12,ZBO), (XO,CE+0.12,ZF+0.02))
box('Car_Facade', (-XO,0.12,ZBO), (XO,CE+0.12,ZB-0.03))
# rodapié de acero rozado
for side in (-1,1):
    box('Car_SteelDark', (side*XI if side<0 else XI-0.012, FL, ZB), ((side*XI+0.012) if side<0 else XI, FL+0.2, ZF))
box('Car_SteelDark', (-XI,FL,ZB), (XI,FL+0.2,ZB+0.012))
# paneles: banda baja acero (0.34..0.62), paneles de madera (0.62..2.02) y friso alto acero, con juntas oscuras
def wall_panels_side(side):
    x = -XI if side<0 else XI; t = 0.03*side
    zs = [ZB+0.02, -0.28, 0.48, ZF-0.02]
    xin = x - (0.0 if side<0 else 0.03); xout = x + (0.03 if side<0 else 0.0)
    xa, xb = (x+0.005, x+0.03) if side<0 else (x-0.03, x-0.005)
    jx0, jx1 = (x-0.03, x+0.005) if side<0 else (x-0.005, x+0.03)
    box('Car_SteelDark', (jx0, FL+0.2, ZB), (jx1, CE, ZF))  # fondo de junta (detrás de los paneles)
    for i in range(3):
        z0, z1 = zs[i]+0.006, zs[i+1]-0.006
        box('Car_Steel', (xa,FL+0.2,z0), (xb,0.62,z1))
        box('Car_Wood', (xa,0.63,z0), (xb,2.02,z1))
        box('Car_Steel', (xa,2.03,z0), (xb,CE,z1))
for s in (-1,1): wall_panels_side(s)
xs = [-XI+0.02, -0.36, 0.36, XI-0.02]
box('Car_SteelDark', (-XI,FL+0.2,ZB), (XI,CE,ZB+0.02))
for i in range(3):
    x0, x1 = xs[i]+0.006, xs[i+1]-0.006
    box('Car_Steel', (x0,FL+0.2,ZB), (x1,0.62,ZB+0.03))
    box('Car_Wood', (x0,0.63,ZB), (x1,2.02,ZB+0.03))
    box('Car_Steel', (x0,2.03,ZB), (x1,CE,ZB+0.03))
# frente interior: paneles de retorno de acero y dintel (transom) con el indicador
box('Car_Steel', (-XI,FL,ZF), (-OW,CE,ZF+0.04))
box('Car_Steel', (OW,FL,ZF), (XI,CE,ZF+0.04))
box('Car_Steel', (-OW,DT,ZF), (OW,CE,ZF+0.04))
box('Car_SteelDark', (-OW-0.015,FL,ZF-0.006), (-OW+0.004,DT+0.015,ZF+0.045))   # jamba
box('Car_SteelDark', (OW-0.004,FL,ZF-0.006), (OW+0.015,DT+0.015,ZF+0.045))
box('Car_SteelDark', (-OW-0.015,DT-0.004,ZF-0.006), (OW+0.015,DT+0.015,ZF+0.045))
box('Car_Display', (-0.30,2.085,ZF-0.035), (0.30,2.225,ZF))                     # carcasa del indicador
# botonera (en el retorno derecho)
px0, px1 = OW+0.07, XI-0.04
box('Car_Panel', (px0,0.92,ZF-0.008), (px1,1.78,ZF))
cx = (px0+px1)/2
for i in range(5):                                                               # columna iluminada (0 abajo)
    y = 1.02 + i*0.115
    cyl('Car_Buttons', (cx-0.045,y,ZF-0.008), (cx-0.045,y,ZF-0.016), 0.021, 20)
    cyl('Car_BtnLit%d'%i, (cx-0.045,y,ZF-0.016), (cx-0.045,y,ZF-0.019), 0.015, 20)
for i in range(6):                                                               # columna de botones apagados
    y = 1.02 + i*0.115*0.8
    cyl('Car_Buttons', (cx+0.045,y,ZF-0.008), (cx+0.045,y,ZF-0.018), 0.018, 20)
cyl('Car_Buttons', (cx+0.045,1.62,ZF-0.008), (cx+0.045,1.62,ZF-0.02), 0.022, 20)  # alarma
box('Car_Grille', (px0+0.03,1.68,ZF-0.012), (px1-0.03,1.75,ZF-0.008))            # altavoz/interfono
box('Car_Plate', (-OW-0.235,1.42,ZF-0.004), (-OW-0.035,1.66,ZF))                 # placa de inspección (retorno izquierdo)
# pasamanos tubular con soportes (fondo y laterales), a 0.9 m del suelo
RY = FL + 0.90; RO = 0.055
cyl('Car_Rail', (-XI+0.12,RY,ZB+RO), (XI-0.12,RY,ZB+RO), 0.019, 18)
for x in (-0.75,0.0,0.75): cyl('Car_Rail', (x,RY,ZB+0.03), (x,RY,ZB+RO), 0.011, 12)
for s in (-1,1):
    cyl('Car_Rail', (s*(XI-RO),RY,ZB+0.12), (s*(XI-RO),RY,0.95), 0.019, 18)
    for z in (-0.6,0.3): cyl('Car_Rail', (s*(XI-0.03),RY,z), (s*(XI-RO),RY,z), 0.011, 12)
    # codos a 90º simplificados (esferas como cilindros cortos)
    cyl('Car_Rail', (s*(XI-RO),RY,ZB+RO), (s*(XI-RO),RY,ZB+0.12), 0.019, 18)
    cyl('Car_Rail', (s*(XI-0.12),RY,ZB+RO), (s*(XI-RO),RY,ZB+RO), 0.019, 18)
# techo: falso techo de acero con luminaria central (marco + difusor) y rejilla de ventilación
box('Car_Ceiling', (-XI,CE,ZB), (XI,CE+0.04,ZF))
box('Car_LightFrame', (-0.66,CE-0.03,-0.52), (0.66,CE,0.72))
box('Car_LightDiffuser', (-0.62,CE-0.034,-0.48), (0.62,CE-0.03,0.68))
for k in range(1,4):
    box('Car_LightFrame', (-0.62,CE-0.045,-0.48+k*0.29), (0.62,CE-0.03,-0.48+k*0.29+0.012))   # celosía
box('Car_Grille', (-0.25,CE-0.006,ZB+0.08), (0.25,CE,ZB+0.22))
box('Car_Facade', (-XO,CE+0.04,ZBO), (XO,CE+0.12,1.44))                       # techo exterior
# puertas de cabina (dos hojas de 0.76 m que se deslizan hacia los lados), con junta de goma en el borde central
DZ0, DZ1 = ZF+0.05, ZF+0.09
box('Door_L', (-OW-0.01,FL+0.004,DZ0), (0.004,DT,DZ1))
box('Door_R', (-0.004,FL+0.004,DZ0+0.006), (OW+0.01,DT,DZ1+0.006))
# fachada exterior (rellano): marco de acero, dintel con indicador exterior
box('Car_Facade', (-XO,FL,1.40), (-OW-0.06,2.6,1.44))
box('Car_Facade', (OW+0.06,FL,1.40), (XO,2.6,1.44))
box('Car_Facade', (-XO,DT+0.06,1.40), (XO,2.6,1.44))
box('Car_SteelDark', (-OW-0.06,FL,1.40), (-OW,DT+0.06,1.47))
box('Car_SteelDark', (OW,FL,1.40), (OW+0.06,DT+0.06,1.47))
box('Car_SteelDark', (-OW-0.06,DT,1.40), (OW+0.06,DT+0.06,1.47))
box('Car_Display', (-0.16,DT+0.14,1.44), (0.16,DT+0.24,1.465))
box('Car_Panel', (OW+0.14,1.0,1.44), (OW+0.24,1.28,1.452))                       # pulsador de llamada exterior
cyl('Car_Buttons', (OW+0.19,1.14,1.452), (OW+0.19,1.14,1.462), 0.02, 20)

# suciedad acumulada junto a las paredes (franja de 0.22 m sobre el suelo)
def quad(n, ps, uvs):
    bm = bm_of(n); vs=[bm.verts.new(p) for p in ps]; f=bm.faces.new(vs); f.normal_update()
    if f.normal.y < 0: f.normal_flip(); uvs=list(reversed(uvs)); vs=list(reversed(vs))
    uvl = bm.loops.layers.uv.verify()
    for l in f.loops: l[uvl].uv = uvs[vs.index(l.vert)] if l.vert in vs else (0,0)
GY = FL + 0.0015; GW = 0.22
quad('Car_Grime', [(-XI,GY,ZB),(XI,GY,ZB),(XI,GY,ZB+GW),(-XI,GY,ZB+GW)], [(0,0),(2*XI,0),(2*XI,1),(0,1)])
for s_ in (-1,1):
    x=s_*XI; xi=s_*(XI-GW)
    quad('Car_Grime', [(x,GY,ZB),(x,GY,ZF),(xi,GY,ZF),(xi,GY,ZB)], [(0,0),(ZF-ZB,0),(ZF-ZB,1),(0,1)])
quad('Car_Grime', [(-XI,GY,ZF),(XI,GY,ZF),(XI,GY,ZF-0.16),(-XI,GY,ZF-0.16)], [(0,0),(2*XI,0),(2*XI,1),(0,1)])

# ---------------- objetos, conversión a ejes de Blender y exportación ----------------
for n,bm in G.items():
    if n in ('Car_Plate','Car_Display'):          # UV 0..1 sobre la cara grande
        uvl = bm.loops.layers.uv.verify(); us=[l[uvl].uv.copy() for f in bm.faces for l in f.loops]
        u0=min(u.x for u in us); u1=max(u.x for u in us); v0=min(u.y for u in us); v1=max(u.y for u in us)
        for f in bm.faces:
            for l in f.loops: l[uvl].uv=((l[uvl].uv.x-u0)/max(1e-4,u1-u0),(l[uvl].uv.y-v0)/max(1e-4,v1-v0))
    me = bpy.data.meshes.new(n)
    for v in bm.verts: v.co = U2B(v.co)
    for f in bm.faces: f.normal_flip()
    bm.normal_update()
    bm.to_mesh(me); bm.free()
    me.validate()
    ob = bpy.data.objects.new(n, me); bpy.context.scene.collection.objects.link(ob)
    mat = bpy.data.materials.get(n) or bpy.data.materials.new(n); me.materials.append(mat)
    for p in me.polygons: pass
# hijos de las puertas: junta de goma con la hoja izquierda
bpy.data.objects['Car_Rubber'].name = 'Car_Rubber'
bpy.ops.export_scene.fbx(filepath="/workspace/v740/elev/Elevator740.fbx", use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         mesh_smooth_type='FACE', add_leaf_bones=False, bake_space_transform=False, object_types={'MESH'})
tot = sum(len(o.data.polygons) for o in bpy.data.objects if o.type=='MESH')
print("ELEV objects", len(G), "faces", tot)
