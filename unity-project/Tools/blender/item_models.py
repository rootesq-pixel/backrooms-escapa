# v7.0.2: modelos 3D de los objetos recogibles (pila, agua de almendras, barra luminosa, cinta VHS, nota, linterna).
# Se modela cada objeto en Blender (medidas reales, biseles, textos), se hornea alta→baja con Cycles
# (albedo, metal/suavidad, normal, emisión) a una textura de 512 px y se exporta la malla baja a FBX.
#   blender -b -P Tools/blender/item_models.py -- <carpeta_salida>
# Todo es obra propia (modelado procedural): dominio público / CC0.
import bpy, bmesh, sys, math, os
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '/workspace/item-models'
ONLY = argv[1].split(',') if len(argv) > 1 else None
RES = 512
os.makedirs(OUT, exist_ok=True)
FONT_B = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
FONT_H = '/usr/share/fonts/truetype/sand-box/google/Courier Prime/CourierPrime-Bold.ttf'
if not os.path.exists(FONT_H): FONT_H = '/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf'

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 16
    sc.render.bake.margin = 6
    return sc

def mat(name, col, rough=0.5, metal=0.0, emit=None, estr=0.0, tex=None, tex_scale=60.0, amt=0.35):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*col, 1)
    b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    if emit is not None:
        b.inputs['Emission Color'].default_value = (*emit, 1); b.inputs['Emission Strength'].default_value = estr
    if tex in ('grime', 'paper', 'brushed'):
        n = nt.nodes.new('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = tex_scale; n.inputs['Detail'].default_value = 10
        if tex == 'brushed':
            mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (1, 1, 40)
            tc = nt.nodes.new('ShaderNodeTexCoord'); nt.links.new(tc.outputs['Object'], mp.inputs['Vector']); nt.links.new(mp.outputs['Vector'], n.inputs['Vector'])
        r = nt.nodes.new('ShaderNodeValToRGB'); r.color_ramp.elements[0].position = 0.42; r.color_ramp.elements[1].position = 0.78
        mx = nt.nodes.new('ShaderNodeMixRGB'); mx.blend_type = 'MULTIPLY'; mx.inputs[0].default_value = amt
        mx.inputs[1].default_value = (*col, 1)
        nt.links.new(n.outputs['Fac'], r.inputs['Fac']); nt.links.new(r.outputs['Color'], mx.inputs[2])
        nt.links.new(mx.outputs['Color'], b.inputs['Base Color'])
        mr = nt.nodes.new('ShaderNodeMapRange'); mr.inputs['To Min'].default_value = rough * 0.75; mr.inputs['To Max'].default_value = min(1, rough * 1.35)
        nt.links.new(n.outputs['Fac'], mr.inputs['Value']); nt.links.new(mr.outputs['Result'], b.inputs['Roughness'])
    return m

def setm(o, m, smooth=True):
    o.data.materials.clear(); o.data.materials.append(m)
    if smooth:
        for p in o.data.polygons: p.use_smooth = True
    return o

def cyl(r, d, loc, m, verts=32, rot=(0, 0, 0), smooth=True, bevel=0.0):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d, location=loc, rotation=rot)
    o = bpy.context.object; setm(o, m, smooth)
    if bevel > 0: md = o.modifiers.new('B', 'BEVEL'); md.width = bevel; md.segments = 2; md.limit_method = 'ANGLE'
    return o

def cone(r1, r2, d, loc, m, verts=32, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=d, location=loc, rotation=rot)
    o = bpy.context.object; setm(o, m); return o

