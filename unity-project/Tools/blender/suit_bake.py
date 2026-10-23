# v7.3.0: traje hazmat realista. Importa Hazmat.fbx (Quaternius, CC0), subdivide el cuerpo, despliega UV,
# hornea texturas procedurales propias (albedo, normales, rugosidad, AO, metal) con Cycles en la GPU y exporta HazmatHD.fbx.
import bpy, sys, os, math
import numpy as np
argv = sys.argv[sys.argv.index('--')+1:]
FBX, OUT = argv[0], argv[1]
SKIP_BAKE = os.environ.get('SUIT_SKIP_BAKE') == '1'
HAND_K = float(os.environ.get('SUIT_HAND_K', '0.66'))   # v7.3.0: guantes a escala realista (antes enormes)
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = bpy.data.objects['Character_Hazmat']; head = bpy.data.objects['Character_Hazmat_Head']
log = open(os.path.join(OUT, 'suit_bake.log'), 'w')
def L(*a): log.write(' '.join(str(x) for x in a) + '\n'); log.flush()

# ---- GPU
sc = bpy.context.scene; sc.render.engine = 'CYCLES'
prefs = bpy.context.preferences.addons['cycles'].preferences
for t in ('OPTIX', 'CUDA'):
    try:
        prefs.compute_device_type = t; prefs.get_devices()
        if any(d.type == t for d in prefs.devices): break
    except Exception as e: L('dev', t, e)
for d in prefs.devices: d.use = (d.type != 'CPU')
sc.cycles.device = 'GPU'; L('device', prefs.compute_device_type, [(d.name, d.type, d.use) for d in prefs.devices])

# ---- subdivisión del cuerpo (silueta suave) + sombreado suave
def apply_subsurf(o, lv):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    m = o.modifiers.new('Sub', 'SUBSURF'); m.levels = lv; m.render_levels = lv; m.quality = 3
    while o.modifiers[0].name != 'Sub': bpy.ops.object.modifier_move_up(modifier='Sub')
    bpy.ops.object.modifier_apply(modifier='Sub')
    for p in o.data.polygons: p.use_smooth = True
apply_subsurf(body, 1)
for p in head.data.polygons: p.use_smooth = True
L('body verts', len(body.data.vertices), 'head verts', len(head.data.vertices))

# ---- UV
def unwrap(o):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006, area_weight=0.0, correct_aspect=True, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
unwrap(body); unwrap(head)
arm.data.pose_position = 'REST'
bpy.context.view_layer.update()

# ---- materiales procedurales (coordenadas de mundo en postura de reposo, metros, z arriba; el frente mira a -Y)
def N(nt, t, **kw):
    n = nt.nodes.new(t)
    for k, v in kw.items(): setattr(n, k, v)
    return n
