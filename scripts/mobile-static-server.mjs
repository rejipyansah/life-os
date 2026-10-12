import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const assetRoot = path.join(projectRoot, 'frontend', 'LifeOS.Web', 'dist');
const port = Number.parseInt(process.env.LIFEOS_E2E_FRONTEND_PORT ?? '5174', 10);
const contentTypes = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.mjs', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.svg', 'image/svg+xml'],
  ['.webp', 'image/webp'],
  ['.png', 'image/png'],
  ['.woff2', 'font/woff2'],
]);

const server = createServer(async (request, response) => {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    response.writeHead(405).end();
    return;
  }

  let pathname;
  try {
    pathname = decodeURIComponent(new URL(request.url ?? '/', 'http://localhost').pathname);
  } catch {
    response.writeHead(400).end();
    return;
  }

  const requestedFile = path.resolve(assetRoot, `.${pathname}`);
  if (requestedFile !== assetRoot && !requestedFile.startsWith(`${assetRoot}${path.sep}`)) {
    response.writeHead(403).end();
    return;
  }

  try {
    let body;
    let filePath = requestedFile;
    try {
      body = await readFile(filePath);
    } catch {
      filePath = path.join(assetRoot, 'index.html');
      body = await readFile(filePath);
    }

    const contentType = contentTypes.get(path.extname(filePath)) ?? 'application/octet-stream';
    const compressible = contentType.startsWith('text/')
      || contentType.includes('javascript')
      || contentType.includes('json');
    const useGzip = compressible && request.headers['accept-encoding']?.includes('gzip');
    const output = useGzip ? gzipSync(body, { level: 6 }) : body;
    response.writeHead(200, {
      'Content-Type': contentType,
      'Content-Length': output.byteLength,
      'Cache-Control': path.basename(filePath).includes('-')
        ? 'public, max-age=31536000, immutable'
        : 'no-cache',
      ...(compressible ? { Vary: 'Accept-Encoding' } : {}),
      ...(useGzip ? { 'Content-Encoding': 'gzip' } : {}),
    });
    response.end(request.method === 'HEAD' ? undefined : output);
  } catch {
    response.writeHead(500).end();
  }
});

server.listen(port, '127.0.0.1', () => {
  console.log(`Compressed production asset preview listening on http://127.0.0.1:${port}`);
});
