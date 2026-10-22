# v6.8 fixed pool room: geometry (Blender: x=Unity x, y=Unity z, z=Unity y; deck top z=0), lightmap UV1, Cycles bake, probes, export
import bpy, bmesh, math, sys, json, struct, os
import numpy as np
argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
SPP=int(argv[0]) if argv else 1024; RES=int(argv[1]) if len(argv)>1 else 2048
OUT=os.environ.get('POOL_OUT','/workspace/pool-render/fixed/').replace('\\','/'); OUT=OUT if OUT.endswith('/') else OUT+'/'; os.makedirs(OUT,exist_ok=True)
def cycles_device(sc):
    """CYCLES_DEVICE=CPU (por defecto) | GPU | OPTIX | CUDA. GPU prueba OptiX y luego CUDA."""
    dev = os.environ.get('CYCLES_DEVICE', 'CPU').upper()
    if dev == 'CPU':
        sc.cycles.device = 'CPU'; return 'CPU'
    p = bpy.context.preferences.addons['cycles'].preferences
    for t in (('OPTIX', 'CUDA') if dev in ('GPU', 'OPTIX') else (dev,)):
        try:
            p.compute_device_type = t; p.get_devices()
        except TypeError:
            continue
        if any(d.type == t for d in p.devices): break
    for d in p.devices: d.use = (d.type == p.compute_device_type)
    sc.cycles.device = 'GPU'; print('cycles device', p.compute_device_type, [d.name for d in p.devices if d.use], flush=True)
    return p.compute_device_type

for o in list(bpy.data.objects): bpy.data.objects.remove(o)
CS=4.0; T=0.2; WH=3.2; DECK=0.28
HW=2*CS-T/2-0.004; HD=1.5*CS-T/2-0.004; H=WH-DECK-0.004
BX=2*CS; BY=1.5*CS                              # block half extents (wall centre lines)
sx=(4*CS-T)/14; sz=(3*CS-T)/10
PX=4*sx; PY=2*sz; PD=1.4
DW=2.0; DH=2.5-DECK                             # door width / top (local)
# v7.2.0: como pool-render/build.py: 3 ventanas en la pared +x (E) y sin puertas a la vista de las cámaras de Blender;
# puertas solo en W (centro) y S (celda 0, detrás de Cam_Wide/Cam_Low)
DOORS={'S':-6.0,'N':None,'W':0.0,'E':None}
WIN_S=[]; WIN_N=[]; WIN_E=[-3.0*sz,0.0,3.0*sz]; WIN_W=1.2*sz; WIN_Z0=0.90; WIN_Z1=2.62
SKY=[(-2.5*sx,0.0),(2.5*sx,0.0)]; SKW=2*sx; SKD=2*sz
quads=[]  # (verts[4], mat)  mats: 0 wall 1 deck 2 pool 3 ceiling 4 emissive
def q(v,m): quads.append(([tuple(map(float,p)) for p in v],m))
def rect_x(y,x0,x1,z0,z1,m,inward):   # wall plane at y, normal +y if inward>0
    v=[(x0,y,z0),(x1,y,z0),(x1,y,z1),(x0,y,z1)]
    q(v[::-1] if inward>0 else v,m)
def rect_y(x,y0,y1,z0,z1,m,inward):   # plane at x, normal +x if inward>0
    v=[(x,y1,z0),(x,y0,z0),(x,y0,z1),(x,y1,z1)]
    q(v[::-1] if inward>0 else v,m)
def rect_z(z,x0,x1,y0,y1,m,up):
    v=[(x0,y0,z),(x1,y0,z),(x1,y1,z),(x0,y1,z)]
    q(v if up>0 else v[::-1],m)