def box(sx, sy, sz, loc, m, bevel=0.0, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.object; o.scale = (sx, sy, sz)
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        md = o.modifiers.new('B', 'BEVEL'); md.width = bevel; md.segments = 2
    setm(o, m, smooth=False)
    if bevel > 0:
        o.data.shade_smooth() if hasattr(o.data, 'shade_smooth') else None
    return o

def text(s, size, loc, m, rot=(math.radians(90), 0, 0), extrude=0.0003, font=None):
    bpy.ops.object.text_add(location=loc, rotation=rot)
    o = bpy.context.object; o.data.body = s; o.data.size = size; o.data.extrude = extrude
    o.data.align_x = 'CENTER'; o.data.align_y = 'CENTER'
    if font and os.path.exists(font): o.data.font = bpy.data.fonts.load(font)
    o.data.materials.append(m)
    bpy.ops.object.convert(target='MESH')
    o = bpy.context.object
    o['hi_only'] = 1
    return o

def apply_all(objs):
    for o in objs:
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        for md in list(o.modifiers):
            try: bpy.ops.object.modifier_apply(modifier=md.name)
            except Exception as e: print('mod', e)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        if o.type == 'MESH' and not o.get('hi_only'):
            try: bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
            except Exception as e: print('smooth', e)

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.object; o.name = name
    return o

def channel_src(nt, b, which):
    """socket que da el valor del canal pedido (para conectarlo a la emisión al hornear)"""
    def sock(inp):
        i = b.inputs[inp]
        if i.is_linked: return i.links[0].from_socket
        n = nt.nodes.new('ShaderNodeRGB' if inp in ('Base Color', 'Emission Color') else 'ShaderNodeValue')
        if inp in ('Base Color', 'Emission Color'): n.outputs[0].default_value = i.default_value
        else: n.outputs[0].default_value = i.default_value
        return n.outputs[0]
    if which == 'albedo': return sock('Base Color')
    if which == 'emit':
        mul = nt.nodes.new('ShaderNodeMixRGB'); mul.blend_type = 'MULTIPLY'; mul.inputs[0].default_value = 1.0
        nt.links.new(sock('Emission Color'), mul.inputs[1])
        s = b.inputs['Emission Strength'].default_value
        mul.inputs[2].default_value = (min(1, s / 8.0),) * 3 + (1,)
        return mul.outputs[0]
    if which == 'ms':
        cc = nt.nodes.new('ShaderNodeCombineColor')
        nt.links.new(sock('Metallic'), cc.inputs[0])
        inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
        nt.links.new(sock('Roughness'), inv.inputs[1]); nt.links.new(inv.outputs[0], cc.inputs[1])
        return cc.outputs[0]

def bake_item(name, parts, uv_margin=0.02):
    sc = bpy.context.scene
    apply_all(parts)
    lows = [o for o in parts if not o.get('hi_only')]
    # copia baja: unión de las piezas (sin textos)
    bpy.ops.object.select_all(action='DESELECT')
    dup = []
    for o in lows:
        c = o.copy(); c.data = o.data.copy(); sc.collection.objects.link(c); dup.append(c)
    low = join(dup, name)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.00005)
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=uv_margin)
    bpy.ops.object.mode_set(mode='OBJECT')
    low.data.materials.clear()
    bm = bpy.data.materials.new('Bake_' + name); bm.use_nodes = True; low.data.materials.append(bm)
    imgnode = bm.node_tree.nodes.new('ShaderNodeTexImage'); bm.node_tree.nodes.active = imgnode
    for p in low.data.polygons: p.use_smooth = True
    try:
        bpy.ops.object.select_all(action='DESELECT'); low.select_set(True); bpy.context.view_layer.objects.active = low
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    except Exception as e:
        print('smooth', e)
    hi = parts
    rb = sc.render.bake
    rb.use_selected_to_active = True; rb.cage_extrusion = 0.003; rb.max_ray_distance = 0.01
    tri = sum(len(p.vertices) - 2 for p in low.data.polygons)
    dims = low.dimensions
    for ch in ('albedo', 'ms', 'emit', 'normal'):
        img = bpy.data.images.new(name + '_' + ch, RES, RES, alpha=False, float_buffer=False)
        if ch == 'normal': img.colorspace_settings.name = 'Non-Color'
        if ch == 'ms': img.colorspace_settings.name = 'Non-Color'
        imgnode.image = img
        saved = []
        if ch != 'normal':
            seen = set()
            for o in hi:
                for m in o.data.materials:
                    if m.name in seen: continue
                    seen.add(m.name)
                    nt = m.node_tree; b = nt.nodes.get('Principled BSDF')
                    src = channel_src(nt, b, ch)
                    em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(src, em.inputs[0]); em.inputs[1].default_value = 1.0
                    out = nt.nodes['Material Output']
                    old = out.inputs['Surface'].links[0].from_socket if out.inputs['Surface'].is_linked else None
                    nt.links.new(em.outputs[0], out.inputs['Surface'])
                    saved.append((nt, out, old))
        bpy.ops.object.select_all(action='DESELECT')
        for o in hi: o.select_set(True)
        low.select_set(True); bpy.context.view_layer.objects.active = low
        bpy.ops.object.bake(type='NORMAL' if ch == 'normal' else 'EMIT', normal_space='TANGENT')
        img.filepath_raw = os.path.join(OUT, name + '_' + ch + '.png'); img.file_format = 'PNG'; img.save()
        for nt, out, old in saved:
            if old is not None: nt.links.new(old, out.inputs['Surface'])
    # exportar solo la malla baja
    for o in hi: o.hide_set(True); o.hide_render = True
    bpy.ops.object.select_all(action='DESELECT'); low.select_set(True); bpy.context.view_layer.objects.active = low
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + '.fbx'), use_selection=True, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, object_types={'MESH'},
                             mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False, path_mode='STRIP', embed_textures=False,
                             axis_forward='-Z', axis_up='Y')
    render_icon(name, hi, low)
    print('[items] %s tris %d dims %.3f %.3f %.3f' % (name, tri, dims.x, dims.y, dims.z))

