# Relay de Backrooms Escape

Servidor mínimo (Node.js, **sin dependencias**, solo el módulo `net`) que permite jugar en cooperativo por Internet **sin abrir puertos en el router**.
Anfitrión y clientes se conectan *hacia fuera* al relay; el relay los empareja por un **código de sala de 6 caracteres** y reenvía los bytes tal cual
(no entiende el protocolo del juego ni lo cifra). Si no tienes un relay, el juego sigue funcionando por conexión directa (IP:puerto, UPnP, VPN).

```
 Anfitrión ──(canal de control + 1 conexión por jugador)──► RELAY ◄──(JOIN ABC234)── Cliente 1
                                                                 ◄──(JOIN ABC234)── Cliente 2
```

## Ejecutarlo

```bash
node server.js                 # escucha en TCP 7780 (variable PORT para cambiarlo)
npm test                       # prueba de integración local: 1 anfitrión + 2 clientes a través del relay
docker build -t backrooms-relay . && docker run -p 7780:7780 backrooms-relay
```

Variables de entorno (todas opcionales): `PORT` (7780), `HOST` (0.0.0.0), `MAX_ROOMS` (500), `MAX_CLIENTS_PER_ROOM` (6), `MAX_ROOMS_PER_IP` (8),
`RELAY_TOKEN` (vacío = sin token), `AUTH_DELAY_MS` (400), `MAX_AUTH_FAILS` (20/min por IP), `HANDSHAKE_MS` (10000), `ACCEPT_MS` (10000), `HOST_SILENCE_MS` (90000), `PING_MS` (20000), `MAX_PIPE_HOURS` (6).

## Protocolo (líneas de texto terminadas en `\n`; tras el `OK` la conexión es un tubo de bytes)

| Quién | Mensaje | Respuesta |
|---|---|---|
| Anfitrión (canal de control) | `HOST BR-RELAY-1 N=<nombre> [T=<token>]` | `OK <CODIGO>`; el canal sigue abierto: el relay manda `NEW <id>` por cada cliente y `PING` (se contesta `PONG`) |
| Anfitrión (una conexión por cliente) | `ACCEPT <CODIGO> <id> [T=<token>]` | `OK` y la conexión queda emparejada con ese cliente |
| Cliente | `JOIN <CODIGO> [T=<token>]` | `OK` (cuando el anfitrión ha aceptado) o `ERR <motivo>` |

Motivos de error: `noroom` (no existe), `badcode` (formato), `full` (sala llena), `busy` (relay lleno / demasiadas salas de tu IP), `timeout` (el anfitrión no respondió), `proto` (versión), `auth` (token incorrecto o ausente).
Una petición que empieza por `GET ` recibe `200 ok` (sirve como comprobación de salud HTTP). Códigos: alfabeto `A-Z` + `2-9` sin `I`, `O`, `0`, `1` (32^6 ≈ 1.000 millones).

## Token compartido (RELAY_TOKEN) — recomendado si el relay es público

Si arrancas el servidor con la variable de entorno **`RELAY_TOKEN`**, `HOST`, `JOIN` y `ACCEPT` deben llevar `T=<token>` o se responde `ERR auth`
(tras una pausa de 0,4 s; más de 20 fallos por minuto desde una IP y deja de responder). Así un desconocido que averigüe la IP no puede crear ni
unirse a salas. El token se compara en tiempo constante y **nunca se escribe en los logs**. Sin `RELAY_TOKEN` el servidor ignora los tokens
(compatible con clientes antiguos y nuevos). Los clientes nuevos funcionan igual con un servidor viejo (sin token) que con uno nuevo.