# deck top (full block incl. under walls/sills) minus pool hole
rect_z(0,-BX,BX,-BY,-PY,1,1); rect_z(0,-BX,BX,PY,BY,1,1); rect_z(0,-BX,-PX,-PY,PY,1,1); rect_z(0,PX,BX,-PY,PY,1,1)
# pool shell
rect_z(-PD,-PX,PX,-PY,PY,2,1)
rect_x(-PY,-PX,PX,-PD,0,2,1); rect_x(PY,-PX,PX,-PD,0,2,-1); rect_y(-PX,-PY,PY,-PD,0,2,1); rect_y(PX,-PY,PY,-PD,0,2,-1)
# ceiling
rect_z(H-0.002,-HW,HW,-HD,HD,3,-1)
# walls with door holes; reveals, risers
def wall_x(y,inward,dx):
    if dx is None: rect_x(y,-HW,HW,0,H,0,inward); return
    rect_x(y,-HW,dx-DW/2,0,H,0,inward); rect_x(y,dx+DW/2,HW,0,H,0,inward); rect_x(y,dx-DW/2,dx+DW/2,DH,H,0,inward)
    yo=y-inward*(T+0.001); a,b=sorted((y+inward*0.0,yo))
    rect_y(dx-DW/2+0.002,a,b,-DECK,DH,0,1); rect_y(dx+DW/2-0.002,a,b,-DECK,DH,0,-1)
    rect_z(DH-0.002,dx-DW/2,dx+DW/2,a,b,0,-1)
    yb=-BY if inward>0 else BY; rect_x(yb,dx-DW/2,dx+DW/2,-DECK,0,1,-inward)
def wall_y(x,inward,dy):
    if dy is None: rect_y(x,-HD,HD,0,H,0,inward); return
    rect_y(x,-HD,dy-DW/2,0,H,0,inward); rect_y(x,dy+DW/2,HD,0,H,0,inward); rect_y(x,dy-DW/2,dy+DW/2,DH,H,0,inward)
    xo=x-inward*(T+0.001); a,b=sorted((x,xo))
    rect_x(dy-DW/2+0.002,a,b,-DECK,DH,0,1); rect_x(dy+DW/2-0.002,a,b,-DECK,DH,0,-1)
    rect_z(DH-0.002,a,b,dy-DW/2,dy+DW/2,0,-1)
    xb=-BX if inward>0 else BX; rect_y(xb,dy-DW/2,dy+DW/2,-DECK,0,1,-inward)
wall_x(-HD,1,DOORS['S']); wall_x(HD,-1,DOORS['N']); wall_y(-HW,1,DOORS['W']); wall_y(HW,-1,DOORS['E'])
# emissive windows / skylights (geometry for Unity look; light comes from area lights)
for x in WIN_S: rect_x(-HD+0.006,x-WIN_W/2,x+WIN_W/2,WIN_Z0,WIN_Z1,4,1)
for x in WIN_N: rect_x(HD-0.006,x-WIN_W/2,x+WIN_W/2,WIN_Z0,WIN_Z1,4,-1)
for y in WIN_E: rect_y(HW-0.006,y-WIN_W/2,y+WIN_W/2,WIN_Z0,WIN_Z1,4,-1)
for (x,y) in SKY: rect_z(H-0.006,x-SKW/2,x+SKW/2,y-SKD/2,y+SKD/2,4,-1)

me=bpy.data.meshes.new('PoolRoomFixed'); bm=bmesh.new()
for v,m in quads:
    f=bm.faces.new([bm.verts.new(p) for p in v]); f.material_index=m
bmesh.ops.remove_doubles(bm,verts=bm.verts,dist=0.0005)
bm.to_mesh(me); bm.free()
ob=bpy.data.objects.new('PoolRoomFixed',me); bpy.context.scene.collection.objects.link(ob)
# UV0: box mapping in metres (tile textures repeat every TILE m)
TILE={0:1.0,1:1.0,2:1.0,3:1.0,4:1.0}
uv0=me.uv_layers.new(name='UV0'); 
for p in me.polygons:
    n=p.normal; ax=max(range(3),key=lambda i:abs(n[i]))
    for li in p.loop_indices:
        c=me.vertices[me.loops[li].vertex_index].co
        a,b={0:(c.y,c.z),1:(c.x,c.z),2:(c.x,c.y)}[ax]
        uv0.data[li].uv=(a/TILE[p.material_index],b/TILE[p.material_index])
