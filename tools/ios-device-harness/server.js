'use strict';
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = 9099;
const rootDir = __dirname;
const mimeTypes = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.svg': 'image/svg+xml', '.png': 'image/png' };

const server = http.createServer((req, res) => {
  const stamp = new Date().toISOString();
  console.log(`${stamp} ${req.socket.remoteAddress} ${req.method} ${req.url}`);
  if (req.method === 'POST' && req.url === '/result') {
    const chunks = [];
    req.on('data', c => chunks.push(c));
    req.on('end', () => {
      fs.appendFileSync(path.join(rootDir, 'results.jsonl'), Buffer.concat(chunks).toString('utf8') + '\n');
      res.writeHead(204, { 'Cache-Control': 'no-store' });
      res.end();
    });
    return;
  }
  const urlPath = decodeURIComponent(req.url.split('?')[0]);
  const filePath = path.normalize(path.join(rootDir, urlPath === '/' ? 'index.html' : urlPath));
  if (!filePath.startsWith(rootDir) || !fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
    res.writeHead(404, { 'Cache-Control': 'no-store' });
    res.end('not found');
    return;
  }
  res.writeHead(200, { 'Content-Type': mimeTypes[path.extname(filePath)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  fs.createReadStream(filePath).pipe(res);
});

server.listen(PORT, '0.0.0.0', () => console.log(`serving ${rootDir} on :${PORT}`));
