# v7.4.0 (PC pass): builds the cabin (build_elev.py), adds a shared lightmap UV (UV2) to the PBR groups,
# bakes ambient occlusion into one atlas with Cycles (GPU when available) and exports the FBX with both UV sets.
#   blender -b -P bake_elev.py -- <build_elev.py> <outdir> <samples> <res>
import bpy, sys, os, math, time
argv = sys.argv[sys.argv.index('--')+1:]
SRC, OUT, SAMPLES, RES = argv[0], argv[1], int(argv[2]), int(argv[3])
os.makedirs(OUT, exist_ok=True)
src = open(SRC, encoding='utf-8').read()
i = src.index('bpy.ops.export_scene.fbx(')
j = src.index('\n', src.index("object_types={'MESH'})", i))
exec(compile(src[:i] + src[j:], SRC, 'exec'))
EXCL = {'Door_L','Door_R','Land_Face','Car_Grime','Car_LightDiffuser','Car_Display','Land_BtnLit','Car_Buttons','Car_Plate','Car_Grille','Car_Rubber'}
mesh_obs = [o for o in bpy.data.objects if o.type == 'MESH']
recv = [o for o in mesh_obs if o.name not in EXCL and not o.name.startswith('Car_BtnLit')]
print('GROUPS', sorted(o.name for o in mesh_obs))
print('AO_RECEIVERS', sorted(o.name for o in recv))
# --- UV2 compartida
for o in recv:
    me = o.data
    if 'UV2' not in me.uv_layers: me.uv_layers.new(name='UV2')
    me.uv_layers[0].active_render = True
    me.uv_layers.active = me.uv_layers['UV2']
bpy.ops.object.select_all(action='DESELECT')
for o in recv: o.select_set(True)
bpy.context.view_layer.objects.active = recv[0]
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
bpy.ops.uv.select_all(action='SELECT')
bpy.ops.uv.pack_islands(rotate=True, margin=4.0/RES * 1.5)
bpy.ops.object.mode_set(mode='OBJECT')
# --- oclusores del rellano (no se exportan): suelo del nivel delante de la puerta, alas del muro y dintel alto
def plane(n, verts):
    me = bpy.data.meshes.new(n); me.from_pydata([U2B(v) for v in verts], [], [(0,1,2,3)]); me.update()
    ob = bpy.data.objects.new(n, me); bpy.context.scene.collection.objects.link(ob); return ob
occ = [plane('OCC_floor', [(-4,LF,1.40),(4,LF,1.40),(4,LF,5),(-4,LF,5)]),
       plane('OCC_wallL', [(-4,LF,1.44),(-XO,LF,1.44),(-XO,3.2,1.44),(-4,3.2,1.44)]),
       plane('OCC_wallR', [(XO,LF,1.44),(4,LF,1.44),(4,3.2,1.44),(XO,3.2,1.44)]),
       plane('OCC_head', [(-XO,2.6,1.44),(XO,2.6,1.44),(XO,3.2,1.44),(-XO,3.2,1.44)])]
stats = {}
if SAMPLES > 0:
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'
    dev = 'CPU'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        for t in ('OPTIX', 'CUDA'):
            try:
                prefs.compute_device_type = t; prefs.get_devices()
                gpus = [d for d in prefs.devices if d.type == t]
                if gpus:
                    for d in prefs.devices: d.use = (d.type == t)
                    dev = t + ':' + ','.join(d.name for d in gpus); break
            except Exception as e: print('no', t, e)
    except Exception as e: print('prefs', e)
    sc.cycles.device = 'GPU' if dev != 'CPU' else 'CPU'
    sc.cycles.samples = SAMPLES
    if sc.world is None: sc.world = bpy.data.worlds.new('W')
    sc.world.light_settings.distance = float(argv[4]) if len(argv) > 4 else 0.8
    img = bpy.data.images.new('ElevAO', RES, RES, float_buffer=True)
    for o in recv:
        for ms in o.material_slots:
            m = ms.material; m.use_nodes = True
            nd = m.node_tree.nodes.new('ShaderNodeTexImage'); nd.image = img
            for x in m.node_tree.nodes: x.select = False
            nd.select = True; m.node_tree.nodes.active = nd
    bpy.ops.object.select_all(action='DESELECT')
    for o in recv: o.select_set(True)
    bpy.context.view_layer.objects.active = recv[0]
    t0 = time.time()
    bpy.ops.object.bake(type='AO', margin=8, margin_type='EXTEND', use_clear=True)
    stats['bake_s'] = round(time.time() - t0, 1); stats['device'] = dev
    img.filepath_raw = os.path.join(OUT, 'ElevAO_raw.exr'); img.file_format = 'OPEN_EXR'; img.save()
    import numpy as _np
    px = _np.array(img.pixels[:], dtype=_np.float32).reshape(RES, RES, 4)[::-1, :, 0]
    _np.save(os.path.join(OUT, 'ElevAO_raw.npy'), px)
    stats['ao_mean'] = round(float(px.mean()), 3)
    for o in recv:
        for ms in o.material_slots:
            nt = ms.material.node_tree
            for x in [x for x in nt.nodes if x.type == 'TEX_IMAGE' and x.image == img]: nt.nodes.remove(x)
for o in occ: bpy.data.objects.remove(o, do_unlink=True)
for o in recv: o.data.uv_layers.active = o.data.uv_layers[0]
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Elevator740.fbx'), use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         mesh_smooth_type='FACE', add_leaf_bones=False, bake_space_transform=False, object_types={'MESH'})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'elev_bake.blend'))
print('BAKE_DONE', stats)
