import bpy
from mathutils import Vector
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
for a in ['grass_medium_01','grass_medium_02','grass_bermuda_01','dandelion_01']:
    before=set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=f'/workspace/grass/src/{a}/{a}_2k.gltf')
    objs=[o for o in bpy.data.objects if o not in before]
    roots=[o for o in objs if o.parent is None]
    print('ASSET',a,len(objs),'roots',len(roots))
    for r in roots[:12]:
        ms=[r]+list(r.children_recursive); ms=[m for m in ms if m.type=='MESH']
        bb=[m.matrix_world@Vector(c) for m in ms for c in m.bound_box]
        if not bb: continue
        print(' ',r.name,r.type,len(ms),[round(min(v[i] for v in bb),2) for i in range(3)],[round(max(v[i] for v in bb),2) for i in range(3)])