def render_icon(name, hi, low):
    """icono del inventario a partir del mismo modelo (alto detalle), con Cycles y fondo transparente"""
    sc = bpy.context.scene
    # se renderiza la malla baja con las texturas horneadas: exactamente lo que se ve en el juego
    for o in hi: o.hide_set(True); o.hide_render = True
    low.hide_set(False); low.hide_render = False
    m = low.data.materials[0]; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    def img(ch, cs):
        n = nt.nodes.new('ShaderNodeTexImage'); n.image = bpy.data.images[name + '_' + ch]; n.image.colorspace_settings.name = cs; return n
    a = img('albedo', 'sRGB'); nt.links.new(a.outputs['Color'], b.inputs['Base Color'])
    ms = img('ms', 'Non-Color'); sep = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(ms.outputs['Color'], sep.inputs[0])
    nt.links.new(sep.outputs[0], b.inputs['Metallic'])
    inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
    nt.links.new(sep.outputs[1], inv.inputs[1]); nt.links.new(inv.outputs[0], b.inputs['Roughness'])
    nm = img('normal', 'Non-Color'); nmap = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(nm.outputs['Color'], nmap.inputs['Color']); nt.links.new(nmap.outputs['Normal'], b.inputs['Normal'])
    em = img('emit', 'sRGB'); nt.links.new(em.outputs['Color'], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = 4.0
    sc.cycles.samples = 64
    try: sc.cycles.use_denoising = True
    except Exception: pass
    sc.render.film_transparent = True; sc.render.resolution_x = sc.render.resolution_y = 512
    sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA'
    vts = [i.identifier for i in sc.view_settings.bl_rna.properties['view_transform'].enum_items]
    sc.view_settings.view_transform = 'Filmic' if 'Filmic' in vts else 'AgX'
    w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes['Background']; bg.inputs[0].default_value = (0.05, 0.05, 0.055, 1); bg.inputs[1].default_value = 0.6
    mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3)
    for o in [low]:
        for v in o.data.vertices:
            p = o.matrix_world @ v.co; mn = Vector(map(min, mn, p)); mx = Vector(map(max, mx, p))
    t = (mn + mx) / 2; size = (mx - mn).length
    az, el = math.radians(-32), math.radians(26)
    dist = size * 2.1
    pos = t + Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el))) * dist
    bpy.ops.object.camera_add(location=pos); cam = bpy.context.object; sc.camera = cam
    cam.rotation_euler = (t - pos).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens_unit = 'FOV'; cam.data.angle = math.radians(30)
    def area(loc, energy, sz, col=(1, 1, 1)):
        bpy.ops.object.light_add(type='AREA', location=loc); l = bpy.context.object
        l.data.energy = energy; l.data.size = sz; l.data.color = col
        l.rotation_euler = (t - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    area(t + Vector((-1.2, -1.4, 1.6)) * dist, 60 * dist * dist, 1.2 * dist, (1.0, 0.95, 0.85))
    area(t + Vector((1.6, 0.8, 0.6)) * dist, 35 * dist * dist, 0.8 * dist, (0.75, 0.85, 1.0))
    area(t + Vector((0.3, -1.6, -0.4)) * dist, 10 * dist * dist, 1.5 * dist)
    sc.render.filepath = os.path.join(OUT, name + '_icon.png')
    bpy.ops.render.render(write_still=True)

def want(n): return ONLY is None or n in ONLY

# Convenciones: Z arriba, origen en la base (apoyado en la superficie), medidas en metros (escala real; el juego la amplía un poco)

# ---------------- Pila (D, 61,5 × 34 mm, tumbada) ----------------
if want('Battery'):
    reset()
    wrap = mat('wrap', (0.025, 0.025, 0.028), 0.38, 0.25, tex='grime')
    copper = mat('copper', (0.78, 0.42, 0.14), 0.32, 1.0, tex='brushed', amt=0.2)
    nick = mat('nick', (0.82, 0.82, 0.8), 0.22, 1.0)
    white = mat('white', (0.88, 0.86, 0.8), 0.5)
    green = mat('green', (0.12, 0.55, 0.22), 0.45)
    R = (0, math.radians(90), 0); r = 0.017; L = 0.0615; zc = r
    p = [cyl(r, L * 0.62, (-L * 0.19, 0, zc), wrap, 40, R, bevel=0.0006),
         cyl(r, L * 0.36, (L * 0.30, 0, zc), copper, 40, R, bevel=0.0006),
         cyl(r * 0.92, 0.0016, (-L / 2 - 0.0002, 0, zc), nick, 40, R),
         cyl(r * 0.92, 0.0016, (L / 2 + 0.0002, 0, zc), nick, 40, R),
         cyl(0.0048, 0.0024, (L / 2 + 0.0014, 0, zc), nick, 24, R, bevel=0.0005)]
    p.append(cyl(r + 0.0002, 0.004, (-0.002, 0, zc), green, 40, R))
    for ang, s, sz in ((0, 'ALCALINA', 0.0042), (180, '1.5V  D', 0.0048)):
        t = text(s, sz, (-L * 0.2, 0, zc), white, rot=(0, 0, 0), font=FONT_B)
        t.location = (-L * 0.2, 0, zc + r + 0.0003)
        if ang: t.rotation_euler = (math.radians(ang), 0, 0); t.location = (-L * 0.2, 0, zc - r - 0.0003)
        p.append(t)
    t2 = text('+', 0.008, (L * 0.30, 0, zc + r + 0.0003), white, rot=(0, 0, 0), font=FONT_B); p.append(t2)
    bake_item('Battery', p)

# ---------------- Agua de almendras (botella de 0,5 L, de pie) ----------------
if want('AlmondWater'):
    reset()
    plastic = mat('plastic', (0.80, 0.84, 0.86), 0.12, 0.0)
    milk = mat('milk', (0.93, 0.89, 0.80), 0.35, tex='grime', amt=0.12)
    cap = mat('cap', (0.62, 0.42, 0.22), 0.5, tex='grime')
    label = mat('label', (0.86, 0.79, 0.62), 0.6, tex='grime')
    ink = mat('ink', (0.30, 0.17, 0.08), 0.6)
    band = mat('band', (0.55, 0.32, 0.12), 0.5)
    p = [cyl(0.033, 0.14, (0, 0, 0.07), milk, 40, bevel=0.003),
         cone(0.033, 0.015, 0.05, (0, 0, 0.165), plastic, 40),
         cyl(0.0155, 0.012, (0, 0, 0.196), plastic, 32),
         cyl(0.0165, 0.02, (0, 0, 0.212), cap, 32, bevel=0.001),
         cyl(0.0335, 0.064, (0, 0, 0.082), label, 40),
         cyl(0.0338, 0.006, (0, 0, 0.052), band, 40), cyl(0.0338, 0.006, (0, 0, 0.112), band, 40)]
    for s, z in (('AGUA DE', 0.091), ('ALMENDRAS', 0.077)):
        t = text(s, 0.0105, (0, -0.0340, z), ink, font=FONT_H)
        md = t.modifiers.new('C', 'SIMPLE_DEFORM'); md.deform_method = 'BEND'; md.deform_axis = 'Z'; md.angle = math.radians(32)
        p.append(t)
    bake_item('AlmondWater', p)

# ---------------- Barra luminosa (15 cm, tumbada) ----------------
if want('Glowstick'):
    reset()
    glow = mat('glow', (0.45, 1.0, 0.35), 0.2, 0.0, emit=(0.35, 1.0, 0.25), estr=8)
    capm = mat('capm', (0.08, 0.30, 0.06), 0.45, tex='grime')
    R = (0, math.radians(90), 0); r = 0.0078
    p = [cyl(r, 0.14, (0, 0, r), glow, 24, R), cyl(r + 0.0008, 0.012, (0.075, 0, r), capm, 24, R, bevel=0.0008),
         cyl(r + 0.0008, 0.01, (-0.074, 0, r), capm, 24, R, bevel=0.0008)]
    bpy.ops.mesh.primitive_torus_add(major_radius=0.0068, minor_radius=0.0016, location=(0.088, 0, r), rotation=(math.radians(90), 0, 0), major_segments=20, minor_segments=8)
    o = bpy.context.object; setm(o, capm); p.append(o)
    bake_item('Glowstick', p)

# ---------------- Cinta VHS (187 × 103 × 25 mm, plana) ----------------
if want('VhsTape'):
    reset()
    shell = mat('shell', (0.022, 0.022, 0.025), 0.42, 0.0, tex='grime')
    win = mat('win', (0.10, 0.10, 0.11), 0.08)
    reel = mat('reel', (0.86, 0.86, 0.86), 0.4)
    tape = mat('tape', (0.07, 0.045, 0.03), 0.3, 0.3)
    lab = mat('lab', (0.90, 0.87, 0.78), 0.65, tex='grime', amt=0.25)
    pen = mat('pen', (0.12, 0.10, 0.38), 0.6)
    H = 0.025
    p = [box(0.187, 0.103, H, (0, 0, H / 2), shell, 0.0015)]
    p.append(box(0.085, 0.03, 0.002, (0, 0.016, H + 0.0004), win, 0.0008))
    for x, rr in ((-0.03, 0.013), (0.03, 0.009)):
        p.append(cyl(rr, 0.0012, (x, 0.016, H + 0.0012), tape, 32))
        p.append(cyl(0.007, 0.0016, (x, 0.016, H + 0.0016), reel, 6))
    p.append(box(0.125, 0.036, 0.001, (0, -0.026, H + 0.0004), lab, 0.0003))
    t = text('NO ABRIR - 1994', 0.0105, (0, -0.026, H + 0.0011), pen, rot=(0, 0, 0), font=FONT_H); p.append(t)
    bake_item('VhsTape', p)

# ---------------- Nota (hoja A5 doblada, manuscrita) ----------------
if want('Note'):
    reset()
    paper = mat('paper', (0.86, 0.81, 0.66), 0.75, tex='grime', tex_scale=18, amt=0.3)
    ink = mat('ink', (0.12, 0.10, 0.16), 0.6)
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=12, y_subdivisions=16, size=1, location=(0, 0, 0))
    o = bpy.context.object; o.scale = (0.148, 0.21, 1); bpy.ops.object.transform_apply(scale=True)
    for v in o.data.vertices:   # doblez y esquina levantada
        v.co.z = 0.0012 + 0.006 * (1 - abs(v.co.y) / 0.105) ** 6 + max(0, v.co.x + v.co.y - 0.14) * 0.15
    sol = o.modifiers.new('S', 'SOLIDIFY'); sol.thickness = 0.0006
    setm(o, paper)
    p = [o]
    lines = ['dia 31 sin salida', 'las luces zumban', 'no confies en', 'quien se parece', 'a ti', '    - M.']
    for i, s in enumerate(lines):
        t = text(s, 0.0115, (-0.0, 0.07 - i * 0.022, 0.0035 + 0.006 * (1 - abs(0.07 - i * 0.022) / 0.105) ** 6), ink, rot=(0, 0, math.radians(-1.5 + i * 0.6)), font=FONT_H, extrude=0.0001)
        p.append(t)
    bake_item('Note', p)