# subdivide large faces a bit (better lightmap pack + vertex density not needed, keep flat)
uv1=me.uv_layers.new(name='UV1'); me.uv_layers.active=uv1
bpy.context.view_layer.objects.active=ob; ob.select_set(True)
for p in me.polygons: p.select = p.material_index!=4
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60),island_margin=0.0,area_weight=0.0,scale_to_bounds=False)
bpy.ops.uv.pack_islands(rotate=True,shape_method='CONCAVE',margin=12.0/RES)
bpy.ops.object.mode_set(mode='OBJECT')
for p in me.polygons:
    if p.material_index==4:
        for li in p.loop_indices: uv1.data[li].uv=(0.0005,0.0005)
# materials (bounce albedo in linear)
def mat(n,col,emit=0):
    m=bpy.data.materials.new(n); m.use_nodes=True; b=m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value=(*col,1); b.inputs['Roughness'].default_value=0.5
    if emit: b.inputs['Emission Color'].default_value=(*col,1); b.inputs['Emission Strength'].default_value=emit
    return m
for m in [mat('Wall',(0.62,0.66,0.67)),mat('Deck',(0.64,0.67,0.66)),mat('Pool',(0.43,0.60,0.66)),mat('Ceil',(0.60,0.63,0.64)),mat('Emit',(0.9,0.96,1.0),0.0)]: me.materials.append(m)
# lights: area lights at windows / skylights
def area(name,loc,rot,sxv,syv,W):
    L=bpy.data.lights.new(name,'AREA'); L.shape='RECTANGLE'; L.size=sxv; L.size_y=syv; L.energy=W; L.spread=math.radians(float(os.environ.get('SPREAD','180')) if name!='Sky' else 180); L.color=(0.88,0.95,1.0)
    o=bpy.data.objects.new(name,L); o.location=loc; o.rotation_euler=rot; bpy.context.scene.collection.objects.link(o)
WINW=float(os.environ.get('WINW','200')); SKYW=float(os.environ.get('SKYW','260'))
for x in WIN_S: area('WinS',(x,-HD+0.03,(WIN_Z0+WIN_Z1)/2),(math.radians(-90),0,0),WIN_W,WIN_Z1-WIN_Z0,WINW)
for x in WIN_N: area('WinN',(x,HD-0.03,(WIN_Z0+WIN_Z1)/2),(math.radians(90),0,0),WIN_W,WIN_Z1-WIN_Z0,WINW)
for y in WIN_E: area('WinE',(HW-0.03,y,(WIN_Z0+WIN_Z1)/2),(0,math.radians(90),0),WIN_Z1-WIN_Z0,WIN_W,WINW)
for (x,y) in SKY: area('Sky',(x,y,H-0.03),(0,0,0),SKW,SKD,SKYW)
sc=bpy.context.scene; sc.render.engine='CYCLES'; cy=sc.cycles; cycles_device(sc); cy.samples=SPP
cy.max_bounces=6; cy.diffuse_bounces=4; cy.use_denoising=False
sc.world=bpy.data.worlds.new('W'); sc.world.use_nodes=True; sc.world.node_tree.nodes['Background'].inputs[1].default_value=0.0
# bake irradiance
img=bpy.data.images.new('LM',RES,RES,float_buffer=True,alpha=False)
for m in me.materials:
    nt=m.node_tree; t=nt.nodes.new('ShaderNodeTexImage'); t.image=img; nt.nodes.active=t
