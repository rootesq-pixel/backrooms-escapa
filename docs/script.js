/* BACKROOMS: ESCAPA — script de la web (vanilla JS, sin dependencias) */
(function () {
  'use strict';

  /* ===== Cambia aquí el título del juego (se aplica a toda la página) ===== */
  var GAME_TITLE = 'BACKROOMS: ESCAPA';

  var doc = document.documentElement;
  doc.classList.remove('no-js');
  doc.classList.add('js');

  var reduceMotionMQ = window.matchMedia('(prefers-reduced-motion: reduce)');
  var reduceMotion = reduceMotionMQ.matches;
  if (reduceMotionMQ.addEventListener) {
    reduceMotionMQ.addEventListener('change', function (e) { reduceMotion = e.matches; });
  }

  /* ---------- Título ---------- */
  function applyTitle() {
    var nodes = document.querySelectorAll('[data-title]');
    for (var i = 0; i < nodes.length; i++) {
      nodes[i].textContent = GAME_TITLE;
      if (nodes[i].hasAttribute('data-text')) nodes[i].setAttribute('data-text', GAME_TITLE);
    }
    document.title = document.title.replace(/^[^—]+/, GAME_TITLE + ' ');
    var metas = document.querySelectorAll('meta[property="og:site_name"], meta[property="og:title"], meta[name="twitter:title"]');
    for (var j = 0; j < metas.length; j++) {
      metas[j].setAttribute('content', metas[j].getAttribute('content').replace('BACKROOMS: ESCAPA', GAME_TITLE));
    }
  }
  applyTitle();

  /* ---------- Menú móvil ---------- */
  var toggle = document.querySelector('.nav-toggle');
  var nav = document.getElementById('menu-principal');
  if (toggle && nav) {
    var setOpen = function (open) {
      nav.classList.toggle('is-open', open);
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
      toggle.querySelector('.sr-only').textContent = open ? 'Cerrar menú' : 'Abrir menú';
    };
    toggle.addEventListener('click', function () { setOpen(!nav.classList.contains('is-open')); });
    nav.addEventListener('click', function (e) { if (e.target.closest('a')) setOpen(false); });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && nav.classList.contains('is-open')) { setOpen(false); toggle.focus(); }
    });
  }

  /* ---------- REC: timecode en directo (HH:MM:SS:FF a 25 fps) ---------- */
  var recEl = document.getElementById('rec-time');
  var start = performance.now();
  function pad(n) { return n < 10 ? '0' + n : '' + n; }
  function tickRec() {
    var t = (performance.now() - start) / 1000;
    var h = Math.floor(t / 3600), m = Math.floor(t / 60) % 60, s = Math.floor(t) % 60, f = Math.floor((t % 1) * 25);
    if (recEl) recEl.textContent = pad(h) + ':' + pad(m) + ':' + pad(s) + ':' + pad(f);
  }
  setInterval(tickRec, 40);
  tickRec();

  /* ---------- Ruido VHS en canvas ---------- */
  function noiseCanvas(canvas, fps, alpha) {
    if (!canvas || !canvas.getContext) return;
    var ctx = canvas.getContext('2d');
    var w, h, img, data;
    function resize() {
      if (canvas.hasAttribute('data-fit')) {
        canvas.width = Math.max(64, Math.round(window.innerWidth / 2));
        canvas.height = Math.max(64, Math.round(window.innerHeight / 2));
      }
      w = canvas.width; h = canvas.height;
      img = ctx.createImageData(w, h); data = img.data;
    }
    resize();
    window.addEventListener('resize', function () { resize(); draw(); });
    function draw() {
      for (var i = 0; i < data.length; i += 4) {
        var v = (Math.random() * 255) | 0;
        data[i] = v; data[i + 1] = v; data[i + 2] = v; data[i + 3] = alpha;
      }
      // banda horizontal más brillante ocasional
      if (Math.random() < 0.25) {
        var y = (Math.random() * h) | 0, bh = 1 + ((Math.random() * 3) | 0);
        for (var yy = y; yy < Math.min(h, y + bh); yy++) {
          for (var x = 0; x < w; x++) { var k = (yy * w + x) * 4; data[k] = data[k + 1] = data[k + 2] = 230; }
        }
      }
      ctx.putImageData(img, 0, 0);
    }
    draw();
    var last = 0, interval = 1000 / fps;
    function loop(ts) {
      if (!reduceMotion && !document.hidden && ts - last > interval) { draw(); last = ts; }
      requestAnimationFrame(loop);
    }
    requestAnimationFrame(loop);
  }
  noiseCanvas(document.getElementById('vhs-noise'), 14, 255);
  var statics = document.querySelectorAll('.static-canvas');
  for (var si = 0; si < statics.length; si++) noiseCanvas(statics[si], 18, 255);

  /* ---------- Glitch de "tracking" al hacer scroll ---------- */
  var lastY = window.scrollY, lastT = performance.now(), cooling = false;
  window.addEventListener('scroll', function () {
    if (reduceMotion || cooling) { lastY = window.scrollY; return; }
    var now = performance.now();
    var speed = Math.abs(window.scrollY - lastY) / Math.max(1, now - lastT); // px/ms
    lastY = window.scrollY; lastT = now;
    if (speed > 1.2) {
      cooling = true;
      document.body.classList.remove('is-tracking');
      void document.body.offsetWidth; // reinicia la animación
      document.body.classList.add('is-tracking');
      setTimeout(function () { document.body.classList.remove('is-tracking'); }, 460);
      setTimeout(function () { cooling = false; }, 1400);
    }
  }, { passive: true });

  /* ---------- Aparición de secciones ---------- */
  var reveals = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window && !reduceMotion) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (en.isIntersecting) { en.target.classList.add('is-visible'); io.unobserve(en.target); }
      });
    }, { threshold: 0.08, rootMargin: '0px 0px -40px 0px' });
    reveals.forEach(function (el) { io.observe(el); });
  } else {
    reveals.forEach(function (el) { el.classList.add('is-visible'); });
  }

  /* ---------- Galería con lightbox ---------- */
  var dlg = document.getElementById('lightbox');
  var dlgImg = document.getElementById('lightbox-img');
  var dlgCap = document.getElementById('lightbox-caption');
  var lastBtn = null;
  document.querySelectorAll('.gallery-btn').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var thumb = btn.querySelector('img');
      if (!dlg || typeof dlg.showModal !== 'function') { window.open(btn.getAttribute('data-full'), '_blank'); return; }
      lastBtn = btn;
      dlgImg.src = btn.getAttribute('data-full');
      dlgImg.alt = thumb ? thumb.alt : '';
      dlgCap.textContent = btn.getAttribute('data-caption') || '';
      dlg.showModal();
    });
  });
  if (dlg) {
    dlg.addEventListener('click', function (e) { if (e.target === dlg) dlg.close(); });
    dlg.addEventListener('close', function () { if (lastBtn) lastBtn.focus(); });
  }

  /* ---------- Botones de descarga deshabilitados (aún sin enlace) ---------- */
  document.querySelectorAll('.btn-disabled').forEach(function (b) {
    b.addEventListener('click', function (e) { e.preventDefault(); });
  });
})();
