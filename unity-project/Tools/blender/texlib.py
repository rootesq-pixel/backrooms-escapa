"""
Mini-DSL para construir materiales procedurales por nodos de Blender (ruido, Voronoi, rampas de color, desgaste...)
y hornear sus canales con Cycles (emisión, 1 muestra) a PNG de 16 bits.

Todo el ruido usa texturas 4D evaluadas sobre un TORO (cos/sin de u y v), por lo que las texturas resultantes son
perfectamente "tileables" (sin costuras) sin trucos de fusión. Las estructuras (rayas, azulejos, remaches...) se
definen con fract() sobre (u, v), que también es periódico.

Autor: proyecto BackroomsEscape (trabajo propio, sin assets externos).
"""
import bpy, math

TWO_PI = 2.0 * math.pi


class X(object):
    """Envoltorio de un socket de salida (float)."""
    def __init__(self, g, sock):
        self.g = g
        self.sock = sock

    def __add__(self, o): return self.g.math('ADD', self, o)
    def __radd__(self, o): return self.g.math('ADD', o, self)
    def __sub__(self, o): return self.g.math('SUBTRACT', self, o)
    def __rsub__(self, o): return self.g.math('SUBTRACT', o, self)
    def __mul__(self, o): return self.g.math('MULTIPLY', self, o)
    def __rmul__(self, o): return self.g.math('MULTIPLY', o, self)
    def __truediv__(self, o): return self.g.math('DIVIDE', self, o)
    def __rtruediv__(self, o): return self.g.math('DIVIDE', o, self)
    def __neg__(self): return self.g.math('MULTIPLY', self, -1.0)
    def __lt__(self, o): return self.g.math('LESS_THAN', self, o)
    def __gt__(self, o): return self.g.math('GREATER_THAN', self, o)


class Col(object):
    """Color como tres X (r, g, b); operaciones por canal."""
    def __init__(self, r, g, b):
        self.c = (r, g, b)

    @staticmethod
    def of(g, v):
        if isinstance(v, Col): return v
        return Col(*[g.val(x) if not isinstance(x, X) else x for x in v])