# ---------------- Linterna (tumbada) ----------------
if want('Flashlight'):
    reset()
    alu = mat('alu', (0.09, 0.09, 0.095), 0.34, 1.0, tex='brushed', amt=0.3)
    knurl = mat('knurl', (0.025, 0.025, 0.027), 0.6, 0.9, tex='grime')
    rubber = mat('rubber', (0.014, 0.014, 0.014), 0.85)
    lens = mat('lens', (0.95, 0.93, 0.85), 0.05, emit=(1, 0.93, 0.75), estr=2)
    yel = mat('yel', (0.85, 0.62, 0.05), 0.45)
    R = (0, math.radians(90), 0); zc = 0.052
    p = [cyl(0.032, 0.30, (0, 0, zc), alu, 32, R, bevel=0.001)]
    for i in range(8): p.append(cyl(0.0335, 0.006, (-0.10 + i * 0.016, 0, zc), knurl, 32, R))
    p.append(cone(0.034, 0.05, 0.07, (0.185, 0, zc), alu, 32, R))
    p.append(cyl(0.052, 0.035, (0.235, 0, zc), alu, 32, R, bevel=0.0015))
    p.append(cyl(0.044, 0.004, (0.2535, 0, zc), lens, 32, R))
    p.append(cyl(0.035, 0.03, (-0.165, 0, zc), rubber, 32, R, bevel=0.002))
    p.append(box(0.022, 0.016, 0.012, (0.09, 0, zc + 0.033), rubber, 0.003))
    p.append(box(0.012, 0.008, 0.006, (0.09, 0, zc + 0.040), yel, 0.002))
    bake_item('Flashlight', p)

