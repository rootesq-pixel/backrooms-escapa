# Blender 4.2: figura de «la Ventana» (Nivel 188): humanoide delgado y alargado (~2,02 m), malla orgánica por modificador
# Skin + subdivisión, esqueleto con nombres propios y pesos automáticos. Exporta Assets/Resources/W188/Ventana.fbx (CC0, propio).
#   blender -b -P Tools/w188/ventana_build.py -- <salida.fbx>
import bpy, sys, math, random
random.seed(188)
from mathutils import Vector
out = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "/tmp/Ventana.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)

# --- esqueleto de puntos (Blender: Z arriba, la figura mira a -Y) y radios (rx, ry) del Skin
P = {}; E = []; RAD = {}
def v(name, x, y, z, rx, ry=None):
    P[name] = (x, y, z); RAD[name] = (rx, ry if ry is not None else rx)
def e(a, b): E.append((a, b))
v("pelvis", 0, 0.01, 1.06, 0.115, 0.085)
v("waist", 0, 0.015, 1.22, 0.092, 0.07)
v("chest", 0, 0.0, 1.42, 0.13, 0.085)
v("upchest", 0, 0.005, 1.56, 0.125, 0.075)
v("neck0", 0, 0.012, 1.64, 0.042)
v("neck1", 0, 0.0, 1.75, 0.038)
v("jaw", 0, -0.02, 1.80, 0.055, 0.06)
v("head", 0, 0.0, 1.88, 0.082, 0.098)
v("crown", 0, 0.012, 1.99, 0.06, 0.07)
for s in (-1, 1):
    L = "L" if s > 0 else "R"
    v("hip"+L, 0.085*s, 0.01, 1.02, 0.075)
    v("thigh"+L, 0.095*s, 0.0, 0.82, 0.063)
    v("knee"+L, 0.1*s, -0.012, 0.56, 0.044)
    v("calf"+L, 0.1*s, 0.008, 0.38, 0.045)
    v("ankle"+L, 0.1*s, 0.01, 0.08, 0.028)
    v("heel"+L, 0.1*s, 0.03, 0.03, 0.03, 0.026)
    v("toe"+L, 0.105*s, -0.14, 0.02, 0.026, 0.018)
    v("clav"+L, 0.07*s, 0.01, 1.585, 0.05)
    v("shoulder"+L, 0.185*s, 0.015, 1.575, 0.05)
    v("biceps"+L, 0.205*s, 0.015, 1.42, 0.04)
    v("elbow"+L, 0.225*s, 0.02, 1.235, 0.03)
    v("forearm"+L, 0.235*s, 0.01, 1.06, 0.032)
    v("wrist"+L, 0.245*s, 0.0, 0.885, 0.021, 0.016)
    v("palm"+L, 0.25*s, -0.004, 0.83, 0.038, 0.014)
    for k, (dx, ln) in enumerate([(-0.024, 0.088), (-0.008, 0.104), (0.008, 0.098), (0.024, 0.078)]):
        n0 = "f%d%s0" % (k, L); n1 = "f%d%s1" % (k, L); n2 = "f%d%s2" % (k, L)
        v(n0, 0.25*s + dx*s, -0.004, 0.79, 0.0105)
        v(n1, 0.25*s + dx*s*1.05, -0.006, 0.79 - ln*0.55, 0.009)
        v(n2, 0.25*s + dx*s*1.1, -0.008, 0.79 - ln, 0.0068)
        e("palm"+L, n0); e(n0, n1); e(n1, n2)
    v("thumb%s0" % L, 0.235*s, -0.02, 0.84, 0.01); v("thumb%s1" % L, 0.228*s, -0.045, 0.8, 0.008)
    e("wrist"+L, "thumb%s0" % L); e("thumb%s0" % L, "thumb%s1" % L)
    e("pelvis", "hip"+L); e("hip"+L, "thigh"+L); e("thigh"+L, "knee"+L); e("knee"+L, "calf"+L); e("calf"+L, "ankle"+L)
    e("ankle"+L, "heel"+L); e("ankle"+L, "toe"+L)
    e("upchest", "clav"+L); e("clav"+L, "shoulder"+L); e("shoulder"+L, "biceps"+L); e("biceps"+L, "elbow"+L)
    e("elbow"+L, "forearm"+L); e("forearm"+L, "wrist"+L); e("wrist"+L, "palm"+L)
