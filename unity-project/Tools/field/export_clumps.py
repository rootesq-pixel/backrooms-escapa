import bpy, struct, os, json
from mathutils import Vector
OUT='/workspace/BackroomsEscape/Assets/Resources/Field/'; os.makedirs(OUT,exist_ok=True)
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
pick={'grass_medium_01':['large_a','large_b','large_c','mid_a','mid_b','tall_a','tall_b','tall_c'],
      'grass_medium_02':['c','d','e'],'grass_bermuda_01':['dead_a','dead_b','seedling_a','seedling_b'],'dandelion_01':['a','b']}
info={}
for a,keys in pick.items():
    before=set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=f'/workspace/grass/src/{a}/{a}_2k.gltf')
    objs=[o for o in bpy.data.objects if o not in before and o.type=='MESH']
    for k in keys:
        o=[x for x in objs if x.name.startswith(a+'_'+k)][0]
        tris0=sum(len(p.vertices)-2 for p in o.data.polygons)
        target=700
        if tris0>target:
            m=o.modifiers.new('d','DECIMATE'); m.ratio=target/tris0; m.use_collapse_triangulate=True
        dg=bpy.context.evaluated_depsgraph_get(); oe=o.evaluated_get(dg); me=oe.to_mesh(); me.calc_loop_triangles()
        mw=o.matrix_world; ctr=sum((mw@v.co for v in me.vertices),Vector())/len(me.vertices)
        uv=me.uv_layers.active.data
        verts=[];idx=[];cache={}
        for t in me.loop_triangles:
            tri=[]
            for li in t.loops:
                vi=me.loops[li].vertex_index; p=mw@me.vertices[vi].co; n=(mw.to_3x3()@me.loops[li].normal).normalized(); u=uv[li].uv
                key=(vi,round(u.x,5),round(u.y,5))
                if key not in cache:
                    cache[key]=len(verts)
                    # Blender (x, y, z-up) -> Unity (x, z-up -> y, y -> z); centre on xz, keep ground at 0
                    verts.append((p.x-ctr.x,p.z,p.y-ctr.y,n.x,n.z,n.y,u.x,u.y))
                tri.append(cache[key])
            idx+= [tri[0],tri[2],tri[1]]
        name=f'{a}_{k}'
        with open(OUT+name+'.bytes','wb') as f:
            f.write(struct.pack('<ii',len(verts),len(idx)))
            for v in verts: f.write(struct.pack('<8f',*v))
            f.write(struct.pack(f'<{len(idx)}i',*idx))
        hgt=max(v[1] for v in verts); info[name]={'tris':len(idx)//3,'tris0':tris0,'h':round(hgt,3)}
        oe.to_mesh_clear()
json.dump(info,open('/workspace/grass/clumps.json','w'),indent=1); print('EXPORTDONE')