class G(object):
    def __init__(self, name, W=1.0, Hh=1.0):
        self.mat = bpy.data.materials.new(name)
        self.mat.use_nodes = True
        self.nt = self.mat.node_tree
        for n in list(self.nt.nodes): self.nt.nodes.remove(n)
        self.nodes = self.nt.nodes
        self.links = self.nt.links
        self.W, self.H = W, Hh            # tamaño en metros de la baldosa
        self._tor = {}
        tc = self.nodes.new('ShaderNodeTexCoord')
        sep = self.nodes.new('ShaderNodeSeparateXYZ')
        self.links.new(tc.outputs['UV'], sep.inputs[0])
        self.u = X(self, sep.outputs[0])
        self.v = X(self, sep.outputs[1])
        self.n_cnt = 0

    # ---------- utilidades de enlace
    def _set(self, sock, v):
        if isinstance(v, X): self.links.new(v.sock, sock)
        else: sock.default_value = v

    def val(self, v):
        n = self.nodes.new('ShaderNodeValue')
        n.outputs[0].default_value = float(v)
        return X(self, n.outputs[0])

    def math(self, op, a, b=None, c=None, clamp=False):
        n = self.nodes.new('ShaderNodeMath')
        n.operation = op
        n.use_clamp = clamp
        self._set(n.inputs[0], a)
        if b is not None: self._set(n.inputs[1], b)
        if c is not None: self._set(n.inputs[2], c)
        return X(self, n.outputs[0])

    # ---------- funciones escalares
    def abs(self, a): return self.math('ABSOLUTE', a)
    def sin(self, a): return self.math('SINE', a)
    def cos(self, a): return self.math('COSINE', a)
    def fract(self, a): return self.math('FRACT', a)
    def floor(self, a): return self.math('FLOOR', a)
    def sqrt(self, a): return self.math('SQRT', a)
    def pow(self, a, b): return self.math('POWER', a, b)
    def minimum(self, a, b): return self.math('MINIMUM', a, b)
    def maximum(self, a, b): return self.math('MAXIMUM', a, b)
    def clamp(self, a): return self.math('ADD', a, 0.0, clamp=True)

    def smooth(self, e0, e1, x):
        n = self.nodes.new('ShaderNodeMapRange')
        n.data_type = 'FLOAT'
        n.interpolation_type = 'SMOOTHSTEP'
        n.clamp = True
        self._set(n.inputs[0], x)
        self._set(n.inputs[1], e0)
        self._set(n.inputs[2], e1)
        n.inputs[3].default_value = 0.0
        n.inputs[4].default_value = 1.0
        return X(self, n.outputs[0])

    def lerp(self, a, b, t):
        if isinstance(a, Col) or isinstance(b, Col):
            a = Col.of(self, a) if not isinstance(a, Col) else a
            b = Col.of(self, b) if not isinstance(b, Col) else b
            return Col(*[self.lerp(a.c[i], b.c[i], t) for i in range(3)])
        return self.math('MULTIPLY_ADD', self.math('SUBTRACT', b, a), t, a)

    def contrast(self, x, k, mid=0.5):
        return self.clamp((x - mid) * k + 0.5)

    def band(self, x, lo, hi, soft=0.01):
        """1 dentro de [lo, hi], 0 fuera, con bordes suaves."""
        return self.smooth(lo - soft, lo + soft, x) * (1.0 - self.smooth(hi - soft, hi + soft, x))

    def tri(self, x):
        """Onda triangular periodica 0..1..0 con periodo 1."""
        return 1.0 - self.abs(self.fract(x) * 2.0 - 1.0)

    # ---------- colores
    def rgb(self, r, g, b): return Col(*[self.val(x) if not isinstance(x, X) else x for x in (r, g, b)])
    def cmul(self, c, k): return Col(*[ch * k for ch in c.c])
    def cadd(self, c, d):
        if isinstance(d, Col): return Col(*[c.c[i] + d.c[i] for i in range(3)])
        return Col(*[ch + d for ch in c.c])
    def cmix(self, a, b, t): return self.lerp(Col.of(self, a), Col.of(self, b), t)

    def combine(self, c):
        n = self.nodes.new('ShaderNodeCombineColor')
        n.mode = 'RGB'
        for i in range(3): self.links.new(c.c[i].sock, n.inputs[i])
        return n.outputs[0]

    def combine_vals(self, a, b, cc):
        return self.combine(Col(*[x if isinstance(x, X) else self.val(x) for x in (a, b, cc)]))

    def ramp(self, x, stops):
        """Rampa de color (pos, (r,g,b)) -> Col."""
        n = self.nodes.new('ShaderNodeValToRGB')
        el = n.color_ramp.elements
        while len(el) < len(stops): el.new(0.5)
        for i, (p, col) in enumerate(stops):
            el[i].position = p
            el[i].color = (col[0], col[1], col[2], 1.0)
        self._set(n.inputs[0], x)
        sp = self.nodes.new('ShaderNodeSeparateColor')
        self.links.new(n.outputs[0], sp.inputs[0])
        return Col(*[X(self, sp.outputs[i]) for i in range(3)])

    def rampf(self, x, stops):
        n = self.nodes.new('ShaderNodeValToRGB')
        el = n.color_ramp.elements
        while len(el) < len(stops): el.new(0.5)
        for i, (p, val) in enumerate(stops):
            el[i].position = p
            el[i].color = (val, val, val, 1.0)
        self._set(n.inputs[0], x)
        sp = self.nodes.new('ShaderNodeSeparateColor')
        self.links.new(n.outputs[0], sp.inputs[0])
        return X(self, sp.outputs[0])

    # ---------- coordenadas sobre el toro (ruido periodico)
    def torus(self, fu, fv):
        key = (round(fu, 4), round(fv, 4))
        if key in self._tor: return self._tor[key]
        th = self.u * TWO_PI
        ph = self.v * TWO_PI
        ru, rv = fu / TWO_PI, fv / TWO_PI
        cx = self.nodes.new('ShaderNodeCombineXYZ')
        self._set(cx.inputs[0], self.cos(th) * ru)
        self._set(cx.inputs[1], self.sin(th) * ru)
        self._set(cx.inputs[2], self.cos(ph) * rv)
        w = self.sin(ph) * rv
        self._tor[key] = (cx.outputs[0], w)
        return self._tor[key]

    def _offset_vec(self, vec, seed):
        if seed == 0: return vec
        o = self.nodes.new('ShaderNodeVectorMath')
        o.operation = 'ADD'
        self.links.new(vec, o.inputs[0])
        o.inputs[1].default_value = (seed * 13.37, seed * 7.91, seed * 3.17)
        return o.outputs[0]

    def noise(self, fu, fv=None, detail=4.0, rough=0.5, lac=2.0, seed=0, kind='FBM', distort=0.0):
        """Ruido 4D periodico con fu x fv celdas por baldosa. Devuelve ~[0,1] centrado en 0.5."""
        if fv is None: fv = fu
        vec, w = self.torus(fu, fv)
        n = self.nodes.new('ShaderNodeTexNoise')
        n.noise_dimensions = '4D'
        n.noise_type = kind
        n.normalize = True
        self.links.new(self._offset_vec(vec, seed), n.inputs['Vector'])
        self._set(n.inputs['W'], w + seed * 5.7 if seed else w)
        n.inputs['Scale'].default_value = 1.0
        n.inputs['Detail'].default_value = detail
        n.inputs['Roughness'].default_value = rough
        n.inputs['Lacunarity'].default_value = lac
        n.inputs['Distortion'].default_value = distort
        return X(self, n.outputs['Fac'])

    def voronoi(self, fu, fv=None, feature='F1', seed=0, rand=1.0, smoothness=1.0):
        """Voronoi 4D periodico. Devuelve la distancia (F1 / DISTANCE_TO_EDGE) y el color aleatorio por celda."""
        if fv is None: fv = fu
        vec, w = self.torus(fu, fv)
        n = self.nodes.new('ShaderNodeTexVoronoi')
        n.voronoi_dimensions = '4D'
        n.feature = feature
        n.distance = 'EUCLIDEAN'
        self.links.new(self._offset_vec(vec, seed), n.inputs['Vector'])
        self._set(n.inputs['W'], w + seed * 5.7 if seed else w)
        n.inputs['Scale'].default_value = 1.0
        n.inputs['Randomness'].default_value = rand
        if feature == 'SMOOTH_F1': n.inputs['Smoothness'].default_value = smoothness
        dist = X(self, n.outputs['Distance'])
        if feature == 'DISTANCE_TO_EDGE': return dist, None
        sp = self.nodes.new('ShaderNodeSeparateColor')
        self.links.new(n.outputs['Color'], sp.inputs[0])
        cell = X(self, sp.outputs[0])
        return dist, cell

    def cellrand(self, cx, cy, seed=0):
        """Aleatorio 0..1 por celda entera (cx, cy)."""
        c = self.nodes.new('ShaderNodeCombineXYZ')
        self._set(c.inputs[0], cx + seed * 17.0)
        self._set(c.inputs[1], cy + seed * 31.0)
        n = self.nodes.new('ShaderNodeTexWhiteNoise')
        n.noise_dimensions = '3D'
        self.links.new(c.outputs[0], n.inputs['Vector'])
        return X(self, n.outputs['Value'])