### Actualizar un relay que ya tienes en marcha en **Windows Server**
1. Copia el nuevo `server.js` encima del anterior (misma carpeta). `test.js` es opcional (`node test.js`).
2. Define el token **y reinicia el proceso** (las variables de entorno solo las lee un proceso al arrancar):
   - **Prueba a mano (PowerShell):** `$env:RELAY_TOKEN="TU-TOKEN"; node server.js` → debe decir `token ACTIVADO`.
   - **pm2:** copia `ecosystem.config.example.js` a `ecosystem.config.js`, pon tu token y ejecuta
     `pm2 delete backrooms-relay` · `pm2 start ecosystem.config.js` · `pm2 save` (si usas `pm2 start server.js`, vale `$env:RELAY_TOKEN="TU-TOKEN"; pm2 restart backrooms-relay --update-env; pm2 save`).
   - **Tarea programada:** edita la acción → Programa `cmd.exe`, argumentos
     `/c cd /d C:\ruta\backrooms-relay && set "RELAY_TOKEN=TU-TOKEN" && node server.js` y reinicia la tarea (Detener + Ejecutar). Alternativa: `setx RELAY_TOKEN "TU-TOKEN" /M` (consola de administrador) y **reiniciar la tarea o el equipo**.
   - **Servicio con NSSM:** `nssm set backrooms-relay AppEnvironmentExtra RELAY_TOKEN=TU-TOKEN` y `nssm restart backrooms-relay`.
3. El cortafuegos no cambia (TCP 7780 entrante). Comprueba: `curl http://IP:7780/` → `ok`, y el log de arranque dice `token ACTIVADO`.
4. A partir de ese momento los clientes **sin** el token (versiones anteriores a la v6.2) reciben `ERR auth`: es lo esperado.

## Seguridad y límites

- El relay no autentica ni cifra: cualquiera que conozca el código puede intentar unirse (el juego rechaza salas llenas / partidas empezadas). Los códigos desaparecen al irse el anfitrión.
- Límites por defecto: 500 salas, 8 salas por IP, 6 conexiones por sala, tiempo de saludo de 10 s, tubos de 6 h como máximo.
- Consume muy poco (reenvía unos pocos KB/s por jugador); una VM mínima sobra para decenas de salas.

## Desplegarlo gratis (o casi) — IMPORTANTE: necesita **TCP crudo**, no solo HTTP

El cliente del juego habla TCP directo con el relay (IPv4), así que sirve cualquier sitio que exponga un puerto TCP público. **No sirven** los planes gratuitos que solo
dan HTTP/WebSocket (p. ej. Render gratis). Opciones (comprueba precios y condiciones actuales; no se han podido verificar desde aquí):

1. **Un VPS / VM con IP pública** (lo más sencillo y fiable): p. ej. la capa «Always Free» de Oracle Cloud, el `e2-micro` gratuito de Google Cloud, o cualquier VPS de 3-5 €/mes.
   ```bash
   # en la VM (Ubuntu/Debian)
   sudo apt install -y nodejs        # o instala Node 18+ con nvm
   git clone <tu copia> backrooms-relay && cd backrooms-relay
   PORT=7780 node server.js          # abre TCP 7780 en el cortafuegos de la VM y en el panel del proveedor
   ```
   Para dejarlo siempre encendido: Docker (`docker run -d --restart=always -p 7780:7780 backrooms-relay`) o un servicio systemd.
2. **Fly.io** (incluye `fly.toml` de ejemplo): `fly launch --no-deploy --copy-config` y `fly deploy`. Los servicios TCP crudos pueden requerir una IPv4 dedicada (de pago) — revisa su documentación; la dirección será `<app>.fly.dev:7780`.
3. **Railway**: servicio desde este Dockerfile + «TCP Proxy» al puerto 7780 (tiene créditos de prueba; después es de pago). Te da una dirección `xxx.proxy.rlwy.net:PUERTO`.
4. Tu propio PC/Raspberry con el puerto TCP 7780 redirigido en el router (si puedes abrir puertos, probablemente no necesites relay).

Comprueba que está vivo: `curl http://<direccion>:7780/` debe devolver `ok`.

## Usarlo desde el juego

Pon la dirección del relay en una de estas tres formas (por orden de prioridad):
1. Variable de entorno `BACKROOMS_RELAY=host:puerto` (para pruebas).
2. El campo **«Servidor relay»** del menú Multijugador (se guarda en `PlayerPrefs`).
3. Un archivo de texto **`relay.txt`** junto a `BackroomsEscape.exe` con una línea `mi-relay.ejemplo.com:7780` (líneas con `#` = comentarios).

Con el relay configurado se activan **Alojar con código** y **Unirse con código** en el menú Multijugador.