def build(mat, kind):
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear(); Lk = nt.links.new
    out = N(nt, 'ShaderNodeOutputMaterial'); bsdf = N(nt, 'ShaderNodeBsdfPrincipled')
    geo = N(nt, 'ShaderNodeNewGeometry')
    sep = N(nt, 'ShaderNodeSeparateXYZ'); Lk(geo.outputs['Position'], sep.inputs[0])
    sepn = N(nt, 'ShaderNodeSeparateXYZ'); Lk(geo.outputs['Normal'], sepn.inputs[0])
    def noise(scale, detail=4, rough=0.55, vec=None):
        n = N(nt, 'ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail; n.inputs['Roughness'].default_value = rough
        Lk(vec if vec is not None else geo.outputs['Position'], n.inputs['Vector']); return n.outputs['Fac']
    def math_(op, a, b=None, clamp=False):
        n = N(nt, 'ShaderNodeMath', operation=op); n.use_clamp = clamp
        for i, x in enumerate([a, b]):
            if x is None: continue
            if isinstance(x, (int, float)): n.inputs[i].default_value = x
            else: Lk(x, n.inputs[i])
        return n.outputs[0]
    def mr(x, a, b, c, d, clamp=True):   # map range
        n = N(nt, 'ShaderNodeMapRange'); n.clamp = clamp
        for i, v in zip(range(1, 5), (a, b, c, d)): n.inputs[i].default_value = v
        Lk(x, n.inputs[0]); return n.outputs[0]
    def mixc(f, a, b):
        n = N(nt, 'ShaderNodeMix', data_type='RGBA'); 
        if isinstance(f, (int, float)): n.inputs[0].default_value = f
        else: Lk(f, n.inputs[0])
        for i, c in ((6, a), (7, b)):
            if isinstance(c, tuple): n.inputs[i].default_value = c + (1,) if len(c) == 3 else c
            else: Lk(c, n.inputs[i])
        return n.outputs[2]
    def band(x, c, w):   # 1 dentro de |x-c|<w (suave)
        return mr(math_('ABSOLUTE', math_('SUBTRACT', x, c)), w * 0.6, w, 1, 0)
    X, Y, Z = sep.outputs[0], sep.outputs[1], sep.outputs[2]; NY, NZ = sepn.outputs[1], sepn.outputs[2]
    height = None; metal = None
    lowZ = mr(Z, 0.05, 0.6, 1, 0)                       # 1 abajo
    grime = math_('MULTIPLY', lowZ, mr(noise(4.0, 6, 0.6), 0.35, 0.65, 0.4, 1))
    wet = mr(Z, 0.12, 0.32, 1, 0)
    dirt = mr(noise(9.0, 8, 0.65), 0.58, 0.72, 0, 1)    # manchas sueltas
    if kind == 'suit':
        # pliegues: bandas horizontales deformadas (mangas y perneras) + arrugas finas de tejido engomado
        w = N(nt, 'ShaderNodeTexWave'); w.wave_type = 'BANDS'; w.bands_direction = 'Z'; w.inputs['Scale'].default_value = 4.2
        w.inputs['Distortion'].default_value = 3.2; w.inputs['Detail'].default_value = 2; w.inputs['Detail Scale'].default_value = 1.5
        Lk(geo.outputs['Position'], w.inputs['Vector'])
        folds = math_('MULTIPLY', w.outputs['Fac'], mr(noise(2.5, 3), 0.4, 0.75, 0.1, 0.8))
        fine = noise(55, 6, 0.6)
        side = band(NY, 0.0, 0.07)                      # costura lateral (contorno delante/detrás)
        waist = band(Z, 0.955, 0.008); shoulder = band(Z, 1.33, 0.006)
        cuffA = band(Z, 0.14, 0.012)
        seams = math_('MAXIMUM', math_('MAXIMUM', side, waist), math_('MAXIMUM', shoulder, cuffA))
        stitch = math_('MULTIPLY', seams, mr(math_('SINE', math_('MULTIPLY', math_('ADD', math_('ADD', X, Y), Z), 900)), 0.3, 0.9, 0, 1))
        front = mr(NY, -0.25, -0.5, 0, 1)
        zip_ = math_('MULTIPLY', math_('MULTIPLY', band(X, 0.0, 0.016), front), math_('MULTIPLY', mr(Z, 0.93, 0.95, 0, 1), mr(Z, 1.36, 1.38, 1, 0)))
        teeth = mr(math_('SINE', math_('MULTIPLY', Z, 1400)), -0.2, 0.4, 0, 1)
        belt = math_('MULTIPLY', mr(Z, 0.86, 0.87, 0, 1), mr(Z, 0.92, 0.93, 1, 0))
        buckle = math_('MULTIPLY', math_('MULTIPLY', belt, band(X, 0.0, 0.045)), front)
        weave = mr(math_('SINE', math_('MULTIPLY', math_('ADD', X, Y), 1100)), -1, 1, 0, 1)
        height = math_('ADD', math_('MULTIPLY', folds, 0.9), math_('MULTIPLY', fine, 0.12))
        height = math_('SUBTRACT', height, math_('MULTIPLY', seams, 0.5))
        height = math_('ADD', height, math_('MULTIPLY', stitch, 0.15))
        height = math_('ADD', height, math_('MULTIPLY', belt, math_('ADD', 0.5, math_('MULTIPLY', weave, 0.15))))
        height = math_('ADD', height, math_('MULTIPLY', zip_, math_('ADD', 0.35, math_('MULTIPLY', teeth, 0.25))))
        height = math_('ADD', height, math_('MULTIPLY', buckle, 0.35))
        yel = mixc(noise(6, 4), (0.80, 0.58, 0.035), (0.88, 0.67, 0.06))
        col = mixc(math_('MULTIPLY', folds, 0.22), yel, (0.58, 0.40, 0.02))           # fondo de pliegue algo más oscuro
        col = mixc(math_('MULTIPLY', stitch, 0.6), col, (0.55, 0.40, 0.04))
        col = mixc(belt, col, (0.07, 0.07, 0.075))
        col = mixc(zip_, col, (0.30, 0.30, 0.31))
        col = mixc(buckle, col, (0.45, 0.45, 0.46))
        col = mixc(math_('MULTIPLY', dirt, 0.35), col, (0.35, 0.30, 0.18))
        col = mixc(math_('MULTIPLY', grime, 0.85), col, (0.26, 0.21, 0.11))
        col = mixc(math_('MULTIPLY', wet, 0.45), col, (0.10, 0.08, 0.03))
        rough = mr(fine, 0, 1, 0.42, 0.58)
        rough = math_('ADD', rough, math_('MULTIPLY', grime, 0.25))
        rough = math_('SUBTRACT', rough, math_('MULTIPLY', wet, 0.3))
        rough = math_('ADD', math_('MULTIPLY', belt, 0.25), rough)
        metal = math_('MAXIMUM', math_('MULTIPLY', zip_, teeth), buckle)
    elif kind in ('rubber', 'visor'):
        fine = noise(90, 5, 0.55)
        crease = N(nt, 'ShaderNodeTexWave'); crease.wave_type = 'RINGS'; crease.inputs['Scale'].default_value = 14; crease.inputs['Distortion'].default_value = 6
        Lk(geo.outputs['Position'], crease.inputs['Vector'])
        sole = mr(Z, 0.035, 0.05, 1, 0)
        tread = mr(math_('SINE', math_('MULTIPLY', Y, 260)), 0.2, 0.6, 0, 1)
        if kind == 'visor':
            height = math_('MULTIPLY', fine, 0.02)
            col = mixc(0.0, (0.012, 0.014, 0.016), (0.012, 0.014, 0.016)); rough = mr(fine, 0, 1, 0.06, 0.12)
        else:
            height = math_('ADD', math_('MULTIPLY', fine, 0.25), math_('MULTIPLY', crease.outputs['Fac'], 0.35))
            height = math_('ADD', height, math_('MULTIPLY', math_('MULTIPLY', sole, tread), 0.8))
            col = mixc(noise(7, 3), (0.085, 0.088, 0.080), (0.110, 0.112, 0.102))
            col = mixc(math_('MULTIPLY', grime, 0.7), col, (0.17, 0.14, 0.09))
            col = mixc(math_('MULTIPLY', wet, 0.4), col, (0.02, 0.02, 0.02))
            rough = mr(fine, 0, 1, 0.50, 0.66)
            rough = math_('SUBTRACT', rough, math_('MULTIPLY', wet, 0.35))
        metal = math_('MULTIPLY', fine, 0.0)
    else:  # gris: piezas de plástico/goma del equipo
        fine = noise(70, 5)
        height = math_('MULTIPLY', fine, 0.2)
        col = mixc(fine, (0.10, 0.10, 0.11), (0.14, 0.14, 0.15))
        col = mixc(math_('MULTIPLY', grime, 0.7), col, (0.2, 0.17, 0.11))
        rough = mr(fine, 0, 1, 0.45, 0.6); metal = math_('MULTIPLY', fine, 0.0)
    bump = N(nt, 'ShaderNodeBump'); bump.inputs['Strength'].default_value = 1.0; bump.inputs['Distance'].default_value = (0.0065 if kind == 'suit' else 0.006)
    Lk(height, bump.inputs['Height'])
    Lk(col, bsdf.inputs['Base Color']); Lk(rough, bsdf.inputs['Roughness']); Lk(bump.outputs['Normal'], bsdf.inputs['Normal'])
    Lk(bsdf.outputs[0], out.inputs['Surface'])
    em = N(nt, 'ShaderNodeEmission'); em.name = 'MetalOut'; Lk(metal, em.inputs['Color'])
    return nt, out, bsdf, em

kinds = {}
for o in (body, head):
    for i, m in enumerate(o.data.materials):
        n = m.name
        k = 'suit' if n.startswith('Hazmat_Main') else ('visor' if (o == head and n.startswith('Black')) else ('rubber' if n.startswith('Black') else 'grey'))
        if (m.name, o.name) in kinds: continue
        if o == head:
            m2 = m.copy(); m2.name = ('Visor' if n.startswith('Black') else n.split('.')[0] + '_Head'); o.data.materials[i] = m2; m = m2
        kinds[(m.name, o.name)] = (m, build(m, k)); L('mat', o.name, m.name, k)

# ---- horneado
def bake_set(o, name, size):
    imgs = {}
    for key in ('albedo', 'normal', 'rough', 'ao', 'metal'):
        im = bpy.data.images.new(f'{name}_{key}', size, size, alpha=False, float_buffer=(key == 'normal'))
        if key != 'albedo': im.colorspace_settings.name = 'Non-Color'
        imgs[key] = im
    mats = [v for (mn, on), v in kinds.items() if on == o.name]
    def target(key):
        for m, (nt, out, bsdf, em) in mats:
            for n in nt.nodes:
                if n.type == 'TEX_IMAGE': nt.nodes.remove(n)
            t = nt.nodes.new('ShaderNodeTexImage'); t.image = imgs[key]; nt.nodes.active = t
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    sc.render.bake.margin = 12; sc.render.bake.use_selected_to_active = False
    for key, typ, samples in (('albedo', 'DIFFUSE', 4), ('normal', 'NORMAL', 4), ('rough', 'ROUGHNESS', 4), ('ao', 'AO', 96), ('metal', 'EMIT', 4)):
        target(key); sc.cycles.samples = samples
        if key == 'metal':
            for m, (nt, out, bsdf, em) in mats: nt.links.new(em.outputs[0], out.inputs['Surface'])
        kw = {}
        if typ == 'DIFFUSE': kw = dict(pass_filter={'COLOR'})
        bpy.ops.object.bake(type=typ, **kw)
        if key == 'metal':
            for m, (nt, out, bsdf, em) in mats: nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
        L('baked', name, key)
    def arr(im): return np.array(im.pixels[:], dtype=np.float32).reshape(size, size, 4)
    a = arr(imgs['albedo']); nrm = arr(imgs['normal']); r = arr(imgs['rough']); ao = arr(imgs['ao']); mt = arr(imgs['metal'])
    def save(px, fn, cs='sRGB'):
        im = bpy.data.images.new(fn, size, size, alpha=True); im.colorspace_settings.name = cs
        im.pixels[:] = px.ravel(); im.filepath_raw = os.path.join(OUT, fn + '.png'); im.file_format = 'PNG'; im.save()
    alb = a.copy(); alb[..., :3] = alb[..., :3] * (0.55 + 0.45 * ao[..., :1]); alb[..., 3] = 1
    save(alb, name + '_a')
    nrm[..., 3] = 1; save(nrm, name + '_n', 'Non-Color')
    mask = np.zeros_like(a); mask[..., 0] = mt[..., 0]; mask[..., 1] = ao[..., 0]; mask[..., 2] = 0; mask[..., 3] = 1 - r[..., 0]
    save(mask, name + '_m', 'Non-Color')
    L('saved', name, 'albedo mean', alb[..., :3].mean(axis=(0, 1)), 'smooth', mask[..., 3].mean(), 'ao', mask[..., 1].mean())
if not SKIP_BAKE:
    bake_set(body, 'HazmatBody', 2048)
    bake_set(head, 'HazmatHead', 1024)

# ---- vista previa (procedural) delante y detrás
try:
    if SKIP_BAKE: raise Exception('skip')
    w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.5, 0.52, 0.55, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.8
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sun.data.energy = 3
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
    sc.render.resolution_x = 900; sc.render.resolution_y = 1000; sc.cycles.samples = 32
    for nm, y, rz in (('front', -3.4, 0), ('back', 3.4, math.pi)):
        cam.location = (0, y, 1.05); cam.rotation_euler = (math.radians(90), 0, rz)
        sc.render.filepath = os.path.join(OUT, 'preview_' + nm + '.png'); bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(sun); bpy.data.objects.remove(cam)
    L('preview ok')
except Exception as e: L('preview fail', e)

# ---- manos a escala realista: se encogen la malla del guante y los huesos de los dedos hacia la muñeca
#      (después del despliegue UV y del horneado: UV y texturas del guante no cambian)
def shrink_hands(k):
    if abs(k - 1) < 1e-3: return
    mw = arm.matrix_world; inv = mw.inverted()
    bw = body.matrix_world; binv = bw.inverted()
    for side in ('L', 'R'):
        W = mw @ arm.data.bones['Middle1.' + side].head_local          # muñeca (cabeza común de los dedos)
        E = mw @ arm.data.bones['LowerArm.' + side].head_local          # codo
        ax = (W - E).normalized(); fl = (W - E).length
        n = 0
        for v in body.data.vertices:
            p = bw @ v.co; t = (p - W).dot(ax)
            if t < -0.30 * fl or (p - W - ax * t).length > 0.6: continue
            w = min(1.0, max(0.0, (t + 0.30 * fl) / (0.30 * fl))); w = w * w * (3 - 2 * w)
            f = 1 - (1 - k) * w
            v.co = binv @ (W + (p - W) * f); n += 1
        bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode='EDIT')
        for eb in arm.data.edit_bones:
            if eb.name.endswith('.' + side) and any(eb.name.startswith(x) for x in ('Thumb', 'Index', 'Middle', 'Pinky', 'Ring')):
                h = mw @ eb.head; tl = mw @ eb.tail
                eb.head = inv @ (W + (h - W) * k); eb.tail = inv @ (W + (tl - W) * k)
        bpy.ops.object.mode_set(mode='OBJECT')
        # posiciones animadas de los dedos (relativas al reposo) a la misma escala
        for a in bpy.data.actions:
            for fc in a.fcurves:
                if fc.data_path.endswith('.location') and ('.' + side + '"') in fc.data_path and any(x in fc.data_path for x in ('Thumb', 'Index', 'Middle', 'Pinky')):
                    for kp in fc.keyframe_points: kp.co.y *= k; kp.handle_left.y *= k; kp.handle_right.y *= k
        L('hand', side, 'verts', n, 'k', k, 'wrist', tuple(round(x, 3) for x in W))
shrink_hands(HAND_K)
if SKIP_BAKE:
    try:
        sc.cycles.samples = 16
        w = bpy.data.worlds.new('W2'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.6, 0.62, 0.65, 1)
        cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
        sc.render.resolution_x = 1200; sc.render.resolution_y = 800
        cam.location = (0, -5.2, 1.3); cam.rotation_euler = (math.radians(90), 0, 0)
        sc.render.filepath = os.path.join(OUT, 'preview_hands.png'); bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(cam)
    except Exception as e: L('preview hands fail', e)

# ---- exportación (materiales sin nodos de imagen; Unity asigna los suyos)
arm.data.pose_position = 'POSE'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'HazmatHD.fbx'), use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE', use_tspace=False, path_mode='STRIP')
L('exported'); log.close()