for a, b in [("pelvis", "waist"), ("waist", "chest"), ("chest", "upchest"), ("upchest", "neck0"), ("neck0", "neck1"), ("neck1", "jaw"), ("jaw", "head"), ("head", "crown")]:
    e(a, b)

names = list(P.keys()); idx = {n: i for i, n in enumerate(names)}
me = bpy.data.meshes.new("VentanaSkel")
me.from_pydata([P[n] for n in names], [(idx[a], idx[b]) for a, b in E], [])
ob = bpy.data.objects.new("Ventana", me); bpy.context.collection.objects.link(ob)
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
sk = ob.modifiers.new("Skin", "SKIN"); sk.use_smooth_shade = True; sk.branch_smoothing = 0.6
for n in names:
    sv = me.skin_vertices[0].data[idx[n]]; sv.radius = RAD[n]
me.skin_vertices[0].data[idx["pelvis"]].use_root = True
sub = ob.modifiers.new("Sub", "SUBSURF"); sub.levels = 2; sub.render_levels = 2
bpy.ops.object.modifier_apply(modifier="Skin"); bpy.ops.object.modifier_apply(modifier="Sub")
# piel: irregularidad muy leve (costillas/clavículas marcadas)
for vv in ob.data.vertices:
    co = vv.co
    if 1.3 < co.z < 1.58 and abs(co.x) < 0.12 and co.y > -0.2:
        vv.co = co + vv.normal * (0.0025 * math.sin(co.z * 120))
ob.data.materials.append(bpy.data.materials.new("Skin"))
bpy.ops.object.shade_smooth()

# --- camisón largo (tela con pliegues verticales y bajo deshilachado), unido a la malla con material «Cloth»
KEYS = [(1.645, .085, .06), (1.60, .15, .085), (1.555, .2, .1), (1.45, .17, .108), (1.25, .132, .098), (1.06, .16, .112), (0.85, .19, .128), (0.58, .215, .145)]
def prof(z):
    for (z0, a0, b0), (z1, a1, b1) in zip(KEYS, KEYS[1:]):
        if z1 <= z <= z0:
            t = (z0 - z) / (z0 - z1); return a0 + (a1 - a0) * t, b0 + (b1 - b0) * t
    return KEYS[-1][1], KEYS[-1][2]
NR, NS = 46, 56; verts = []; faces = []
for r in range(NR):
    t = r / (NR - 1); z = 1.645 - t * (1.645 - 0.58)
    rx, ry = prof(z)
    for k in range(NS):
        th = 2 * math.pi * k / NS
        fold = 0.0
        if z < 1.3:
            a = min(1, (1.3 - z) / 0.6)
            fold = a * (0.055 * math.sin(7 * th + 0.6) + 0.03 * math.sin(13 * th + 2.1 + z * 4) + 0.015 * math.sin(23 * th + z * 9))
        elif z < 1.55:
            fold = 0.012 * math.sin(9 * th + z * 30)
        zz = z
        if r == NR - 1:
            zz = z + 0.035 * math.sin(5 * th + 1) + 0.02 * math.sin(17 * th) + 0.012 * random.random()
        verts.append((math.sin(th) * rx * (1 + fold), -math.cos(th) * ry * (1 + fold) + 0.008, zz))
for r in range(NR - 1):
    for k in range(NS):
        a = r * NS + k; b = r * NS + (k + 1) % NS
        faces.append((a, b, b + NS, a + NS))
gm = bpy.data.meshes.new("Gown"); gm.from_pydata(verts, [], faces)
go = bpy.data.objects.new("Gown", gm); bpy.context.collection.objects.link(go)
go.data.materials.append(bpy.data.materials.new("Cloth"))
bpy.ops.object.select_all(action="DESELECT"); bpy.context.view_layer.objects.active = go; go.select_set(True)
so = go.modifiers.new("Sol", "SOLIDIFY"); so.thickness = 0.007; so.offset = 1
ss = go.modifiers.new("Sub", "SUBSURF"); ss.levels = 1
bpy.ops.object.modifier_apply(modifier="Sol"); bpy.ops.object.modifier_apply(modifier="Sub"); bpy.ops.object.shade_smooth()
ob.select_set(True); bpy.context.view_layer.objects.active = ob; bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active

