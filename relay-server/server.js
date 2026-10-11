'use strict';
// Relay TCP para Backrooms Escape (sin dependencias; solo el módulo 'net' de Node).
//
// Protocolo (líneas de texto UTF-8 terminadas en \n; después del "OK" la conexión pasa a ser un tubo de bytes crudos):
//   Anfitrión, canal de control : HOST <protocolo> [nombre]\n   ->  OK <CODIGO>\n      (el canal sigue abierto)
//                                  relay -> anfitrión: NEW <id>\n (llega un cliente), PING\n ; anfitrión -> relay: PONG\n
//   Anfitrión, conexión de datos: ACCEPT <CODIGO> <id>\n         ->  OK\n  (y se empareja con el cliente <id>)
//   Cliente                     : JOIN <CODIGO>\n                ->  OK\n (cuando el anfitrión ha aceptado) | ERR <motivo>\n
//   Motivos de error: noroom (no existe), badcode (formato), full (sala llena), busy (relay lleno), timeout (el anfitrión no respondió), proto.
//   Token opcional: si el servidor se arranca con la variable RELAY_TOKEN, HOST / JOIN / ACCEPT deben llevar ademas " T=<token>" al final
//   (p. ej. "HOST BR-RELAY-1 N=Ana T=abc", "JOIN K838K3 T=abc"); si no, responde "ERR auth". Sin RELAY_TOKEN se ignora (compatible con clientes antiguos).
//   Cualquier conexión que empiece por "GET " recibe un 200 "ok" (comprobación de salud HTTP) y se cierra.
const net = require('net');
const crypto = require('crypto');

const PROTO = 'BR-RELAY-1';
const ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // sin I, O, 0, 1
const CODE_LEN = 6;

function envInt(name, def) { const v = parseInt(process.env[name], 10); return Number.isFinite(v) ? v : def; }