b=sc.render.bake; b.use_pass_direct=True; b.use_pass_indirect=True; b.use_pass_color=False; b.margin=16; b.margin_type='EXTEND'; b.target='IMAGE_TEXTURES'
bpy.ops.object.bake(type='DIFFUSE',uv_layer='UV1')
px=np.array(img.pixels[:],dtype=np.float32).reshape(RES,RES,4)
np.save(OUT+'lm_raw.npy',px[:,:,:3])
# probes: 6 positions, cube faces 8x8 at 90 deg -> directional samples (Unity coords)
probes=[]
cam=bpy.data.cameras.new('C'); cam.angle=math.radians(90); co=bpy.data.objects.new('C',cam); sc.collection.objects.link(co); sc.camera=co
sc.render.resolution_x=sc.render.resolution_y=8; sc.render.resolution_percentage=100; cy.samples=64
sc.render.image_settings.file_format='OPEN_EXR'
from mathutils import Matrix, Vector
faces=[(Vector((1,0,0)),Vector((0,0,1))),(Vector((-1,0,0)),Vector((0,0,1))),(Vector((0,1,0)),Vector((0,0,1))),(Vector((0,-1,0)),Vector((0,0,1))),(Vector((0,0,1)),Vector((0,1,0))),(Vector((0,0,-1)),Vector((0,1,0)))]
PTS=[(x,z,h) for h in (0.5,1.6) for x in (-6.0,-3.0,0.0,3.0,6.0) for z in (-4.5,0.0,4.5)]+[(x,0.0,-0.8) for x in (-3.0,0.0,3.0)]
for (px_,py,pz) in PTS:
  if True:
    samples=[]
    for fwd,up in faces:
        co.location=(px_,py,pz); co.rotation_euler=fwd.to_track_quat('-Z','Y' if up.y else 'Z').to_euler()
        sc.render.filepath=OUT+'probe_tmp.exr'; bpy.ops.render.render(write_still=True)
        im=bpy.data.images.load(OUT+'probe_tmp.exr'); a=np.array(im.pixels[:]).reshape(8,8,4); bpy.data.images.remove(im)
        R=co.matrix_world.to_3x3()
        for j in range(8):
            for i in range(8):
                u=(i+0.5)/8*2-1; v=(j+0.5)/8*2-1      # j from bottom
                d=R@Vector((u,v,-1)); dw=4/64/ (d.length**3); d.normalize()
                c=a[j,i,:3]*dw/math.pi
                samples.append([d.x,d.z,d.y,float(c[0]),float(c[1]),float(c[2])])
    probes.append({'p':[px_,pz+DECK,py],'s':samples})
json.dump({'probes':probes},open(OUT+'probes.json','w'))
# export mesh (Unity coords: swap y/z -> flip winding); local origin = block centre at corridor floor (y=0) -> deck at DECK
me.calc_loop_triangles() if hasattr(me,'calc_loop_triangles') else None
uv0=me.uv_layers['UV0']; uv1=me.uv_layers['UV1']
verts=[];uvA=[];uvB=[];nrm=[];subs={k:[] for k in range(5)};index={}
for tri in me.loop_triangles:
    m=me.polygons[tri.polygon_index].material_index; idx=[]
    for li in tri.loops:
        vco=me.vertices[me.loops[li].vertex_index].co; n=me.polygons[tri.polygon_index].normal
        key=(tuple(round(c,5) for c in vco),tuple(round(c,4) for c in n),tuple(round(c,5) for c in uv0.data[li].uv),tuple(round(c,6) for c in uv1.data[li].uv))
        if key not in index:
            index[key]=len(verts); verts.append((vco.x,vco.z+DECK,vco.y)); nrm.append((n.x,n.z,n.y)); uvA.append(tuple(uv0.data[li].uv)); uvB.append(tuple(uv1.data[li].uv))
        idx.append(index[key])
    subs[m]+= [idx[0],idx[2],idx[1]]
with open(OUT+'PoolRoomFixed.bytes','wb') as f:
    f.write(struct.pack('<ii',len(verts),5))
    for i in range(len(verts)): f.write(struct.pack('<10f',*verts[i],*nrm[i],*uvA[i],*uvB[i]))
    for k in range(5): f.write(struct.pack('<i',len(subs[k]))); f.write(struct.pack('<%di'%len(subs[k]),*subs[k]))
bpy.ops.wm.save_as_mainfile(filepath=OUT+'pool_fixed.blend')
print('DONE verts',len(verts),'tris',sum(len(s) for s in subs.values())//3)
