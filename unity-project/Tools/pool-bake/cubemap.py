# v6.9: cubemap de reflexión renderizado con Cycles desde el centro de la sala (6 caras, convención DX de Unity)
import bpy, sys, os
from mathutils import Matrix, Vector
argv = sys.argv[sys.argv.index('--')+1:]
SPP = int(argv[0]); RES = int(argv[1]); CY = float(argv[2]) if len(argv) > 2 else 1.4
OUT = os.environ.get('POOL_OUT', os.path.dirname(os.path.abspath(__file__))).replace('\\', '/').rstrip('/') + '/cube/'
os.makedirs(OUT, exist_ok=True)
TEX = os.environ.get('POOL_TEX', '/workspace/BackroomsEscape/Assets/Resources/Textures/PoolRoom/').replace('\\', '/').rstrip('/') + '/'
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

sc = bpy.context.scene
ob = bpy.data.objects['PoolRoomFixed'] if 'PoolRoomFixed' in bpy.data.objects else [o for o in sc.objects if o.type == 'MESH'][0]
me = ob.data
def texmat(name, img, rough, tint=(1,1,1)):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TEX + img)
    uv = nt.nodes.new('ShaderNodeUVMap'); uv.uv_map = 'UV0'
    nt.links.new(uv.outputs[0], t.inputs[0])
    mix = nt.nodes.new('ShaderNodeMixRGB'); mix.blend_type = 'MULTIPLY'; mix.inputs[0].default_value = 1
    mix.inputs[2].default_value = (*tint, 1)
    nt.links.new(t.outputs[0], mix.inputs[1]); nt.links.new(mix.outputs[0], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = rough
    return m
old = list(me.materials)
new = [texmat('W', 'WallTile_a.jpg', 0.3), texmat('D', 'DeckTile_a.jpg', 0.35), texmat('P', 'PoolTile_a.jpg', 0.3), texmat('C', 'WallTile_a.jpg', 0.6, (0.97,0.97,0.97)), old[4]]
for i, m in enumerate(new): me.materials[i] = m
# agua
PX, PY = 4.514, 2.36
bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0, -0.06)); w = bpy.context.object; w.scale = (PX*2, PY*2, 1)
wm = bpy.data.materials.new('Water'); wm.use_nodes = True; b = wm.node_tree.nodes['Principled BSDF']
b.inputs['Base Color'].default_value = (0.75, 0.95, 0.96, 1); b.inputs['Roughness'].default_value = 0.03
b.inputs['IOR'].default_value = 1.333; b.inputs['Transmission Weight'].default_value = 1.0
w.data.materials.append(wm)
sc.render.engine = 'CYCLES'; cycles_device(sc); sc.cycles.samples = SPP; sc.cycles.use_denoising = True
sc.render.resolution_x = sc.render.resolution_y = RES; sc.render.resolution_percentage = 100
sc.render.image_settings.file_format = 'OPEN_EXR'; sc.render.image_settings.color_depth = '16'
sc.view_settings.view_transform = 'Standard'
cd = bpy.data.cameras.new('cube'); cd.angle = 1.5707963; cd.clip_start = 0.02
cam = bpy.data.objects.new('cube', cd); sc.collection.objects.link(cam); sc.camera = cam
def b(v): return Vector((v[0], v[2], v[1]))   # Unity -> Blender
faces = [('px',(1,0,0),(0,1,0),(0,0,-1)), ('nx',(-1,0,0),(0,1,0),(0,0,1)), ('py',(0,1,0),(0,0,-1),(1,0,0)),
         ('ny',(0,-1,0),(0,0,1),(1,0,0)), ('pz',(0,0,1),(0,1,0),(1,0,0)), ('nz',(0,0,-1),(0,1,0),(-1,0,0))]
for n, d, u, r in faces:
    R = Matrix((b(r), b(u), -b(d))).transposed()
    cam.matrix_world = Matrix.Translation((0, 0, CY)) @ R.to_4x4()
    sc.render.filepath = OUT + n + '.exr'
    bpy.ops.render.render(write_still=True)
print('CUBEDONE')
