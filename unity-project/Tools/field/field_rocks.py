# v7.2.0 Campo infinito: rocas escaneadas CC0 de Poly Haven (namaqualand_boulders_01, namaqualand_boulder_05, rock_07)
# -> FBX con _LOD0/_LOD1/_LOD2 (diezmadas), base en z=0, centradas. Uso: blender -b -P field_rocks.py -- <carpeta glTF> <salida>
import bpy, sys, os
from mathutils import Vector
SRC, OUT = sys.argv[sys.argv.index("--") + 1:][:2]
ROCKS = [("namaqualand_boulders_01", "FieldRock0", (5000, 1400, 350)), ("namaqualand_boulder_05", "FieldRock1", (5000, 1400, 350)), ("rock_07", "FieldRock2", (3000, 900, 250))]
for src, name, tris in ROCKS:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, src, src + "_1k.gltf"))
    ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in ms: o.select_set(True)
    bpy.context.view_layer.objects.active = ms[0]
    if len(ms) > 1: bpy.ops.object.join()
    ob = bpy.context.active_object
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM'); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    vs = [v.co for v in ob.data.vertices]
    mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))); mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    c = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    for v in ob.data.vertices: v.co -= c
    for m in ob.data.materials: m.name = "Rock"
    n0 = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    ob.name = name; out = []
    for i, t in enumerate(tris):
        cp = ob.copy(); cp.data = ob.data.copy(); bpy.context.collection.objects.link(cp); cp.name = name + "_LOD%d" % i
        if t < n0:
            d = cp.modifiers.new("dec", 'DECIMATE'); d.ratio = t / n0
            bpy.context.view_layer.objects.active = cp; bpy.ops.object.select_all(action='DESELECT'); cp.select_set(True); bpy.ops.object.modifier_apply(modifier="dec")
        out.append(cp)
    bpy.data.objects.remove(ob)
    bpy.ops.object.select_all(action='DESELECT')
    for o in out: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True, bake_space_transform=True, path_mode='STRIP')
    print("ROCK", name, "src tris", n0, "size", tuple(round(x, 3) for x in (mx - mn)), "lods", [sum(len(p.vertices) - 2 for p in o.data.polygons) for o in out])