# ---------------- v7.0.4: llaves (5 variantes parecidas: solo una abre la salida de cada nivel) ----------------
def torus(R, r, loc, m, sz=1.0, rot=(0, 0, 0), seg=(32, 12)):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, location=loc, rotation=rot, major_segments=seg[0], minor_segments=seg[1])
    o = bpy.context.object; o.scale = (1, 1, sz); bpy.ops.object.transform_apply(scale=True); setm(o, m); return o

def key_blade(p, m, x0, L, w, t, z, teeth, groove_m=None):
    """hoja plana tipo Yale a lo largo de X desde x0, con dientes en el canto +Y"""
    p.append(box(L, w, t, (x0 + L / 2, 0, z), m, 0.0004))
    n = len(teeth); step = (L * 0.8) / n
    for i, hgt in enumerate(teeth):
        p.append(box(step * 0.75, hgt, t, (x0 + L * 0.12 + step * (i + 0.5), w / 2 + hgt / 2 - 0.0002, z), m, 0.0003, rot=(0, 0, math.radians(8 if i % 2 else -8))))
    p.append(box(L * 0.1, w * 0.7, t, (x0 + L - L * 0.04, -w * 0.12, z), m, 0.0004, rot=(0, 0, math.radians(-25))))   # punta biselada
    if groove_m is not None:
        p.append(box(L * 0.82, w * 0.16, 0.0004, (x0 + L * 0.5, -w * 0.12, z + t / 2 + 0.0001), groove_m))