# ---------------------------------------------------------------------------------------------
# Render (horneado por emision)
# ---------------------------------------------------------------------------------------------
def setup_scene(res):
    sc = bpy.context.scene
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 1
    sc.cycles.use_adaptive_sampling = False
    sc.cycles.use_denoising = False
    sc.cycles.filter_width = 0.01
    sc.cycles.max_bounces = 0
    sc.render.resolution_x = res
    sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.threads_mode = 'AUTO'
    sc.view_settings.view_transform = 'Raw'
    sc.view_settings.look = 'None'
    sc.display_settings.display_device = 'sRGB'
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGB'
    sc.render.image_settings.color_depth = '16'
    w = bpy.data.worlds.new('W') if not sc.world else sc.world
    sc.world = w
    w.use_nodes = False
    w.color = (0, 0, 0)
    bpy.ops.mesh.primitive_plane_add(size=1.0, location=(0.5, 0.5, 0.0))
    plane = bpy.context.active_object
    # UV (0..1) exacta
    me = plane.data
    uvl = me.uv_layers.active or me.uv_layers.new(name='UVMap')
    for loop in me.loops:
        co = me.vertices[loop.vertex_index].co
        uvl.data[loop.index].uv = (co.x + 0.5, co.y + 0.5)
    cam = bpy.data.cameras.new('C')
    cam.type = 'ORTHO'
    cam.ortho_scale = 1.0
    cobj = bpy.data.objects.new('C', cam)
    bpy.context.collection.objects.link(cobj)
    cobj.location = (0.5, 0.5, 2.0)
    cobj.rotation_euler = (0, 0, 0)
    sc.camera = cobj
    return plane


def render_channel(g, plane, color_out, path):
    """Renderiza el socket de color indicado como emision pura a 'path' (PNG 16 bits, sin transformacion de vista)."""
    nt = g.nt
    for n in [n for n in nt.nodes if n.type in ('OUTPUT_MATERIAL', 'EMISSION')]: nt.nodes.remove(n)
    em = nt.nodes.new('ShaderNodeEmission')
    em.inputs['Strength'].default_value = 1.0
    nt.links.new(color_out, em.inputs['Color'])
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    plane.data.materials.clear()
    plane.data.materials.append(g.mat)
    sc = bpy.context.scene
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
