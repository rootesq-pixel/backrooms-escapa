FROM node:20-alpine
WORKDIR /app
COPY package.json server.js ./
ENV PORT=7780
EXPOSE 7780
USER node
CMD ["node", "server.js"]
