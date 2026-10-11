'use strict';
// Prueba de integración local: un anfitrión y 2 clientes a través del relay, por loopback.
const net = require('net');
const assert = require('assert');
const crypto = require('crypto');
const { createRelay, PROTO } = require('./server');

let passed = 0;
function ok(cond, msg) { assert.ok(cond, 'FALLO: ' + msg); passed++; console.log('PASS ' + msg); }

// utilidades -----------------------------------------------------------------
function connect(port) { return new Promise((res, rej) => { const s = net.connect(port, '127.0.0.1', () => res(s)); s.once('error', rej); }); }
function readLine(sock, timeoutMs = 4000) {
  return new Promise((res, rej) => {
    let buf = Buffer.alloc(0);
    const t = setTimeout(() => { cleanup(); rej(new Error('timeout leyendo línea')); }, timeoutMs);
    const onData = (d) => { buf = Buffer.concat([buf, d]); const i = buf.indexOf(10); if (i >= 0) { cleanup(); sock.pause(); const rest = buf.slice(i + 1); if (rest.length) sock.unshift(rest); res(buf.slice(0, i).toString()); } };
    const onEnd = () => { cleanup(); rej(new Error('cerrada')); };
    function cleanup() { clearTimeout(t); sock.removeListener('data', onData); sock.removeListener('close', onEnd); }
    sock.on('data', onData); sock.on('close', onEnd); sock.resume();
  });
}
function readBytes(sock, n, timeoutMs = 8000) {
  return new Promise((res, rej) => {
    const chunks = []; let got = 0;
    const t = setTimeout(() => { cleanup(); rej(new Error('timeout leyendo bytes (' + got + '/' + n + ')')); }, timeoutMs);
    const onData = (d) => { chunks.push(d); got += d.length; if (got >= n) { cleanup(); sock.pause(); const all = Buffer.concat(chunks); if (all.length > n) sock.unshift(all.slice(n)); res(all.slice(0, n)); } };
    function cleanup() { clearTimeout(t); sock.removeListener('data', onData); }
    sock.on('data', onData); sock.resume();
  });
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// anfitrión simulado: canal de control + conexiones de datos
async function hostRoom(port, token) {
  const tk = token ? ' T=' + token : '';
  const control = await connect(port);
  control.write('HOST ' + PROTO + ' N=Anfitrion' + tk + '\n');
  const l = await readLine(control);
  const m = /^OK ([A-Z0-9]{6})$/.exec(l);
  if (!m) throw new Error('respuesta de HOST inesperada: ' + l);
  const room = { code: m[1], control, data: [] };
  (async () => {
    try {
      for (;;) {
        const line = await readLine(control, 60000);
        if (line.startsWith('NEW ')) {
          const id = line.split(' ')[1];
          const d = await connect(port);
          d.write('ACCEPT ' + room.code + ' ' + id + tk + '\n');
          const ok2 = await readLine(d);
          if (ok2 === 'OK') room.data.push(d);
        } else if (line === 'PING') control.write('PONG\n');
      }
    } catch (e) { /* cerrada */ }
  })();
  return room;
}
async function join(port, code, token) {
  const s = await connect(port);
  s.write('JOIN ' + code + (token ? ' T=' + token : '') + '\n');
  const l = await readLine(s, 6000);
  return { sock: s, reply: l };
}
async function waitFor(fn, ms = 4000) { const t0 = Date.now(); while (Date.now() - t0 < ms) { if (fn()) return true; await sleep(20); } return false; }

(async () => {
  const relay = createRelay({ port: 0, host: '127.0.0.1', log: false, handshakeMs: 600, acceptMs: 700, maxClientsPerRoom: 3 });
  const port = await relay.listen();
  console.log('relay de prueba en el puerto ' + port);

  // 1) sala + 2 clientes, datos en ambos sentidos
  const room = await hostRoom(port);
  ok(/^[A-HJ-NP-Z2-9]{6}$/.test(room.code), 'el relay crea una sala con código de 6 caracteres sin ambiguos (' + room.code + ')');
  const c1 = await join(port, room.code);
  const c2 = await join(port, room.code.toLowerCase());
  ok(c1.reply === 'OK' && c2.reply === 'OK', 'dos clientes se unen con el código (el segundo en minúsculas)');
  await waitFor(() => room.data.length === 2);
  ok(room.data.length === 2, 'el anfitrión recibe 2 conexiones de datos emparejadas');
  const [h1, h2] = room.data;
  // los emparejamientos pueden llegar en otro orden: identifica por el contenido
  c1.sock.write('hola-c1'); c2.sock.write('hola-c2');
  const a = (await readBytes(h1, 7)).toString(), b = (await readBytes(h2, 7)).toString();
  ok(new Set([a, b]).size === 2 && [a, b].includes('hola-c1') && [a, b].includes('hola-c2'), 'cada cliente llega a su propia conexión del anfitrión');
  const hFor = (tag) => (a === tag ? h1 : h2);
  const big = crypto.randomBytes(300000);
  hFor('hola-c1').write(big);
  const got = await readBytes(c1.sock, big.length);
  ok(got.equals(big), 'el anfitrión envía 300 KB al cliente 1 sin pérdidas');
  const big2 = crypto.randomBytes(200000);
  c2.sock.write(big2);
  const got2 = await readBytes(hFor('hola-c2'), big2.length);
  ok(got2.equals(big2), 'el cliente 2 envía 200 KB al anfitrión sin pérdidas');
  // ráfaga de mensajes pequeños en orden
  for (let i = 0; i < 500; i++) c1.sock.write(Buffer.from([i & 255]));
  const burst = await readBytes(hFor('hola-c1'), 500);
  let inOrder = true; for (let i = 0; i < 500; i++) if (burst[i] !== (i & 255)) inOrder = false;
  ok(inOrder, '500 mensajes de 1 byte llegan en orden');

  // 2) un cliente se va: el otro sigue
  const hc1 = hFor('hola-c1'); let hc1Closed = false; hc1.on('close', () => (hc1Closed = true));
  c1.sock.destroy();
  ok(await waitFor(() => hc1Closed, 2000), 'al irse el cliente 1 se cierra su conexión en el anfitrión');
  c2.sock.write('sigo');
  ok((await readBytes(hFor('hola-c2'), 4)).toString() === 'sigo', 'el cliente 2 sigue conectado tras irse el 1');

  // 3) errores
  const bad = await join(port, 'ZZZZZZ');
  ok(bad.reply === 'ERR noroom', 'código inexistente => ERR noroom');
  const bad2 = await join(port, 'AB1');
  ok(bad2.reply === 'ERR badcode', 'código mal formado => ERR badcode');
  const bad3 = await connect(port); bad3.write('HOST version-vieja\n');
  ok((await readLine(bad3)) === 'ERR proto', 'versión de relay distinta => ERR proto');
  const bad4 = await connect(port); bad4.write('HOLA\n');
  ok((await readLine(bad4)) === 'ERR proto', 'comando desconocido => ERR proto');
  // sala llena (límite 3 por sala en esta prueba; ya hay 1 activa)
  const f1 = await join(port, room.code), f2 = await join(port, room.code);
  const f3 = await join(port, room.code);
  ok(f1.reply === 'OK' && f2.reply === 'OK' && f3.reply === 'ERR full', 'sala llena => ERR full al superar el máximo');
  // salud HTTP
  const h = await connect(port); h.write('GET / HTTP/1.0\r\n\r\n');
  let resp = ''; h.on('data', (d) => (resp += d)); await sleep(300);
  ok(resp.startsWith('HTTP/1.1 200'), 'comprobación de salud HTTP responde 200');
  // conexión muda: se cierra por tiempo de handshake
  const mute = await connect(port); let closed = false; mute.on('close', () => (closed = true));
  ok(await waitFor(() => closed, 2000), 'una conexión que no dice nada se cierra (timeout de handshake)');

  // 4) el anfitrión no responde al NEW: el cliente recibe ERR timeout
  const silent = net.connect(port, '127.0.0.1'); silent.write('HOST ' + PROTO + '\n');
  const sl = await readLine(silent); const scode = sl.split(' ')[1];
  const to = await join(port, scode);
  ok(to.reply === 'ERR timeout', 'si el anfitrión no acepta a tiempo => ERR timeout');

  // 5) el anfitrión cierra: la sala desaparece
  room.control.destroy();
  await waitFor(() => !relay.rooms.has(room.code));
  ok(!relay.rooms.has(room.code), 'al irse el anfitrión la sala se elimina');
  const late = await join(port, room.code);
  ok(late.reply === 'ERR noroom', 'unirse a una sala cerrada => ERR noroom');
  silent.destroy();

  // 6) límites: salas por IP
  const rel2 = createRelay({ port: 0, host: '127.0.0.1', log: false, maxRoomsPerIp: 2 });
  const p2 = await rel2.listen();
  const r1 = await hostRoom(p2), r2 = await hostRoom(p2);
  const x = await connect(p2); x.write('HOST ' + PROTO + '\n');
  ok((await readLine(x)) === 'ERR busy', 'límite de salas por IP => ERR busy');
  ok(r1.code !== r2.code, 'los códigos de sala son distintos');
  r1.control.destroy(); r2.control.destroy(); x.destroy();
  await rel2.close();


  // 7) token compartido (RELAY_TOKEN)
  const SECRET = 'tok_' + crypto.randomBytes(9).toString('base64url');
  const rel3 = createRelay({ port: 0, host: '127.0.0.1', log: false, token: SECRET, authDelayMs: 20, maxAuthFails: 6 });
  const p3 = await rel3.listen();
  const a1 = await connect(p3); a1.write('HOST ' + PROTO + ' N=Intruso\n');
  ok((await readLine(a1)) === 'ERR auth', 'con token activo, HOST sin token => ERR auth');
  const a2 = await connect(p3); a2.write('HOST ' + PROTO + ' N=Intruso T=' + SECRET + 'x\n');
  ok((await readLine(a2)) === 'ERR auth', 'con token activo, HOST con token incorrecto => ERR auth');
  ok(rel3.rooms.size === 0, 'los intentos rechazados no crean sala');
  const troom = await hostRoom(p3, SECRET);
  ok(/^[A-HJ-NP-Z2-9]{6}$/.test(troom.code), 'con el token correcto, HOST crea la sala (' + troom.code + ')');
  const j0 = await join(p3, troom.code);
  ok(j0.reply === 'ERR auth', 'JOIN sin token => ERR auth (aunque se conozca el código)');
  const j1 = await join(p3, troom.code, 'otro');
  ok(j1.reply === 'ERR auth', 'JOIN con token incorrecto => ERR auth');
  const j2 = await join(p3, troom.code, SECRET);
  ok(j2.reply === 'OK', 'JOIN con el token correcto => OK');
  await waitFor(() => troom.data.length === 1);
  j2.sock.write('con-token'); ok((await readBytes(troom.data[0], 9)).toString() === 'con-token', 'los datos circulan con token activo');
  // un intruso que conozca código e id no puede secuestrar la conexión de datos
  const hijack = await connect(p3); hijack.write('ACCEPT ' + troom.code + ' 999\n');
  ok((await readLine(hijack)) === 'ERR auth', 'ACCEPT sin token => ERR auth');
  // demasiados intentos fallidos desde una IP: el relay deja de responder (cierra)
  let silentClosed = 0;
  for (let i = 0; i < 12; i++) { const x = await connect(p3); x.write('JOIN AAAAAA\n'); let got = ''; x.on('data', (d) => (got += d)); let c = false; x.on('close', () => (c = true)); await waitFor(() => c, 600); if (c && got === '') silentClosed++; }
  ok(silentClosed >= 3, 'tras muchos tokens erróneos el relay cierra sin responder (' + silentClosed + ' conexiones)');
  ok(rel3.stats.authFailed >= 8, 'el relay cuenta los rechazos de autenticación (' + rel3.stats.authFailed + ')');
  [a1, a2, j0.sock, j1.sock, j2.sock, hijack].forEach((x) => { try { x.destroy(); } catch (e) { /* ok */ } });
  troom.control.destroy(); troom.data.forEach((x) => x.destroy());
  await rel3.close();
  // sin RELAY_TOKEN el token que mande un cliente se ignora (compatibilidad hacia atrás)
  const rel4 = createRelay({ port: 0, host: '127.0.0.1', log: false, token: '' });
  const p4 = await rel4.listen();
  const oldHost = await hostRoom(p4, 'cualquiera');
  const jj = await join(p4, oldHost.code);
  ok(oldHost.code && jj.reply === 'OK', 'sin RELAY_TOKEN: clientes con o sin token funcionan igual');
  jj.sock.destroy(); oldHost.control.destroy(); oldHost.data.forEach((x) => x.destroy());
  await rel4.close();

  [c1.sock, c2.sock, f1.sock, f2.sock, bad.sock, bad2.sock, bad3, bad4, h, mute].forEach((s) => { try { s.destroy(); } catch (e) { /* ok */ } });
  room.data.forEach((s) => s.destroy());
  await relay.close();
  console.log('\nRESULT passed=' + passed + ' failed=0');
  process.exit(0);
})().catch((e) => { console.error('\nFALLO: ' + (e && e.stack || e)); console.error('RESULT passed=' + passed + ' failed=1'); process.exit(1); });
