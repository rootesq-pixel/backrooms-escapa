# v7.7.0 Desierto Cálido: modelos CC0 de Poly Haven -> FBX con _LOD0/_LOD1/_LOD2 (diezmadas), base en z=0, centrados,
# materiales M0..Mn y sus texturas (albedo _a, normal _n, máscara _m: R metal, G AO, A suavidad) a 1024 px.
# Variantes «rotas» de los muebles: se separan las piezas sueltas y se quitan algunas (patas, travesaños, tablas).
# Uso: blender -b -P desert_props.py -- <carpeta mod de Poly Haven> <salida> [solo_nombre]
import bpy, bmesh, sys, os, random
from mathutils import Vector
SRC, OUT = sys.argv[sys.argv.index("--") + 1:][:2]
ONLY = sys.argv[sys.argv.index("--") + 3] if len(sys.argv) > sys.argv.index("--") + 3 else None
os.makedirs(OUT, exist_ok=True)
# (fuente, nombre, triángulos por LOD, rotura) rotura: None | ("legs", n) quita n piezas bajas y delgadas | ("parts", frac) quita una fracción de piezas
ITEMS = [
    ("namaqualand_boulder_02", "DRock0", (6000, 1600, 400), None),
    ("namaqualand_boulder_03", "DRock1", (6000, 1600, 400), None),
    ("namaqualand_boulder_04", "DRock2", (6000, 1600, 400), None),
    ("namaqualand_boulder_06", "DRock3", (6000, 1600, 400), None),
    ("namaqualand_cliff_01", "DCliff0", (12000, 3000, 700), None),
    ("namaqualand_cliff_02", "DCliff1", (12000, 3000, 700), None),
    ("namaqualand_rocks_01", "DRocks", (5000, 1400, 350), None),
    ("namaqualand_stones_01", "DStones", (4000, 1000, 250), None),
    ("sand_rocks_small_01", "DPebbles", (3000, 800, 200), None),
    ("dry_branches_medium_01", "DPlant0", (5000, 1500, 400), None),
    ("wild_rooibos_bush", "DPlant1", (6000, 1800, 450), None),
    ("dry_quiver_leaf", "DPlant2", (3000, 900, 250), None),
    ("shrub_sorrel_01", "DPlant3", (5000, 1500, 400), None),
    ("painted_wooden_chair_01", "FChair0", (4000, 1200, 300), None),
    ("painted_wooden_chair_01", "FChair0_broken", (4000, 1200, 300), ("legs", 2)),
    ("painted_wooden_chair_02", "FChair1_broken", (4000, 1200, 300), ("legs", 1)),
    ("painted_wooden_table", "FTable0", (4000, 1200, 300), None),
    ("painted_wooden_table", "FTable0_broken", (4000, 1200, 300), ("legs", 1)),
    ("painted_wooden_stool", "FStool0", (2500, 800, 200), None),
    ("wooden_stool_01", "FStool1_broken", (2500, 800, 200), ("legs", 1)),
    ("old_bed_frame", "FBed_broken", (6000, 1800, 450), ("parts", 0.25)),
    ("wooden_bucket_01", "FBucket", (2500, 800, 200), None),
    ("wooden_bookshelf_worn", "FShelf_broken", (6000, 1800, 450), ("parts", 0.3)),
    ("jug_01", "FJug", (2000, 600, 150), None),
    ("ceramic_pot", "FPot", (2500, 800, 200), None),
    ("stone_fire_pit", "FFirePit", (5000, 1500, 400), None),
    ("wooden_crate_01", "FCrate_broken", (3000, 900, 250), ("parts", 0.3)),
    ("painted_wooden_cabinet", "FCabinet_broken", (5000, 1500, 400), ("parts", 0.2)),
    ("wooden_barrels_01", "FBarrels", (5000, 1500, 400), None),
    ("wooden_ladder", "FLadder_broken", (3000, 900, 250), ("parts", 0.2)),
]

def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)

def save_img(img, path, size=1024):
    im = img.copy(); im.scale(size, size)
    im.filepath_raw = path; im.file_format = 'PNG' if path.endswith('.png') else 'JPEG'
    im.save(); bpy.data.images.remove(im)

def pack_mask(arm, ao_img, out, size=1024):
    # máscara Standard: R metal, G AO, B 0, A suavidad (1 - rugosidad); arm de Poly Haven = AO, rugosidad, metal
    import numpy as np
    if arm is None:
        px = np.zeros((size, size, 4), np.float32); px[..., 1] = 1.0; px[..., 3] = 0.15
    else:
        im = arm.copy(); im.scale(size, size)
        a = np.array(im.pixels[:], np.float32).reshape(size, size, 4); bpy.data.images.remove(im)
        px = np.zeros_like(a); px[..., 0] = a[..., 2]; px[..., 1] = a[..., 0]; px[..., 3] = 1.0 - a[..., 1]
    img = bpy.data.images.new("mask", size, size, alpha=True); img.colorspace_settings.name = 'Non-Color'
    img.pixels = px.ravel().tolist(); img.filepath_raw = out; img.file_format = 'PNG'; img.save(); bpy.data.images.remove(img)

