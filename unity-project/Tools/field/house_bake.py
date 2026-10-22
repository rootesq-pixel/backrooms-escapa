# v7.2.0: horneado de la luz del cielo nublado para las casas del Campo infinito (Blender Cycles, GPU con CYCLES_DEVICE=GPU).
#   blender -b --factory-startup --python house_bake.py -- houses.bytes sky.hdr out_dir SPP [W H]
# houses.bytes: V720HouseExport (coords de Unity, UV2 ya empaquetada en el atlas 3x2). Salida: lm_raw.npy (float32 HxWx3) + preview.
import bpy, bmesh, sys, os, struct, math
import numpy as np
argv = sys.argv[sys.argv.index('--') + 1:]
SRC, HDR, OUT, SPP = argv[0], argv[1], argv[2].rstrip('/\\') + '/', int(argv[3])
W = int(argv[4]) if len(argv) > 4 else 3072; H = int(argv[5]) if len(argv) > 5 else 2048
os.makedirs(OUT, exist_ok=True)
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
sc = bpy.context.scene

def cycles_device():
    dev = os.environ.get('CYCLES_DEVICE', 'CPU').upper()
    if dev == 'CPU': sc.cycles.device = 'CPU'; return
    p = bpy.context.preferences.addons['cycles'].preferences
    for t in ('OPTIX', 'CUDA'):
        try: p.compute_device_type = t; p.get_devices()
        except TypeError: continue
        if any(d.type == t for d in p.devices): break
    for d in p.devices: d.use = (d.type == p.compute_device_type)
    sc.cycles.device = 'GPU'; print('cycles device', p.compute_device_type, flush=True)

# albedo lineal aproximado de cada submesh (rebote): paredes, tejado, suelo, madera, cristal, piedra
ALB = [(0.30, 0.27, 0.23), (0.20, 0.19, 0.18), (0.25, 0.20, 0.16), (0.22, 0.21, 0.19), (0.03, 0.035, 0.035), (0.28, 0.24, 0.19)]
mats = []
for i, c in enumerate(ALB):
    m = bpy.data.materials.new('M%d' % i); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = (*c, 1); b.inputs['Roughness'].default_value = 0.85 if i != 4 else 0.1
    mats.append(m)
img = bpy.data.images.new('LM', W, H, float_buffer=True, alpha=False)
for m in mats:
    t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = img; m.node_tree.nodes.active = t
gm = bpy.data.materials.new('Ground'); gm.use_nodes = True
gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.075, 0.095, 0.040, 1)
gm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 1.0

f = open(SRC, 'rb'); rd = lambda fmt: struct.unpack('<' + fmt, f.read(struct.calcsize('<' + fmt)))
n_mesh = rd('i')[0]; objs = []
for k in range(n_mesh):
    var, nv, ns = rd('iii')
    V = []; UV0 = []; UV1 = []
    for i in range(nv):
        x, y, z, nx, ny, nz, u0, v0, u1, v1 = rd('10f')
        V.append((x, z, y)); UV0.append((u0, v0)); UV1.append((u1, v1))   # Unity (x,y,z) -> Blender (x,z,y)
    bm = bmesh.new(); bv = [bm.verts.new(p) for p in V]; bm.verts.ensure_lookup_table()
    l0 = bm.loops.layers.uv.new('UV0'); l1 = bm.loops.layers.uv.new('UV1')
    for s in range(ns):
        cnt = rd('i')[0]; idx = rd('%di' % cnt)
        for t in range(0, cnt, 3):
            tri = (idx[t], idx[t + 2], idx[t + 1])   # el cambio de ejes invierte el sentido
            try: fc = bm.faces.new([bv[j] for j in tri])
            except ValueError: continue
            fc.material_index = s
            for lp, j in zip(fc.loops, tri): lp[l0].uv = UV0[j]; lp[l1].uv = UV1[j]
    me = bpy.data.meshes.new('House%d' % var); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    ob = bpy.data.objects.new('House%d' % var, me); sc.collection.objects.link(ob)
    ox = (var % 3) * 60.0; oy = (var // 3) * 60.0; ob.location = (ox, oy, 0.0)
    me.uv_layers.active = me.uv_layers['UV1']
    objs.append(ob)
    # suelo bajo cada casa (rebote verde oliva de la pradera), fuera del lightmap
    bpy.ops.mesh.primitive_plane_add(size=50, location=(ox, oy, -0.02)); g = bpy.context.object; g.data.materials.append(gm); g.name = 'Ground%d' % var
print('casas', n_mesh, flush=True)

w = bpy.data.worlds.new('Sky'); sc.world = w; w.use_nodes = True
nt = w.node_tree; env = nt.nodes.new('ShaderNodeTexEnvironment'); env.image = bpy.data.images.load(HDR)
nt.links.new(env.outputs[0], nt.nodes['Background'].inputs[0]); nt.nodes['Background'].inputs[1].default_value = 1.0

sc.render.engine = 'CYCLES'; cycles_device(); cy = sc.cycles; cy.samples = SPP; cy.use_denoising = False
cy.max_bounces = 6; cy.diffuse_bounces = 4
bk = sc.render.bake; bk.use_pass_direct = True; bk.use_pass_indirect = True; bk.use_pass_color = False
bk.margin = 4; bk.margin_type = 'EXTEND'; bk.target = 'IMAGE_TEXTURES'
for o in bpy.data.objects: o.select_set(o.name.startswith('House'))
bpy.context.view_layer.objects.active = objs[0]
bpy.ops.object.bake(type='DIFFUSE', uv_layer='UV1')
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)[:, :, :3]
np.save(OUT + 'house_lm_raw.npy', px)
np.savez_compressed(OUT + 'house_lm_raw16.npz', lm=px.astype(np.float16))
print('BAKE OK', px.mean(), np.median(px[px.sum(2) > 0], axis=0) if (px.sum(2) > 0).any() else 0, flush=True)
