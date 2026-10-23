# v7.3.0: utilidades comunes para el sofá y la barca (Blender 4.2, modelado y texturas procedurales propias, CC0)
import bpy, bmesh, os, math, random
import numpy as np
from mathutils import Vector, Matrix, noise as mnoise

sc = None; LOG = None
def init(out, logname):
    global sc, LOG
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene; os.makedirs(out, exist_ok=True)
    LOG = open(os.path.join(out, logname), 'w'); return sc
def L(*a): LOG.write(' '.join(str(x) for x in a) + '\n'); LOG.flush()
def activate(o):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
def apply_mods(o):
    activate(o)
    for m in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)
def obj_from_bm(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o
def box(name, lo, hi, bevel=0.0, seg=3, subd=0):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    c = (Vector(lo) + Vector(hi)) * 0.5; s = Vector(hi) - Vector(lo)
    for v in bm.verts: v.co = Vector((v.co.x * s.x, v.co.y * s.y, v.co.z * s.z)) + c
    o = obj_from_bm(name, bm)
    if bevel > 0:
        m = o.modifiers.new('bev', 'BEVEL'); m.width = bevel; m.segments = seg; m.limit_method = 'NONE'
    if subd > 0:
        m = o.modifiers.new('sub', 'SUBSURF'); m.levels = subd; m.render_levels = subd
    apply_mods(o); return o
def cyl(name, c, r, h, axis='Z', seg=16, r2=None):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
    rot = {'Z': Matrix.Identity(3), 'X': Matrix.Rotation(math.pi / 2, 3, 'Y'), 'Y': Matrix.Rotation(math.pi / 2, 3, 'X')}[axis]
    for v in bm.verts: v.co = rot @ v.co + Vector(c)
    return obj_from_bm(name, bm)
def sweep(name, pts, w, h, up_hint=Vector((0, 0, 1)), closed=False, side=None):
    """barrido de un rectángulo w (ancho, perpendicular en el plano) x h (alto) a lo largo de pts"""
    bm = bmesh.new(); rings = []
    n = len(pts)
    for i, p in enumerate(pts):
        a = pts[max(i - 1, 0)]; b = pts[min(i + 1, n - 1)]; t = (b - a).normalized()
        u = up_hint if side is None else side(i, p)
        x = t.cross(u).normalized(); y = x.cross(t).normalized()
        ring = [bm.verts.new(p + x * sx * w * 0.5 + y * sy * h * 0.5) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        rings.append(ring)
    for i in range(n - 1):
        for k in range(4):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % 4], rings[i + 1][(k + 1) % 4], rings[i + 1][k]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bm(name, bm)
def set_attr(o, name, fn):
    """atributo de color por vértice (POINT, FLOAT_COLOR) calculado con fn(co, normal, index)"""
    me = o.data
    a = me.color_attributes.get(name) or me.color_attributes.new(name, 'FLOAT_COLOR', 'POINT')
    for i, v in enumerate(me.vertices):
        a.data[i].color = fn(o.matrix_world @ v.co, (o.matrix_world.to_3x3() @ v.normal).normalized(), i)
def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name; return o
def smooth(o, angle=None):
    activate(o)
    if angle is None: bpy.ops.object.shade_smooth()
    else: bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))

