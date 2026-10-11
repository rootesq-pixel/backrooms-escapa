// Ejemplo para pm2 (Windows / Linux):  copia a ecosystem.config.js, cambia el token y ejecuta
//   pm2 start ecosystem.config.js && pm2 save
// NO subas tu ecosystem.config.js con el token a ningún repositorio.
module.exports = {
  apps: [{
    name: 'backrooms-relay',
    script: 'server.js',
    env: {
      PORT: '7780',
      RELAY_TOKEN: 'PON-AQUI-TU-TOKEN',   // el mismo que se integró en el juego
    },
  }],
};