def tex_of(mat, kind):
    if mat is None or not mat.use_nodes: return None
    bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None: return None
    def up(sock):
        for l in sock.links:
            n = l.from_node
            while n is not None and n.type != 'TEX_IMAGE':
                ins = [i for i in n.inputs if i.is_linked]
                n = ins[0].links[0].from_node if ins else None
            if n is not None: return n.image
        return None
    if kind == 'base': return up(bsdf.inputs['Base Color'])
    if kind == 'normal': return up(bsdf.inputs['Normal'])
    if kind == 'arm': return up(bsdf.inputs['Roughness']) or up(bsdf.inputs['Metallic'])
    return None

def remove_parts(ob, mode, amount, rnd):
    bpy.context.view_layer.objects.active = ob; bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.separate(type='LOOSE'); bpy.ops.object.mode_set(mode='OBJECT')
    parts = [o for o in bpy.context.selected_objects]
    if len(parts) < 2: return ob, 0
    zmin = min(min((o.matrix_world @ v.co).z for v in o.data.vertices) for o in parts)
    def info(o):
        ws = [o.matrix_world @ v.co for v in o.data.vertices]
        mn = Vector((min(w.x for w in ws), min(w.y for w in ws), min(w.z for w in ws))); mx = Vector((max(w.x for w in ws), max(w.y for w in ws), max(w.z for w in ws)))
        return mn, mx
    victims = []
    if mode == 'legs':
        cands = []
        for o in parts:
            mn, mx = info(o); d = mx - mn
            if mn.z < zmin + 0.03 and d.z > 2.2 * max(d.x, d.y): cands.append(o)   # pieza que toca el suelo, alta y delgada = pata
        rnd.shuffle(cands); victims = cands[:amount]
    else:
        big = max(parts, key=lambda o: len(o.data.vertices))
        cands = [o for o in parts if o is not big]; rnd.shuffle(cands); victims = cands[:max(1, int(len(cands) * amount))]
    for v in victims: bpy.data.objects.remove(v)
    rest = [o for o in parts if o not in victims]
    bpy.ops.object.select_all(action='DESELECT')
    for o in rest: o.select_set(True)
    bpy.context.view_layer.objects.active = rest[0]; bpy.ops.object.join()
    return bpy.context.active_object, len(victims)

for src, name, lods, breakage in ITEMS:
    if ONLY and ONLY != name: continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, src, src + "_1k.gltf"))
    ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in ms: o.select_set(True)
    bpy.context.view_layer.objects.active = ms[0]
    if len(ms) > 1: bpy.ops.object.join()
    ob = bpy.context.active_object
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM'); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    removed = 0
    if breakage: ob, removed = remove_parts(ob, breakage[0], breakage[1], random.Random(hash(name) & 0xffff))
    vs = [v.co for v in ob.data.vertices]
    mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))); mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    c = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    for v in ob.data.vertices: v.co -= c
    # materiales -> M0..Mn con sus texturas
    for i, m in enumerate(ob.data.materials):
        if m is None: continue
        base, nor, arm = tex_of(m, 'base'), tex_of(m, 'normal'), tex_of(m, 'arm')
        if base is not None: save_img(base, os.path.join(OUT, "%s_m%d_a.%s" % (name, i, "png" if name.startswith("DPlant") else "jpg")))
        if nor is not None: save_img(nor, os.path.join(OUT, "%s_m%d_n.png" % (name, i)))
        pack_mask(arm, None, os.path.join(OUT, "%s_m%d_m.png" % (name, i)))
        m.name = "M%d" % i
    n0 = tris(ob); ob.name = name; out = []
    for i, t in enumerate(lods):
        cp = ob.copy(); cp.data = ob.data.copy(); bpy.context.collection.objects.link(cp); cp.name = name + "_LOD%d" % i
        if t < n0:
            d = cp.modifiers.new("dec", 'DECIMATE'); d.ratio = t / n0
            bpy.context.view_layer.objects.active = cp; bpy.ops.object.select_all(action='DESELECT'); cp.select_set(True); bpy.ops.object.modifier_apply(modifier="dec")
        out.append(cp)
    bpy.data.objects.remove(ob)
    bpy.ops.object.select_all(action='DESELECT')
    for o in out: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True, bake_space_transform=True, path_mode='STRIP')
    print("PROP", name, "src", src, "tris", n0, "size", tuple(round(x, 3) for x in (mx - mn)), "mats", len(out[0].data.materials), "removed", removed, "lods", [tris(o) for o in out], flush=True)