# ---------------------------------------------------------------- nodos
class NT:
    def __init__(self, mat):
        mat.use_nodes = True; self.nt = mat.node_tree; self.nt.nodes.clear(); self.Lk = self.nt.links.new
        self.out = self.N('ShaderNodeOutputMaterial'); self.bsdf = self.N('ShaderNodeBsdfPrincipled')
        self.Lk(self.bsdf.outputs[0], self.out.inputs['Surface'])
        self.geo = self.N('ShaderNodeNewGeometry')
        s = self.N('ShaderNodeSeparateXYZ'); self.Lk(self.geo.outputs['Position'], s.inputs[0]); self.PX, self.PY, self.PZ = s.outputs
    def N(self, t, **kw):
        n = self.nt.nodes.new(t)
        for k, v in kw.items(): setattr(n, k, v)
        return n
    def _in(self, sock, x):
        if isinstance(x, (int, float)): sock.default_value = x
        elif isinstance(x, tuple): sock.default_value = x if len(x) == 4 or not hasattr(sock.default_value, '__len__') or len(sock.default_value) == 3 else x + (1,)
        else: self.Lk(x, sock)
    def m(self, op, a, b=None, c=None, clamp=False):
        n = self.N('ShaderNodeMath', operation=op); n.use_clamp = clamp
        for i, x in enumerate([a, b, c]):
            if x is not None: self._in(n.inputs[i], x)
        return n.outputs[0]
    def mr(self, x, a, b, c=0.0, d=1.0, smooth=False):
        n = self.N('ShaderNodeMapRange'); n.clamp = True
        if smooth: n.interpolation_type = 'SMOOTHSTEP'
        for i, v in zip(range(1, 5), (a, b, c, d)): n.inputs[i].default_value = v
        self._in(n.inputs[0], x); return n.outputs[0]
    def vec(self, x, y, z):
        n = self.N('ShaderNodeCombineXYZ')
        for i, v in enumerate((x, y, z)): self._in(n.inputs[i], v)
        return n.outputs[0]
    def vm(self, op, a, b=None, scale=None):
        n = self.N('ShaderNodeVectorMath', operation=op)
        self._in(n.inputs[0], a)
        if b is not None: self._in(n.inputs[1], b)
        if scale is not None: n.inputs['Scale'].default_value = scale
        return n.outputs[0] if op not in ('DOT_PRODUCT', 'LENGTH', 'DISTANCE') else n.outputs['Value']
    def noise(self, scale, detail=4, rough=0.55, dist=0.0, vec=None, col=False):
        n = self.N('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
        n.inputs['Roughness'].default_value = rough; n.inputs['Distortion'].default_value = dist
        self.Lk(vec if vec is not None else self.geo.outputs['Position'], n.inputs['Vector'])
        return n.outputs['Color'] if col else n.outputs['Fac']
    def voro(self, scale, feat='F1', out_='Distance', vec=None, rand=1.0):
        n = self.N('ShaderNodeTexVoronoi'); n.feature = feat; n.inputs['Scale'].default_value = scale; n.inputs['Randomness'].default_value = rand
        self.Lk(vec if vec is not None else self.geo.outputs['Position'], n.inputs['Vector']); return n.outputs[out_]
    def wave(self, scale, dist, vec, detail=3, kind='BANDS', direction='X', profile='SIN'):
        n = self.N('ShaderNodeTexWave'); n.wave_type = kind; n.bands_direction = direction; n.wave_profile = profile
        n.inputs['Scale'].default_value = scale; n.inputs['Distortion'].default_value = dist; n.inputs['Detail'].default_value = detail
        self.Lk(vec, n.inputs['Vector']); return n.outputs['Fac']
    def mix(self, f, a, b):
        n = self.N('ShaderNodeMix', data_type='RGBA')
        self._in(n.inputs[0], f)
        for i, c in ((6, a), (7, b)):
            if isinstance(c, tuple): n.inputs[i].default_value = c + (1,) if len(c) == 3 else c
            else: self.Lk(c, n.inputs[i])
        return n.outputs[2]
    def mulc(self, c, f):
        n = self.N('ShaderNodeMix', data_type='RGBA'); n.blend_type = 'MULTIPLY'; n.inputs[0].default_value = 1.0
        self._in(n.inputs[6], c)
        if isinstance(f, (int, float)): n.inputs[7].default_value = (f, f, f, 1)
        else: self.Lk(f, n.inputs[7])
        return n.outputs[2]
    def attr(self, name):
        a = self.N('ShaderNodeAttribute'); a.attribute_name = name; s = self.N('ShaderNodeSeparateColor'); self.Lk(a.outputs['Color'], s.inputs[0])
        return s.outputs[0], s.outputs[1], s.outputs[2]
    def bump(self, h, strength, dist=0.01):
        b = self.N('ShaderNodeBump'); b.inputs['Strength'].default_value = strength; b.inputs['Distance'].default_value = dist
        self._in(b.inputs['Height'], h); self.Lk(b.outputs['Normal'], self.bsdf.inputs['Normal']); return b

# ---------------------------------------------------------------- GPU, horneado y exportación
def gpu():
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    for t in ('OPTIX', 'CUDA'):
        try:
            prefs.compute_device_type = t; prefs.get_devices()
            if any(d.type == t for d in prefs.devices): break
        except Exception as e: L('dev', t, e)
    for d in prefs.devices: d.use = (d.type != 'CPU')
    sc.cycles.device = 'GPU' if any(d.use for d in prefs.devices) else 'CPU'
    L('device', prefs.compute_device_type, sc.cycles.device, [(d.name, d.type, d.use) for d in prefs.devices])
def uv(objs, margin=0.003):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=margin, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
def bake(objs, nt, S, out, base, emit=None, metal=None, ao_samples=96):
    """hornea albedo, normal, rugosidad, AO (y emisión, metal) -> base_{a,n,m[,e]}.png"""
    sc = bpy.context.scene
    keys = ['albedo', 'normal', 'rough', 'ao'] + (['emit'] if emit is not None else []) + (['metal'] if metal is not None else [])
    imgs = {}
    for k in keys:
        im = bpy.data.images.new('B_' + base + k, S, S, alpha=False, float_buffer=(k == 'normal'))
        if k != 'albedo' and k != 'emit': im.colorspace_settings.name = 'Non-Color'
        imgs[k] = im
    tex = nt.nt.nodes.new('ShaderNodeTexImage'); nt.nt.nodes.active = tex
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    sc.render.bake.margin = 8; sc.render.bake.use_selected_to_active = False
    for k in keys:
        tex.image = imgs[k]; typ = {'albedo': 'DIFFUSE', 'normal': 'NORMAL', 'rough': 'ROUGHNESS', 'ao': 'AO', 'emit': 'EMIT', 'metal': 'EMIT'}[k]
        sc.cycles.samples = ao_samples if k == 'ao' else 4
        if k == 'emit': nt.Lk(emit, nt.out.inputs['Surface'])
        if k == 'metal':
            e = nt.N('ShaderNodeEmission'); nt._in(e.inputs[0], metal); nt.Lk(e.outputs[0], nt.out.inputs['Surface'])
        kw = dict(pass_filter={'COLOR'}) if typ == 'DIFFUSE' else {}
        bpy.ops.object.bake(type=typ, **kw)
        if k in ('emit', 'metal'): nt.Lk(nt.bsdf.outputs[0], nt.out.inputs['Surface'])
        L('baked', base, k)
    nt.nt.nodes.remove(tex)
    def arr(im): return np.array(im.pixels[:], dtype=np.float32).reshape(S, S, 4)
    def save(px, fn, srgb):
        im = bpy.data.images.new(fn, S, S, alpha=True); im.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
        im.pixels[:] = px.ravel(); im.filepath_raw = os.path.join(out, fn + '.png'); im.file_format = 'PNG'; im.save()
    a = arr(imgs['albedo']); n = arr(imgs['normal']); r = arr(imgs['rough']); ao = arr(imgs['ao'])
    alb = a.copy(); alb[..., :3] *= (0.55 + 0.45 * ao[..., :1]); alb[..., 3] = 1; save(alb, base + '_a', True)
    n[..., 3] = 1; save(n, base + '_n', False)
    mk = np.zeros_like(a); mk[..., 1] = ao[..., 0]; mk[..., 3] = 1 - r[..., 0]
    if metal is not None: mk[..., 0] = arr(imgs['metal'])[..., 0]
    save(mk, base + '_m', False)
    if emit is not None:
        e = arr(imgs['emit']); e[..., 3] = 1; save(e, base + '_e', True)
    L('stats', base, 'albedo', alb[..., :3].mean(axis=(0, 1)), 'smooth', mk[..., 3].mean(), 'ao', mk[..., 1].mean())
def preview(out, views, res=(1200, 800), samples=48, world=(0.5, 0.55, 0.58), lights=()):
    sc = bpy.context.scene
    w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = world + (1,)
    w.node_tree.nodes['Background'].inputs[1].default_value = 1.0
    for nm, loc, en in lights:
        l_ = bpy.data.objects.new(nm, bpy.data.lights.new(nm, 'POINT')); l_.data.energy = en; l_.location = loc; sc.collection.objects.link(l_)
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = res; sc.cycles.samples = samples
    for nm, c, t in views:
        cam.location = c; cam.rotation_euler = (Vector(t) - Vector(c)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out, 'preview_' + nm + '.png'); bpy.ops.render.render(write_still=True)
def export(out, fn, objs):
    sc = bpy.context.scene
    for o in list(sc.objects):
        if o.type != 'MESH': bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, fn), use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL',
                             bake_space_transform=True, mesh_smooth_type='FACE', use_tspace=False, path_mode='STRIP', bake_anim=False, use_mesh_modifiers=True)
    L('exported', fn)