function createRelay(opts) {
  const cfg = Object.assign({
    port: envInt('PORT', 7780),
    host: process.env.HOST || '0.0.0.0',
    maxRooms: envInt('MAX_ROOMS', 500),
    maxClientsPerRoom: envInt('MAX_CLIENTS_PER_ROOM', 6),
    maxRoomsPerIp: envInt('MAX_ROOMS_PER_IP', 8),
    handshakeMs: envInt('HANDSHAKE_MS', 10000),
    acceptMs: envInt('ACCEPT_MS', 10000),
    hostSilenceMs: envInt('HOST_SILENCE_MS', 90000),
    pingMs: envInt('PING_MS', 20000),
    maxPipeMs: envInt('MAX_PIPE_HOURS', 6) * 3600 * 1000,
    token: process.env.RELAY_TOKEN || '',
    authDelayMs: envInt('AUTH_DELAY_MS', 400),
    maxAuthFails: envInt('MAX_AUTH_FAILS', 20),
    log: true,
  }, opts || {});

  const rooms = new Map();          // codigo -> { code, control, pending: Map(id -> {sock, timer}), clients, ip, created }
  const stats = { rooms: 0, joins: 0, pipes: 0, rejected: 0, authFailed: 0 };
  const authFails = new Map();      // ip -> { n, t }
  const tokenHash = cfg.token ? crypto.createHash('sha256').update(cfg.token).digest() : null;
  let nextId = 1;
  const log = (...a) => { if (cfg.log) console.log(new Date().toISOString(), ...a); };

  function newCode() {
    for (let tries = 0; tries < 50; tries++) {
      let c = '';
      for (let i = 0; i < CODE_LEN; i++) c += ALPHABET[Math.floor(Math.random() * ALPHABET.length)];
      if (!rooms.has(c)) return c;
    }
    return null;
  }
  const validCode = (c) => typeof c === 'string' && c.length === CODE_LEN && [...c].every((ch) => ALPHABET.includes(ch));
  const ipOf = (s) => (s.remoteAddress || '').replace('::ffff:', '');
  const roomsOfIp = (ip) => { let n = 0; for (const r of rooms.values()) if (r.ip === ip) n++; return n; };

  // devuelve true si el token es correcto (o no hay token configurado); si no, responde ERR auth (con retardo) y cierra
  function checkAuth(sock, parts) {
    if (!tokenHash) return true;
    const ip = ipOf(sock);
    let t = '';
    for (const p of parts) if (p.startsWith('T=')) t = p.slice(2);
    const h = crypto.createHash('sha256').update(t).digest();
    if (crypto.timingSafeEqual(h, tokenHash)) return true;
    stats.authFailed++; stats.rejected++;
    const now = Date.now();
    const f = authFails.get(ip) || { n: 0, t: now };
    if (now - f.t > 60000) { f.n = 0; f.t = now; }
    f.n++; authFails.set(ip, f);
    if (authFails.size > 5000) authFails.clear();
    if (f.n > cfg.maxAuthFails) { safeDestroy(sock); return false; }   // demasiados intentos: sin respuesta
    log('acceso rechazado (token) desde', ip);
    setTimeout(() => safeEnd(sock, 'ERR auth\n'), cfg.authDelayMs);
    return false;
  }

  function closeRoom(room, why) {
    if (!rooms.delete(room.code)) return;
    log('sala cerrada', room.code, why);
    for (const p of room.pending.values()) { clearTimeout(p.timer); safeEnd(p.sock, 'ERR timeout\n'); }
    room.pending.clear();
    clearInterval(room.pinger);
    safeDestroy(room.control);
  }
  function safeEnd(sock, msg) { try { if (msg) sock.write(msg); sock.end(); setTimeout(() => safeDestroy(sock), 500).unref(); } catch (e) { safeDestroy(sock); } }
  function safeDestroy(sock) { try { sock.destroy(); } catch (e) { /* nada */ } }

  function pipe(a, b, room) {
    stats.pipes++;
    a.setTimeout(0); b.setTimeout(0);
    a.setNoDelay(true); b.setNoDelay(true);
    let done = false;
    const end = () => { if (done) return; done = true; clearTimeout(maxT); room.clients = Math.max(0, room.clients - 1); safeDestroy(a); safeDestroy(b); };
    const maxT = setTimeout(end, cfg.maxPipeMs); maxT.unref();
    a.pipe(b); b.pipe(a);
    a.on('close', end); b.on('close', end); a.on('error', end); b.on('error', end);
  }

  // lee una línea byte a byte (sin pasarse: lo que venga después es parte del tubo)
  function readLine(sock, cb, maxLen) {
    let buf = Buffer.alloc(0), finished = false;
    const finish = (err, line) => {
      if (finished) return; finished = true;
      sock.removeListener('data', onData); sock.removeListener('close', onClose); sock.removeListener('error', onClose);
      sock.pause();
      cb(err, line, buf);
    };
    const onData = (d) => {
      buf = Buffer.concat([buf, d]);
      const i = buf.indexOf(10);
      if (i >= 0) {
        const line = buf.slice(0, i).toString('utf8').replace(/\r$/, '');
        const rest = buf.slice(i + 1);
        buf = rest;
        finish(null, line);
      } else if (buf.length > (maxLen || 256)) finish(new Error('línea demasiado larga'));
    };
    const onClose = () => finish(new Error('cerrada'));
    sock.on('data', onData); sock.on('close', onClose); sock.on('error', onClose);
    sock.resume();
  }

  const server = net.createServer((sock) => {
    sock.setNoDelay(true);
    sock.setKeepAlive(true, 30000);
    sock.on('error', () => safeDestroy(sock));
    const hs = setTimeout(() => safeDestroy(sock), cfg.handshakeMs);
    readLine(sock, (err, line, rest) => {
      clearTimeout(hs);
      if (err) return safeDestroy(sock);
      const parts = line.trim().split(/\s+/);
      const cmd = parts[0];
      if (line.startsWith('GET ')) { return safeEnd(sock, 'HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 3\r\nConnection: close\r\n\r\nok\n'); }
      if ((cmd === 'HOST' || cmd === 'ACCEPT' || cmd === 'JOIN') && !checkAuth(sock, parts)) return;
      if (cmd === 'HOST') return onHost(sock, parts);
      if (cmd === 'ACCEPT') return onAccept(sock, parts, rest);
      if (cmd === 'JOIN') return onJoin(sock, parts);
      safeEnd(sock, 'ERR proto\n');
    });
  });

  function onHost(sock, parts) {
    const ip = ipOf(sock);
    if (parts[1] !== PROTO) { stats.rejected++; return safeEnd(sock, 'ERR proto\n'); }
    if (rooms.size >= cfg.maxRooms || roomsOfIp(ip) >= cfg.maxRoomsPerIp) { stats.rejected++; return safeEnd(sock, 'ERR busy\n'); }
    const code = newCode();
    if (!code) return safeEnd(sock, 'ERR busy\n');
    const room = { code, control: sock, pending: new Map(), clients: 0, ip, created: Date.now(), lastHost: Date.now(), pinger: null };
    rooms.set(code, room); stats.rooms++;
    log('sala creada', code, 'desde', ip, '(' + rooms.size + ' activas)');
    sock.write('OK ' + code + '\n');
    // el canal de control: líneas PONG del anfitrión
    let acc = '';
    sock.on('data', (d) => { room.lastHost = Date.now(); acc += d.toString('utf8'); if (acc.length > 1024) acc = acc.slice(-256); acc = acc.replace(/[^\n]*\n/g, ''); });
    sock.setTimeout(0);
    sock.resume();
    room.pinger = setInterval(() => {
      if (Date.now() - room.lastHost > cfg.hostSilenceMs) return closeRoom(room, 'el anfitrión no responde');
      try { sock.write('PING\n'); } catch (e) { closeRoom(room, 'error de escritura'); }
    }, cfg.pingMs);
    room.pinger.unref();
    sock.on('close', () => closeRoom(room, 'el anfitrión se fue'));
  }

  function onJoin(sock, parts) {
    const code = (parts[1] || '').toUpperCase();
    if (!validCode(code)) { stats.rejected++; return safeEnd(sock, 'ERR badcode\n'); }
    const room = rooms.get(code);
    if (!room) { stats.rejected++; return safeEnd(sock, 'ERR noroom\n'); }
    if (room.clients + room.pending.size >= cfg.maxClientsPerRoom) { stats.rejected++; return safeEnd(sock, 'ERR full\n'); }
    const id = String(nextId++);
    const timer = setTimeout(() => { if (room.pending.delete(id)) safeEnd(sock, 'ERR timeout\n'); }, cfg.acceptMs);
    sock.on('close', () => { if (room.pending.delete(id)) clearTimeout(timer); });
    room.pending.set(id, { sock, timer });
    stats.joins++;
    try { room.control.write('NEW ' + id + '\n'); } catch (e) { closeRoom(room, 'error de escritura'); }
  }

  function onAccept(sock, parts, rest) {
    const room = rooms.get((parts[1] || '').toUpperCase());
    const p = room && room.pending.get(parts[2]);
    if (!room || !p) return safeEnd(sock, 'ERR noroom\n');
    room.pending.delete(parts[2]); clearTimeout(p.timer);
    room.clients++;
    p.sock.removeAllListeners('close');
    sock.write('OK\n'); p.sock.write('OK\n');
    if (rest && rest.length) p.sock.write(rest);
    pipe(sock, p.sock, room);
    log('emparejados en', room.code, '(' + room.clients + ' conexiones)');
  }

  return {
    server, rooms, stats, cfg,
    listen() { return new Promise((res, rej) => { server.once('error', rej); server.listen(cfg.port, cfg.host, () => { server.removeListener('error', rej); res(server.address().port); }); }); },
    close() { for (const r of [...rooms.values()]) closeRoom(r, 'apagado'); return new Promise((res) => server.close(() => res())); },
  };
}

module.exports = { createRelay, PROTO, ALPHABET, CODE_LEN };

if (require.main === module) {
  const relay = createRelay();
  relay.listen().then((p) => console.log('Relay de Backrooms Escape escuchando en TCP ' + p + ' (protocolo ' + PROTO + ', token ' + (relay.cfg.token ? 'ACTIVADO' : 'desactivado') + ')')).catch((e) => { console.error('No se pudo abrir el puerto:', e.message); process.exit(1); });
  const bye = () => relay.close().then(() => process.exit(0));
  process.on('SIGTERM', bye); process.on('SIGINT', bye);
  setInterval(() => console.log(new Date().toISOString(), 'estado: salas', relay.rooms.size, JSON.stringify(relay.stats)), 10 * 60 * 1000).unref();
}