# UV cilíndricas en metros (la textura de tela de las cortinas se repite cada ~0,33 m)
uvl = ob.data.uv_layers.new(name="UVMap")
for poly in ob.data.polygons:
    for li in poly.loop_indices:
        co = ob.data.vertices[ob.data.loops[li].vertex_index].co
        th = math.atan2(co.x, -co.y)
        uvl.data[li].uv = (th * 0.15 * 3.0, co.z * 3.0)

# --- armadura con nombres
arm = bpy.data.armatures.new("VentanaRig"); ao = bpy.data.objects.new("VentanaRig", arm); bpy.context.collection.objects.link(ao)
bpy.context.view_layer.objects.active = ao; bpy.ops.object.mode_set(mode="EDIT")
def bone(name, a, b, parent=None):
    bb = arm.edit_bones.new(name); bb.head = Vector(P[a]) if isinstance(a, str) else Vector(a); bb.tail = Vector(P[b]) if isinstance(b, str) else Vector(b)
    if parent: bb.parent = arm.edit_bones[parent]; bb.use_connect = False
    return bb
bone("Hips", "pelvis", "waist"); bone("Spine", "waist", "chest", "Hips"); bone("Chest", "chest", "upchest", "Spine")
bone("Neck", "neck0", "neck1", "Chest"); bone("Head", "neck1", "crown", "Neck")
for L in ("L", "R"):
    bone("Clavicle."+L, "clav"+L, "shoulder"+L, "Chest"); bone("UpperArm."+L, "shoulder"+L, "elbow"+L, "Clavicle."+L)
    bone("Forearm."+L, "elbow"+L, "wrist"+L, "UpperArm."+L); bone("Hand."+L, "wrist"+L, "palm"+L, "Forearm."+L)
    for k in range(4):
        bone("Finger%d.%s.1" % (k, L), "f%d%s0" % (k, L), "f%d%s1" % (k, L), "Hand."+L)
        bone("Finger%d.%s.2" % (k, L), "f%d%s1" % (k, L), "f%d%s2" % (k, L), "Finger%d.%s.1" % (k, L))
    bone("Thumb."+L, "thumb%s0" % L, "thumb%s1" % L, "Hand."+L)
    bone("Thigh."+L, "hip"+L, "knee"+L, "Hips"); bone("Shin."+L, "knee"+L, "ankle"+L, "Thigh."+L); bone("Foot."+L, "ankle"+L, "toe"+L, "Shin."+L)
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.object.select_all(action="DESELECT"); ob.select_set(True); ao.select_set(True); bpy.context.view_layer.objects.active = ao
try:
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
except Exception as ex:
    print("auto weights fallo:", ex)
# el camisón no sigue a los brazos: se quitan los pesos de brazo/mano en sus vértices y se renormaliza (si no, al levantar
# la mano arrastra media prenda y sale un «ala» enorme en la silueta)
gv = set()
for poly in ob.data.polygons:
    if poly.material_index == 1: gv.update(poly.vertices)
armg = [vg.index for vg in ob.vertex_groups if any(vg.name.startswith(p) for p in ("Clavicle", "UpperArm", "Forearm", "Hand", "Finger", "Thumb"))]
names_by_idx = {vg.index: vg.name for vg in ob.vertex_groups}
for vi in gv:
    vt = ob.data.vertices[vi]
    for gi in armg:
        try: ob.vertex_groups[names_by_idx[gi]].remove([vi])
        except RuntimeError: pass
    gs = [(g.group, g.weight) for g in vt.groups]
    tot = sum(w for _, w in gs)
    if tot < 1e-4: ob.vertex_groups["Chest"].add([vi], 1.0, "REPLACE")
    else:
        for gi, w in gs: ob.vertex_groups[names_by_idx[gi]].add([vi], w / tot, "REPLACE")
print("camison", len(gv))
empty = [vg.name for vg in ob.vertex_groups]
print("grupos", len(empty), "vertices", len(ob.data.vertices), "caras", len(ob.data.polygons))
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         add_leaf_bones=False, bake_anim=False, use_armature_deform_only=True, mesh_smooth_type="FACE", object_types={"ARMATURE", "MESH"})
print("OK", out)