KEYS = [
    # nombre, color, rugosidad, tipo de cabeza, dientes, texto
    ('Key0', (0.80, 0.60, 0.24), 0.30, 'round', [0.0022, 0.0034, 0.0016, 0.0030, 0.0024], 'TESA'),
    ('Key1', (0.66, 0.67, 0.69), 0.28, 'square', [0.0030, 0.0018, 0.0032, 0.0020, 0.0028, 0.0016], 'B-12'),
    ('Key2', (0.20, 0.16, 0.13), 0.70, 'ring', None, ''),
    ('Key3', (0.62, 0.40, 0.20), 0.38, 'clover', [0.0018, 0.0028, 0.0034, 0.0020, 0.0026], 'ACE'),
    ('Key4', (0.78, 0.78, 0.76), 0.22, 'oval', [0.0032, 0.0022, 0.0018, 0.0034, 0.0020], '4'),
]
for kname, kcol, krough, kbow, kteeth, ktxt in KEYS:
    if not want(kname): continue
    reset()
    km = mat(kname + '_m', kcol, krough, 1.0, tex='grime' if kname == 'Key2' else 'brushed', amt=0.35 if kname == 'Key2' else 0.18)
    dark = mat(kname + '_d', tuple(c * 0.35 for c in kcol), 0.6, 1.0)
    T = 0.0022; z = T / 2
    p = []
    if kbow == 'round':
        p.append(torus(0.0105, 0.0055, (0, 0, z + 0.0006), km, sz=0.30))
        x0 = 0.014
    elif kbow == 'square':
        p.append(box(0.022, 0.024, T * 1.1, (0, 0, z), km, 0.003)); p.append(cyl(0.0035, T * 1.3, (-0.004, 0, z), dark, 20))
        x0 = 0.010
    elif kbow == 'clover':
        for a in (90, 210, 330):
            ax, ay = math.cos(math.radians(a)) * 0.0062, math.sin(math.radians(a)) * 0.0062
            p.append(torus(0.0058, 0.0030, (ax - 0.002, ay, z + 0.0005), km, sz=0.33))
        x0 = 0.010
    elif kbow == 'oval':
        o = torus(0.0105, 0.0050, (0, 0, z + 0.0005), km, sz=0.30); o.scale = (1.25, 0.9, 1); bpy.ops.object.transform_apply(scale=True); p.append(o)
        # etiqueta de llavero con número
        tag = mat('tag', (0.86, 0.80, 0.55), 0.7, tex='grime', amt=0.3)
        ringm = mat('ring', (0.7, 0.7, 0.7), 0.25, 1.0)
        p.append(torus(0.0085, 0.0007, (-0.016, 0, 0.0009), ringm, sz=1.0))
        p.append(box(0.022, 0.014, 0.0008, (-0.034, 0.004, 0.0004), tag, 0.0006, rot=(0, 0, math.radians(14))))
        tt = text('4', 0.008, (-0.034, 0.004, 0.0010), mat('inkk', (0.1, 0.1, 0.3), 0.6), rot=(0, 0, math.radians(14)), font=FONT_H); p.append(tt)
        x0 = 0.013
    else:   # 'ring': llave antigua de paletón (hierro)
        p.append(torus(0.011, 0.0022, (0, 0, 0.0022), km, sz=1.0, seg=(36, 12)))
        p.append(cyl(0.0024, 0.058, (0.040, 0, 0.0024), km, 20, (0, math.radians(90), 0)))
        p.append(cyl(0.0034, 0.004, (0.013, 0, 0.0024), km, 20, (0, math.radians(90), 0)))
        p.append(box(0.010, 0.012, 0.0036, (0.064, 0.0068, 0.0018), km, 0.0005))
        for wx in (0.061, 0.067): p.append(box(0.0016, 0.006, 0.0038, (wx, 0.0098, 0.0019), dark))
        bake_item(kname, p)
        continue
    key_blade(p, km, x0, 0.040 if kname != 'Key1' else 0.046, 0.0085, T, z, kteeth, groove_m=dark)
    if ktxt and kbow != 'oval':
        p.append(text(ktxt, 0.0042, (0.0 if kbow != 'square' else 0.003, 0, T + 0.0009), dark, rot=(0, 0, 0), font=FONT_B))
    bake_item(kname, p)
print('[items] OK')
