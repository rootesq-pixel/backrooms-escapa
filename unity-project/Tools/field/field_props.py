# v7.0.1 Campo infinito: árboles muertos y poste de madera para Unity (FBX con _LOD0/_LOD1/_LOD2).
# Árbol: esqueleto ramificado -> modificador Skin (una sola malla cerrada, ramas soldadas al tronco), raíces que se hunden
# en el suelo, desplazamiento de corteza. Textura en Unity por triplanar (Backrooms/FieldBark) con PBR CC0 (Poly Haven).
import bpy, bmesh, math, random, sys
from mathutils import Vector, Matrix
OUT = sys.argv[sys.argv.index("--") + 1]

def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def tree(seed, name):
    rnd = random.Random(seed)
    bm = bmesh.new(); rad = {}
    def vert(p, r):
        v = bm.verts.new(p); rad[v] = r; return v
    def branch(v0, d, length, r, depth):
        segs = max(2, int(length / 0.45)); prev = v0; p = Vector(v0.co); dd = d.copy()
        for i in range(segs):
            dd = (dd + Vector((rnd.uniform(-.25, .25), rnd.uniform(-.25, .25), rnd.uniform(-.12, .18)))).normalized()
            p = p + dd * (length / segs)
            rr = r * (1 - 0.75 * (i + 1) / segs)
            v = vert(p, max(rr, 0.018)); bm.edges.new((prev, v)); prev = v
            if depth < 3 and i >= 1 and rnd.random() < (0.55 if depth == 0 else 0.4):
                a = rnd.uniform(0, 2 * math.pi); up = rnd.uniform(0.25, 0.75)
                nd = Vector((math.cos(a), math.sin(a), up)).normalized()
                branch(v, nd, length * rnd.uniform(0.35, 0.6), rr * 0.6, depth + 1)
        # rama rota: punta mocha
        return prev
    # raíces: arrancan del pie y se hunden en la tierra
    base = vert(Vector((0, 0, 0.25)), 0.30)
    for k in range(5):
        a = k * 2 * math.pi / 5 + rnd.uniform(-.3, .3); prev = base
        for j, (dist, z, r) in enumerate([(0.35, 0.05, 0.17), (0.75, -0.08, 0.10), (1.1, -0.25, 0.05)]):
            v = vert(Vector((math.cos(a) * dist, math.sin(a) * dist, z)), r); bm.edges.new((prev, v)); prev = v
    v0 = vert(Vector((0, 0, -0.4)), 0.26); bm.edges.new((v0, base))
    H = rnd.uniform(4.2, 5.6)
    trunk_top = branch(base, Vector((rnd.uniform(-.1, .1), rnd.uniform(-.1, .1), 1)).normalized(), H, 0.24, 0)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob; ob.select_set(True)
    sk = ob.modifiers.new("skin", 'SKIN'); sk.use_smooth_shade = True
    for v in me.vertices:
        pass
    sv = me.skin_vertices[0].data
    vlist = list(rad.values())
    for i, d in enumerate(sv): d.radius = (vlist[i], vlist[i])
    me.skin_vertices[0].data[0].use_root = True
    ob.modifiers.new("sub", 'SUBSURF').levels = 1
    tex = bpy.data.textures.new(name + "_n", 'CLOUDS'); tex.noise_scale = 0.18
    ds = ob.modifiers.new("disp", 'DISPLACE'); ds.texture = tex; ds.strength = 0.035; ds.mid_level = 0.5
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT'); bpy.ops.mesh.select_loose(); bpy.ops.mesh.delete(type='VERT'); bpy.ops.object.mode_set(mode='OBJECT')
    return ob

def pole(name):
    parts = []
    def add(op, mat, **kw):
        op(**kw); o = bpy.context.active_object; o.data.materials.append(mat); parts.append(o); return o
    wood = bpy.data.materials.new("PoleWood"); metal = bpy.data.materials.new("PoleMetal"); cer = bpy.data.materials.new("PoleCeramic")
    p = add(bpy.ops.mesh.primitive_cone_add, wood, vertices=20, radius1=0.15, radius2=0.11, depth=9.4, location=(0, 0, 9.4 / 2 - 1.2))
    add(bpy.ops.mesh.primitive_cube_add, wood, size=1, location=(0, 0, 7.5)).scale = (0.11, 2.0, 0.11)
    for s in (-1, 1):
        b = add(bpy.ops.mesh.primitive_cube_add, metal, size=1, location=(0, s * 0.35, 7.25)); b.scale = (0.03, 0.75, 0.03); b.rotation_euler = (s * 0.6, 0, 0)
    for y in (-0.85, 0.0, 0.85):
        add(bpy.ops.mesh.primitive_cylinder_add, metal, vertices=8, radius=0.015, depth=0.16, location=(0, y, 7.62))
        add(bpy.ops.mesh.primitive_cone_add, cer, vertices=14, radius1=0.07, radius2=0.035, depth=0.14, location=(0, y, 7.72))
        add(bpy.ops.mesh.primitive_cylinder_add, cer, vertices=14, radius=0.032, depth=0.06, location=(0, y, 7.81))
    for z in range(0, 6):
        st = add(bpy.ops.mesh.primitive_cylinder_add, metal, vertices=6, radius=0.012, depth=0.3, location=(0.12 if z % 2 else -0.12, 0, 2.4 + z * 0.45)); st.rotation_euler = (0, math.pi / 2, 0)
    add(bpy.ops.mesh.primitive_cube_add, metal, size=1, location=(0.14, 0, 2.0)).scale = (0.01, 0.1, 0.14)   # placa de numeración
    # montículo de tierra al pie (se funde con el terreno)
    m = add(bpy.ops.mesh.primitive_uv_sphere_add, wood, segments=16, ring_count=8, radius=0.5, location=(0, 0, -0.32)); m.scale = (1, 1, 0.55)
    m.data.materials[0] = bpy.data.materials.new("PoleDirt")
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]; bpy.ops.object.join()
    o = bpy.context.active_object; o.name = name; o.data.name = name
    return o

def lods(ob, ratios):
    out = []
    for i, r in enumerate(ratios):
        c = ob.copy(); c.data = ob.data.copy(); bpy.context.collection.objects.link(c)
        c.name = ob.name + "_LOD%d" % i
        if r < 1:
            d = c.modifiers.new("dec", 'DECIMATE'); d.ratio = r
            bpy.context.view_layer.objects.active = c; bpy.ops.object.select_all(action='DESELECT'); c.select_set(True)
            bpy.ops.object.modifier_apply(modifier="dec")
        out.append(c)
    bpy.data.objects.remove(ob)
    return out

def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True, bake_space_transform=True)

clear()
for i, seed in enumerate([11, 23, 37]):
    t = tree(seed, "DeadTree%d" % i)
    m = bpy.data.materials.new("Bark"); t.data.materials.append(m)
    ls = lods(t, [1.0, 0.3, 0.08])
    print("tree", i, [len(o.data.polygons) for o in ls])
    export(ls, OUT + "/DeadTree%d.fbx" % i)
    for o in ls: bpy.data.objects.remove(o)
p = pole("UtilityPole")
ls = lods(p, [1.0, 0.45, 0.15]); print("pole", [len(o.data.polygons) for o in ls])
export(ls, OUT + "/UtilityPole.fbx")
