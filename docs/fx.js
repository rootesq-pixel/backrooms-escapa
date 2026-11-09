/* BACKROOMS: ESCAPA — capa de efectos (vanilla JS + WebGL, sin dependencias)
   - Pasillo infinito de Nivel 0 en WebGL (raycasting por celdas) que recorres al hacer scroll
   - Post-procesado VHS/CRT: aberración cromática, tracking, grano, scanlines, distorsión de tubo
   - Fluorescentes que fallan, apagones, "entidades" que se dejan ver muy de vez en cuando
   - Transiciones "noclip" entre secciones, linterna en las zonas oscuras, títulos que se corrompen
   - Zumbido ambiental opcional generado con WebAudio (apagado por defecto)
   Respeta prefers-reduced-motion y tiene alternativa sin WebGL (el papel pintado CSS original). */
(function () {
  'use strict';

  var doc = document.documentElement;
  var body = document.body;
  var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var coarse = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
  var small = window.innerWidth < 760;
  var lowPower = coarse || small;

  function $(s, r) { return (r || document).querySelector(s); }
  function $$(s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); }
  function clamp(x, a, b) { return x < a ? a : x > b ? b : x; }
  function rand(a, b) { return a + Math.random() * (b - a); }
  function el(tag, cls, html) { var e = document.createElement(tag); if (cls) e.className = cls; if (html) e.innerHTML = html; return e; }

  /* ====================================================================
     Estado global compartido entre DOM, WebGL y audio
     ==================================================================== */
  var S = {
    power: 1,          // 1 = fluorescentes encendidos, 0 = apagón
    glitch: 0,         // pulso de glitch (decae solo)
    noclip: 0,         // 0..1 durante una transición noclip
    entity: { x: 0.5, z: 0, vis: 0, target: 0 },
    camZ: 0, targetZ: 0, lastScroll: window.scrollY, speed: 0
  };

  /* ====================================================================
     1. Pasillo WebGL
     ==================================================================== */
  var gl = null, canvas = null, webgl = false;
  var STEPS = lowPower ? 26 : 40;
  var OCT = lowPower ? 2 : 3;

  var VERT = 'attribute vec2 p;varying vec2 vUv;void main(){vUv=p*.5+.5;gl_Position=vec4(p,0.,1.);}';

  var SCENE = [
    'precision mediump float;',
    'varying vec2 vUv;',
    'uniform vec2 uRes;uniform float uTime;uniform vec3 uCam;uniform vec2 uLook;uniform float uPower;uniform vec3 uEnt;uniform float uNoclip;',
    '#define STEPS ' + STEPS,
    '#define OCT ' + OCT,
    '#define H 1.0',
    'float h21(vec2 p){p=fract(p*vec2(123.34,456.21));p+=dot(p,p+45.32);return fract(p.x*p.y);}',
    'float h11(float x){return fract(sin(x*127.1)*43758.5453);}',
    'float vn(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.-2.*f);',
    ' return mix(mix(h21(i),h21(i+vec2(1,0)),f.x),mix(h21(i+vec2(0,1)),h21(i+vec2(1,1)),f.x),f.y);}',
    'float fbm(vec2 p){float a=.5,s=0.;for(int i=0;i<OCT;i++){s+=a*vn(p);p*=2.03;a*=.5;}return s;}',
    // mapa: el pasillo x=0 siempre libre, laterales con huecos, cruces cada 7 celdas
    'float wall(vec2 c){',
    ' if(c.x==0.)return 0.;',
    ' float h=h21(c+vec2(17.,3.));',
    ' if(mod(c.y,7.)==3.)return 0.;',
    ' if(abs(c.x)==1.)return step(h,.62);',
    ' if(mod(c.x,4.)==2.&&mod(c.y,2.)==0.)return 1.;',
    ' return step(h,.26);}',
    'float lightOn(vec2 k){float h=h21(k*1.7+3.1);float on=1.;',
    ' if(h<.07)on=.0;',
    ' else if(h<.2)on=.25+.75*step(.45,vn(vec2(uTime*14.+h*91.,h*7.)));',
    ' return on*uPower;}',
    'float lightAt(vec3 p){vec2 k0=floor((p.xz-.5)/2.);float s=0.;',
    ' for(int i=0;i<2;i++)for(int j=0;j<2;j++){vec2 k=k0+vec2(float(i),float(j));vec2 lc=k*2.+.5;',
    '  vec3 d=vec3(lc.x,H,lc.y)-p;s+=lightOn(k)*1.35/(1.+dot(d,d)*2.4);}',
    ' return s;}',
    'vec3 wallpaper(vec2 uv,float t){float a=1./(1.+t*.35);',
    ' vec3 b=vec3(.72,.62,.31);',
    ' float st=abs(fract(uv.x*6.)-.5);b*=1.-.12*a*smoothstep(.36,.42,st);',
    ' vec2 f=fract(uv*12.)-.5;float d=abs(f.x)+abs(f.y);',
    ' b*=1.-.08*a*smoothstep(.40,.45,d)*(1.-smoothstep(.45,.5,d));',
    ' b+=.035*a*smoothstep(.13,.05,length(f));',
    ' b*=.86+.24*fbm(uv*vec2(3.,5.));',
    ' float stn=smoothstep(.5,.85,fbm(uv*vec2(1.3,2.)+7.))*(1.-smoothstep(.0,.55,uv.y));',
    ' stn+=.6*smoothstep(.62,.9,fbm(uv*vec2(2.1,1.)+3.))*smoothstep(.6,1.,uv.y);',
    ' b=mix(b,b*vec3(.5,.42,.22),clamp(stn,0.,1.)*.8);',
    ' if(uv.y<.055)b=vec3(.26,.2,.1);',
    ' return b;}',
    'vec3 carpet(vec2 p,float t){float a=1./(1.+t*.5);',
    ' vec3 c=vec3(.46,.39,.2)*(.85+.3*a*(vn(p*55.)-.5)*2.);',
    ' float d=smoothstep(.45,.8,fbm(p*.6+11.));return mix(c,c*vec3(.55,.5,.35),d);}',
    'vec3 ceiling(vec3 p,float t){float a=1./(1.+t*.4);',
    ' vec3 c=vec3(.8,.77,.62);vec2 g=fract(p.xz*2.);float l=min(min(g.x,1.-g.x),min(g.y,1.-g.y));',
    ' c*=1.-.35*a*smoothstep(.05,.02,l);',
    ' c*=.9+.15*fbm(p.xz*1.7);',
    ' vec2 k=floor((p.xz-.5)/2.+.5);vec2 q=p.xz-(k*2.+.5);',
    ' float pan=step(abs(q.x),.17)*step(abs(q.y),.3);',
    ' return mix(c,vec3(1.,.98,.86)*(.15+2.2*lightOn(k)),pan);}',
    'float sdBox(vec2 p,vec2 b){vec2 d=abs(p)-b;return length(max(d,0.))+min(max(d.x,d.y),0.);}',
    'float sdSeg(vec2 p,vec2 a,vec2 b){vec2 pa=p-a,ba=b-a;float h=clamp(dot(pa,ba)/dot(ba,ba),0.,1.);return length(pa-ba*h);}',
    'float ent(vec2 q){float d=length((q-vec2(0.,.86))*vec2(1.,.78))-.045;',
    ' d=min(d,sdBox(q-vec2(0.,.62),vec2(.042,.2)));',
    ' d=min(d,sdBox(q-vec2(-.024,.22),vec2(.011,.22)));d=min(d,sdBox(q-vec2(.024,.22),vec2(.011,.22)));',
    ' d=min(d,sdSeg(q,vec2(-.045,.79),vec2(-.075,.3))-.01);d=min(d,sdSeg(q,vec2(.045,.79),vec2(.08,.28))-.01);',
    ' return d;}',
    'void main(){',
    ' vec2 uv=(vUv-.5)*vec2(uRes.x/uRes.y,1.);',
    ' float r=length(uv);uv*=1.+uNoclip*(1.8*r*r);',
    ' vec3 ro=uCam;float yaw=uLook.x,pit=uLook.y;',
    ' vec3 fw=vec3(sin(yaw)*cos(pit),sin(pit),cos(yaw)*cos(pit));',
    ' vec3 rt=normalize(vec3(fw.z,0.,-fw.x));vec3 up=cross(fw,rt);',
    ' vec3 rd=normalize(fw*(1.15-uNoclip*.75)+rt*uv.x+up*uv.y);',
    ' vec2 p=ro.xz,d=rd.xz;vec2 cell=floor(p);vec2 sv=sign(d);vec2 td=abs(1./max(abs(d),1e-4));',
    ' vec2 tm=(sv*(cell-p)+sv*.5+.5)*td;float t=60.;float side=0.;bool hit=false;',
    ' for(int i=0;i<STEPS;i++){',
    '  if(tm.x<tm.y){t=tm.x;tm.x+=td.x;cell.x+=sv.x;side=0.;}else{t=tm.y;tm.y+=td.y;cell.y+=sv.y;side=1.;}',
    '  if(wall(cell)>.5){hit=true;break;}}',
    ' if(!hit)t=60.;',
    ' float y=ro.y+rd.y*t;vec3 col;float dist=t;',
    ' if(hit&&y>0.&&y<H){vec3 hp=ro+rd*t;float u=side<.5?hp.z:hp.x;',
    '  col=wallpaper(vec2(u,y),t)*(side<.5?1.:.84);',
    '  col*=.06+.1*uPower+lightAt(hp)*.9;',
    ' }else if(rd.y<0.){dist=-ro.y/rd.y;vec3 hp=ro+rd*dist;col=carpet(hp.xz,dist)*(.05+.08*uPower+lightAt(hp)*.75);',
    ' }else{dist=(H-ro.y)/max(rd.y,1e-4);vec3 hp=ro+rd*dist;col=ceiling(hp,dist);',
    '  vec2 k=floor((hp.xz-.5)/2.+.5);vec2 q=hp.xz-(k*2.+.5);',
    '  if(!(abs(q.x)<.17&&abs(q.y)<.3))col*=.08+.08*uPower+lightAt(hp)*.8;}',
    // entidad: silueta-billboard en el fondo del pasillo
    ' if(uEnt.z>.01){vec2 ep=uEnt.xy;vec2 n=normalize(ep-ro.xz);float dn=dot(d,n);',
    '  if(dn>.01){float te=dot(ep-ro.xz,n)/dn;',
    '   if(te>0.&&te<dist){vec3 q3=ro+rd*te;float lat=dot(q3.xz-ep,vec2(-n.y,n.x));vec2 q=vec2(lat,q3.y);',
    '    float sd=ent(q);float m=smoothstep(.006,-.002,sd)*uEnt.z;',
    '    col=mix(col,vec3(.012,.01,.008),m);',
    '    float eyes=smoothstep(.009,.003,min(length(q-vec2(-.016,.875)),length(q-vec2(.016,.875))));',
    '    col+=vec3(.9,.85,.7)*eyes*uEnt.z*(1.-uPower*.6);',
    '    dist=mix(dist,te,m);}}}',
    ' vec3 fog=vec3(.62,.54,.29)*(.06+.42*uPower);',
    ' col=mix(col,fog,1.-exp(-dist*.13));',
    ' gl_FragColor=vec4(col,1.);}'
  ].join('\n');

  var POST = [
    'precision mediump float;varying vec2 vUv;',
    'uniform sampler2D uTex;uniform vec2 uRes;uniform float uTime;uniform float uGlitch;uniform float uNoclip;',
    'float h11(float x){return fract(sin(x*127.1)*43758.5453);}',
    'float h12(vec2 p){return fract(sin(dot(p,vec2(12.9898,78.233)))*43758.5453);}',
    'void main(){vec2 c=vUv-.5;vec2 uv=.5+c*(1.+.09*dot(c,c));',
    ' float line=floor(uv.y*uRes.y);float fr=floor(uTime*24.);',
    ' float j=(h11(line*.37+fr)-.5)*(.0015+.03*uGlitch+.06*uNoclip);',
    ' j*=.35+step(.93-uGlitch*.5,h11(line*.11+fr*.7))*2.;',
    ' float by=1.-fract(uTime*.06);float band=smoothstep(.05,0.,abs(uv.y-by));',
    ' uv.x+=j+band*.008*sin(uv.y*240.+uTime*40.);',
    ' float blk=floor(uv.y*14.);uv.x+=(h11(blk+fr)-.5)*.3*uNoclip*uNoclip;',
    ' uv.y+=(h11(fr*1.3)-.5)*.03*uNoclip;',
    ' float ca=.0018+.012*dot(c,c)+.008*uGlitch+.025*uNoclip;',
    ' vec3 col=vec3(texture2D(uTex,uv+vec2(ca,0.)).r,texture2D(uTex,uv).g,texture2D(uTex,uv-vec2(ca,0.)).b);',
    ' float lum=dot(col,vec3(.299,.587,.114));col=mix(vec3(lum),col,.82)*vec3(1.04,1.,.9);',
    ' col+=(h12(uv*uRes+fract(uTime)*91.)-.5)*.075;',
    ' col+=band*.05*(h12(vec2(uv.x*uRes.x,fr))-.3);',
    ' col*=.9+.1*sin(uv.y*uRes.y*3.14159);',
    ' col*=smoothstep(1.05,.32,length(c)*1.25);',
    ' col=mix(col,vec3(1.,.97,.86),smoothstep(.72,1.,uNoclip)*.85);',
    ' if(uv.x<0.||uv.x>1.||uv.y<0.||uv.y>1.)col=vec3(0.);',
    ' gl_FragColor=vec4(col,1.);}'
  ].join('\n');

  var progScene, progPost, fbo, fboTex, buf, U = {}, scale = lowPower ? 0.55 : 0.5, maxScale = lowPower ? 0.7 : 0.62;
  var W = 0, Hh = 0;

  function compile(type, src) {
    var s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) { throw new Error(gl.getShaderInfoLog(s)); }
    return s;
  }
  function program(fs) {
    var p = gl.createProgram();
    gl.attachShader(p, compile(gl.VERTEX_SHADER, VERT));
    gl.attachShader(p, compile(gl.FRAGMENT_SHADER, fs));
    gl.bindAttribLocation(p, 0, 'p');
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
    return p;
  }
  function uni(p, names) { var o = {}; names.forEach(function (n) { o[n] = gl.getUniformLocation(p, n); }); return o; }

  function initGL() {
    if (new URLSearchParams(location.search).has('nogl')) return false;
    canvas = el('canvas', 'fx-corridor'); canvas.setAttribute('aria-hidden', 'true');
    try {
      gl = canvas.getContext('webgl', { antialias: false, alpha: false, depth: false, stencil: false, powerPreference: 'low-power', preserveDrawingBuffer: false });
    } catch (e) { gl = null; }
    if (!gl) return false;
    try {
      progScene = program(SCENE); progPost = program(POST);
    } catch (e) { gl = null; return false; }
    U.s = uni(progScene, ['uRes', 'uTime', 'uCam', 'uLook', 'uPower', 'uEnt', 'uNoclip']);
    U.p = uni(progPost, ['uTex', 'uRes', 'uTime', 'uGlitch', 'uNoclip']);
    buf = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
    fboTex = gl.createTexture(); fbo = gl.createFramebuffer();
    body.insertBefore(canvas, body.firstChild);
    canvas.addEventListener('webglcontextlost', function (e) { e.preventDefault(); webgl = false; doc.classList.remove('fx-webgl'); }, false);
    resize(true);
    window.addEventListener('resize', function () { resize(false); });
    return true;
  }

  function resize(force) {
    if (!gl) return;
    var cw = window.innerWidth, ch = window.innerHeight;
    var cap = lowPower ? 520 : 960;
    var w = Math.max(160, Math.min(cap, Math.round(cw * scale)));
    var h = Math.max(90, Math.round(w * ch / cw));
    if (!force && w === W && Math.abs(h - Hh) < 8) return;
    W = w; Hh = h; canvas.width = w; canvas.height = h;
    gl.bindTexture(gl.TEXTURE_2D, fboTex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, fboTex, 0);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  }

  function render(time) {
    if (!webgl) return;
    var t = time / 1000;
    var sway = reduce ? 0 : 1;
    var bob = Math.sin(t * 7.5) * 0.012 * clamp(S.speed * 0.6, 0, 1);
    var camX = 0.5 + Math.sin(t * 0.37) * 0.05 * sway;
    var camY = 0.55 + Math.sin(t * 0.9) * 0.006 * sway + bob;
    var yaw = (Math.sin(t * 0.13) * 0.2 + Math.sin(t * 0.41) * 0.05) * sway;
    var pitch = (Math.sin(t * 0.23) * 0.025 - 0.02) * sway;

    gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
    gl.viewport(0, 0, W, Hh);
    gl.useProgram(progScene);
    gl.uniform2f(U.s.uRes, W, Hh);
    gl.uniform1f(U.s.uTime, t);
    gl.uniform3f(U.s.uCam, camX, camY, S.camZ);
    gl.uniform2f(U.s.uLook, yaw, pitch);
    gl.uniform1f(U.s.uPower, S.power);
    gl.uniform3f(U.s.uEnt, S.entity.x, S.entity.z, S.entity.vis);
    gl.uniform1f(U.s.uNoclip, S.noclip);
    gl.drawArrays(gl.TRIANGLES, 0, 3);

    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.viewport(0, 0, W, Hh);
    gl.useProgram(progPost);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, fboTex);
    gl.uniform1i(U.p.uTex, 0);
    gl.uniform2f(U.p.uRes, W, Hh);
    gl.uniform1f(U.p.uTime, t);
    gl.uniform1f(U.p.uGlitch, S.glitch);
    gl.uniform1f(U.p.uNoclip, S.noclip);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }

  webgl = initGL();
  if (webgl) doc.classList.add('fx-webgl'); else doc.classList.add('fx-nogl');

  /* ====================================================================
     2. Capas DOM: OSD de videocámara, apagón, entidad, intro, botón de zumbido
     ==================================================================== */
  var blackout = el('div', 'fx-blackout'); blackout.setAttribute('aria-hidden', 'true');
  body.appendChild(blackout);

  var osd = el('div', 'fx-osd', '<div class="fx-osd-l"><span class="fx-play">&#9654; PLAY</span> <span class="fx-sp">SP</span> <span class="fx-ch" id="fx-ch">CH 00 · INICIO</span></div>' +
    '<div class="fx-osd-r"><span class="fx-batt" aria-hidden="true"><i></i><i></i><i></i></span> <span id="fx-date"></span></div>');
  osd.setAttribute('aria-hidden', 'true');
  body.appendChild(osd);

  var MESES = ['ENE', 'FEB', 'MAR', 'ABR', 'MAY', 'JUN', 'JUL', 'AGO', 'SEP', 'OCT', 'NOV', 'DIC'];
  var dateEl = $('#fx-date');
  function p2(n) { return n < 10 ? '0' + n : '' + n; }
  function tickDate() {
    var d = new Date();
    dateEl.textContent = p2(d.getDate()) + ' ' + MESES[d.getMonth()] + ' 1996  ' + p2(d.getHours()) + ':' + p2(d.getMinutes()) + ':' + p2(d.getSeconds());
  }
  tickDate(); setInterval(tickDate, 1000);

  var entityEl = el('div', 'fx-entity',
    '<svg viewBox="0 0 60 300" preserveAspectRatio="xMidYMax meet"><path d="M30 6c-8 0-12 7-12 16s5 15 8 17l-2 8c-7 3-11 7-12 15l-6 78c-1 6 1 10 4 10l3-2 6-64 2 36-2 60-3 108h7l7-100 2-30 2 30 7 100h7l-3-108-2-60 2-36 6 64 3 2c3 0 5-4 4-10l-6-78c-1-8-5-12-12-15l-2-8c3-2 8-8 8-17s-4-16-12-16z"/>' +
    '<circle class="fx-eye" cx="25.5" cy="22" r="1.4"/><circle class="fx-eye" cx="34.5" cy="22" r="1.4"/></svg>');
  entityEl.setAttribute('aria-hidden', 'true');
  body.appendChild(entityEl);

  /* Zumbido (WebAudio, generado; apagado por defecto) */
  var humBtn = el('button', 'fx-hum', '<span class="fx-hum-ico" aria-hidden="true"></span><span class="fx-hum-txt">Zumbido: OFF</span>');
  humBtn.type = 'button';
  humBtn.setAttribute('aria-pressed', 'false');
  humBtn.setAttribute('aria-label', 'Activar el zumbido ambiental de los fluorescentes');
  body.appendChild(humBtn);

  var A = null;
  function buildAudio() {
    var AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return null;
    var ctx = new AC();
    var master = ctx.createGain(); master.gain.value = 0; master.connect(ctx.destination);
    var power = ctx.createGain(); power.gain.value = 1; power.connect(master);
    var lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 340; lp.Q.value = 1.2; lp.connect(power);
    function osc(type, f, g, dest) { var o = ctx.createOscillator(); o.type = type; o.frequency.value = f; var gg = ctx.createGain(); gg.gain.value = g; o.connect(gg); gg.connect(dest); o.start(); return o; }
    osc('sawtooth', 50, 0.22, lp);
    osc('sine', 100, 0.32, power);
    osc('sine', 150, 0.09, power);
    var o4 = osc('sine', 100.6, 0.12, power); // batido lento
    // zumbido de reactancia: ruido filtrado modulado a 100 Hz
    var len = ctx.sampleRate * 2, nb = ctx.createBuffer(1, len, ctx.sampleRate), ch = nb.getChannelData(0);
    for (var i = 0; i < len; i++) ch[i] = Math.random() * 2 - 1;
    var ns = ctx.createBufferSource(); ns.buffer = nb; ns.loop = true;
    var bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = 2600; bp.Q.value = 0.8;
    var am = ctx.createGain(); am.gain.value = 0.0;
    var lfo = ctx.createOscillator(); lfo.type = 'square'; lfo.frequency.value = 100;
    var lfoG = ctx.createGain(); lfoG.gain.value = 0.018; lfo.connect(lfoG); lfoG.connect(am.gain);
    ns.connect(bp); bp.connect(am); am.connect(power); ns.start(); lfo.start();
    return { ctx: ctx, master: master, power: power, noise: nb, o4: o4 };
  }
  function spark() {
    if (!A || !humOn) return;
    var c = A.ctx, s = c.createBufferSource(); s.buffer = A.noise;
    var g = c.createGain(); var f = c.createBiquadFilter(); f.type = 'highpass'; f.frequency.value = 1800;
    var now = c.currentTime;
    g.gain.setValueAtTime(0.0001, now); g.gain.exponentialRampToValueAtTime(0.25, now + 0.005); g.gain.exponentialRampToValueAtTime(0.0001, now + 0.09);
    s.connect(f); f.connect(g); g.connect(A.master); s.start(now, Math.random()); s.stop(now + 0.12);
  }
  var humOn = false;
  humBtn.addEventListener('click', function () {
    humOn = !humOn;
    if (humOn && !A) A = buildAudio();
    if (!A) { humOn = false; return; }
    if (A.ctx.state === 'suspended') A.ctx.resume();
    var now = A.ctx.currentTime;
    A.master.gain.cancelScheduledValues(now);
    A.master.gain.setTargetAtTime(humOn ? 0.075 : 0, now, humOn ? 0.4 : 0.12);
    humBtn.setAttribute('aria-pressed', humOn ? 'true' : 'false');
    humBtn.setAttribute('aria-label', humOn ? 'Silenciar el zumbido ambiental' : 'Activar el zumbido ambiental de los fluorescentes');
    humBtn.querySelector('.fx-hum-txt').textContent = humOn ? 'Zumbido: ON' : 'Zumbido: OFF';
    humBtn.classList.toggle('is-on', humOn);
  });
  function audioPower(v) {
    if (A && humOn) A.power.gain.setTargetAtTime(0.05 + v * 0.95, A.ctx.currentTime, 0.015);
  }

  /* ====================================================================
     3. Secciones: canal del OSD, títulos corruptos, linterna
     ==================================================================== */
  var sections = $$('main > section[id]');
  var names = {};
  $$('#menu-principal a[href^="#"]').forEach(function (a) { names[a.getAttribute('href').slice(1)] = a.textContent.trim(); });
  names.inicio = 'Inicio';
  var chEl = $('#fx-ch');
  var currentSec = null;
  function setChannel(sec) {
    if (sec === currentSec) return;
    currentSec = sec;
    var i = sections.indexOf(sec);
    var n = (names[sec.id] || sec.id).toUpperCase();
    chEl.textContent = 'CH ' + p2(i) + ' · ' + n;
    if (!reduce) { S.glitch = Math.max(S.glitch, 0.8); osd.classList.remove('fx-osd-blip'); void osd.offsetWidth; osd.classList.add('fx-osd-blip'); }
  }
  if ('IntersectionObserver' in window) {
    var secIO = new IntersectionObserver(function (es) {
      es.forEach(function (e) { if (e.isIntersecting) setChannel(e.target); });
    }, { rootMargin: '-45% 0px -50% 0px' });
    sections.forEach(function (s) { secIO.observe(s); });
  }

  // Títulos que se "decodifican" desde una cinta corrupta
  var GLYPHS = '▓▒░█▌▐#@%&$?¿!¡01/\\<>ÑÁÉ_';
  function scramble(h) {
    var node = null;
    for (var i = 0; i < h.childNodes.length; i++) if (h.childNodes[i].nodeType === 3 && h.childNodes[i].nodeValue.trim()) { node = h.childNodes[i]; break; }
    if (!node || h.dataset.fxDone) return;
    h.dataset.fxDone = '1';
    var final = node.nodeValue, len = final.length, start = performance.now(), dur = 900;
    if (!h.hasAttribute('aria-label')) { h.setAttribute('aria-label', h.textContent.trim()); h.dataset.fxAria = '1'; }
    h.classList.add('fx-decoding');
    (function step(now) {
      var k = clamp((now - start) / dur, 0, 1), out = '';
      for (var j = 0; j < len; j++) {
        var c = final[j];
        if (c === ' ' || j / len < k * 1.15 - 0.15) out += c; else out += GLYPHS[(Math.random() * GLYPHS.length) | 0];
      }
      node.nodeValue = out;
      if (k < 1) requestAnimationFrame(step);
      else { node.nodeValue = final; h.classList.remove('fx-decoding'); if (h.dataset.fxAria) h.removeAttribute('aria-label'); }
    })(start);
  }
  var heads = $$('.section-head h2');
  heads.forEach(function (h) { h.classList.add('fx-head'); h.setAttribute('data-fx', h.textContent.trim()); });
  if (!reduce && 'IntersectionObserver' in window) {
    var hIO = new IntersectionObserver(function (es) {
      es.forEach(function (e) { if (e.isIntersecting) { scramble(e.target); hIO.unobserve(e.target); } });
    }, { threshold: 0.6 });
    heads.forEach(function (h) { hIO.observe(h); });
  }
  // pulsos de glitch aleatorios en el título visible
  function glitchPulse() {
    if (!reduce && !document.hidden) {
      var vis = heads.filter(function (h) { var r = h.getBoundingClientRect(); return r.bottom > 0 && r.top < window.innerHeight; });
      var h1 = $('#hero-title'); if (h1) { var r1 = h1.getBoundingClientRect(); if (r1.bottom > 0) vis.push(h1); }
      if (vis.length) {
        var h = vis[(Math.random() * vis.length) | 0];
        h.classList.remove('fx-glitch-pulse'); void h.offsetWidth; h.classList.add('fx-glitch-pulse');
        setTimeout(function () { h.classList.remove('fx-glitch-pulse'); }, 420);
      }
    }
    setTimeout(glitchPulse, rand(2600, 6500));
  }
  if (!reduce) setTimeout(glitchPulse, 2500);

  // Linterna en las secciones oscuras
  var darkSecs = $$('.section-dark');
  var pointer = { x: -9999, y: -9999, t: 0 };
  darkSecs.forEach(function (s) {
    var v = el('div', 'fx-veil'); v.setAttribute('aria-hidden', 'true'); s.appendChild(v); s.classList.add('fx-dark');
  });
  if (!reduce && darkSecs.length) {
    var onMove = function (e) {
      var pt = e.touches ? e.touches[0] : e;
      if (!pt) return;
      pointer.x = pt.clientX; pointer.y = pt.clientY; pointer.t = performance.now();
    };
    window.addEventListener('pointermove', onMove, { passive: true });
    window.addEventListener('touchstart', onMove, { passive: true });
    window.addEventListener('touchmove', onMove, { passive: true });
  }
  var beam = { x: window.innerWidth / 2, y: window.innerHeight / 2 };
  function updateFlashlight(time) {
    if (reduce) return;
    var idle = performance.now() - pointer.t > 2500;
    var tx, ty;
    if (idle) { // la linterna "busca" sola
      var t = time / 1000;
      tx = window.innerWidth * (0.5 + 0.32 * Math.sin(t * 0.45) + 0.06 * Math.sin(t * 1.7));
      ty = window.innerHeight * (0.48 + 0.22 * Math.sin(t * 0.31 + 1.2));
    } else { tx = pointer.x; ty = pointer.y; }
    beam.x += (tx - beam.x) * (idle ? 0.04 : 0.28);
    beam.y += (ty - beam.y) * (idle ? 0.04 : 0.28);
    var flick = S.power < 0.5 ? 0.55 + Math.random() * 0.2 : 1;
    for (var i = 0; i < darkSecs.length; i++) {
      var s = darkSecs[i], r = s.getBoundingClientRect();
      if (r.bottom < 0 || r.top > window.innerHeight) continue;
      s.style.setProperty('--fx-x', (beam.x - r.left).toFixed(1) + 'px');
      s.style.setProperty('--fx-y', (beam.y - r.top).toFixed(1) + 'px');
      s.style.setProperty('--fx-r', (lowPower ? 200 : 260) * flick + 'px');
    }
  }

  /* ====================================================================
     4. Fluorescentes que fallan y entidades
     ==================================================================== */
  var lastEntity = -1e9, failing = false;
  function setPower(v) {
    S.power = v;
    blackout.style.opacity = ((1 - v) * 0.62).toFixed(3);
    body.classList.toggle('fx-power-off', v < 0.4);
    audioPower(v);
  }
  function powerFailure() {
    if (reduce || document.hidden || S.noclip > 0) { schedule(); return; }
    failing = true;
    var long = Math.random() < 0.45;
    var seq = [[0.15, 50], [1, 70], [0.05, 90], [0.8, 40], [0.1, 60], [1, 220]];
    if (long) seq = seq.concat([[0.25, 40], [0, rand(700, 1500)], [0.6, 50], [0, 90], [1, 0]]);
    else seq.push([1, 0]);
    var sawEntity = long && performance.now() - lastEntity > 45000 && Math.random() < 0.55;
    var i = 0;
    (function next() {
      var st = seq[i++];
      if (!st) { failing = false; schedule(); if (sawEntity) showCorridorEntity(); return; }
      if (st[0] < 0.5 && Math.random() < 0.6) spark();
      setPower(st[0]);
      if (sawEntity && st[0] === 0 && st[1] > 400 && Math.random() < 0.5) showDomEntity(st[1]);
      setTimeout(next, st[1]);
    })();
  }
  function schedule() { setTimeout(powerFailure, rand(9000, 24000)); }
  function showCorridorEntity() {
    if (!webgl) return;
    lastEntity = performance.now();
    S.entity.x = 0.5 + rand(-0.15, 0.15);
    S.entity.z = S.camZ + rand(5.5, 8.5);
    S.entity.target = 1;
    setTimeout(function () { S.entity.target = 0; }, rand(900, 1600));
  }
  function showDomEntity(ms) {
    lastEntity = performance.now();
    var left = Math.random() < 0.5;
    entityEl.style.left = left ? rand(2, 12) + 'vw' : 'auto';
    entityEl.style.right = left ? 'auto' : rand(2, 12) + 'vw';
    entityEl.classList.add('is-on');
    setTimeout(function () { entityEl.classList.remove('is-on'); }, Math.min(ms, 900));
  }
  if (!reduce) setTimeout(powerFailure, rand(6000, 11000));

  /* ====================================================================
     5. Transición "noclip" al navegar por el menú
     ==================================================================== */
  var noclipBusy = false;
  function animateNoclip(from, to, ms, done) {
    var st = performance.now();
    (function f(now) {
      var k = clamp((now - st) / ms, 0, 1);
      S.noclip = from + (to - from) * (k * k * (3 - 2 * k));
      if (k < 1) requestAnimationFrame(f); else if (done) done();
    })(st);
  }
  function noclipTo(target, hash) {
    if (noclipBusy) return;
    noclipBusy = true;
    body.classList.add('fx-noclipping');
    spark();
    animateNoclip(0, 1, 420, function () {
      target.scrollIntoView({ behavior: 'instant', block: 'start' });
      if (history.pushState) history.pushState(null, '', hash); else location.hash = hash;
      S.camZ = S.targetZ = window.scrollY / 260 + driftZ;
      if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
      target.focus({ preventScroll: true });
      body.classList.add('fx-noclip-out');
      animateNoclip(1, 0, 520, function () {
        body.classList.remove('fx-noclipping', 'fx-noclip-out');
        noclipBusy = false;
      });
    });
  }
  if (!reduce) {
    document.addEventListener('click', function (e) {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      var a = e.target.closest && e.target.closest('a[href^="#"]');
      if (!a || a.classList.contains('skip-link')) return;
      var hash = a.getAttribute('href');
      if (hash.length < 2) return;
      var target = document.getElementById(hash.slice(1));
      if (!target) return;
      e.preventDefault();
      noclipTo(target, hash);
    });
  }

  /* ====================================================================
     6. Intro de cinta (una vez por sesión)
     ==================================================================== */
  var seen = false;
  try { seen = sessionStorage.getItem('fx-intro') === '1'; sessionStorage.setItem('fx-intro', '1'); } catch (e) {}
  if (!reduce && !seen && !new URLSearchParams(location.search).has('nointro')) {
    var intro = el('div', 'fx-intro', '<div class="fx-intro-blue"><span>&#9654; PLAY</span><span class="fx-intro-sp">SP</span>' +
      '<p class="fx-intro-title">CINTA 01<br><small>NIVEL 0 · ' + (new Date().getFullYear() - 30) + '</small></p><p class="fx-intro-track">TRACKING ▮▮▮▮▮▯▯▯</p></div>' +
      '<canvas class="fx-intro-static" width="120" height="70"></canvas>');
    intro.setAttribute('aria-hidden', 'true');
    body.appendChild(intro);
    var ic = intro.querySelector('canvas'), ictx = ic.getContext('2d'), iimg = ictx.createImageData(120, 70);
    var introStart = performance.now();
    (function st(now) {
      for (var i = 0; i < iimg.data.length; i += 4) { var v = (Math.random() * 255) | 0; iimg.data[i] = iimg.data[i + 1] = iimg.data[i + 2] = v; iimg.data[i + 3] = 255; }
      ictx.putImageData(iimg, 0, 0);
      if (now - introStart < 2200) requestAnimationFrame(st);
    })(introStart);
    setTimeout(function () { intro.classList.add('is-static'); S.glitch = 1; }, 1050);
    setTimeout(function () { intro.classList.add('is-out'); }, 1450);
    setTimeout(function () { intro.remove(); }, 2300);
  }

  /* ====================================================================
     7. Bucle principal
     ==================================================================== */
  var heroEl = $('#inicio');
  var driftZ = 0, lastT = performance.now(), frames = 0, acc = 0, lastFrame = 0;
  function loop(now) {
    requestAnimationFrame(loop);
    if (document.hidden) { lastT = now; return; }
    var dt = Math.min(0.1, (now - lastT) / 1000); lastT = now;

    // scroll -> avance por el pasillo
    var y = window.scrollY;
    var dy = y - S.lastScroll; S.lastScroll = y;
    S.speed += (Math.abs(dy) / Math.max(dt, 0.001) / 900 - S.speed) * 0.1;
    if (Math.abs(dy) > 400 && !noclipBusy) S.glitch = Math.max(S.glitch, 0.6);
    driftZ += dt * 0.07;
    S.targetZ = y / 260 + driftZ;
    S.camZ += (S.targetZ - S.camZ) * Math.min(1, dt * 4);
    S.glitch = Math.max(0, S.glitch - dt * 1.6);
    S.entity.vis += (S.entity.target - S.entity.vis) * Math.min(1, dt * (S.entity.target ? 3 : 9));

    updateFlashlight(now);

    if (!webgl) return;
    var hh = heroEl ? heroEl.offsetHeight : window.innerHeight;
    var past = y / Math.max(1, hh);
    var op = S.noclip > 0 ? 1 : clamp(1 - past * 0.85, 0.2, 1);
    canvas.style.opacity = op.toFixed(3);

    // tope de fps: 60 en la portada, 30 detrás del contenido o en móvil
    var cap = (lowPower || op < 0.5) && S.noclip === 0 ? 33 : 16;
    if (now - lastFrame < cap - 1) return;
    var ft = now - lastFrame; lastFrame = now;
    render(now);

    // calidad adaptativa
    frames++; acc += ft;
    if (frames >= 45) {
      var avg = acc / frames; frames = 0; acc = 0;
      if (avg > cap * 1.6 && scale > 0.16) { scale *= 0.8; resize(true); }
      else if (avg < cap * 1.05 && scale < maxScale) { scale = Math.min(maxScale, scale * 1.08); resize(false); }
    }
  }

  if (new URLSearchParams(location.search).has('fxdebug')) {
    window.__backroomsFX = { S: S, entity: showCorridorEntity, ghost: showDomEntity, power: setPower, fail: powerFailure };
  }

  if (reduce) {
    // un único fotograma estático, sin animación
    if (webgl) {
      S.camZ = 0.4; render(4000);
      var rmOp = function () { var hh = heroEl ? heroEl.offsetHeight : window.innerHeight; canvas.style.opacity = clamp(1 - (window.scrollY / Math.max(1, hh)) * 0.85, 0.2, 1).toFixed(3); };
      rmOp(); window.addEventListener('scroll', rmOp, { passive: true });
    }
  } else {
    if (webgl) render(performance.now());
    requestAnimationFrame(loop);
  }
})();
