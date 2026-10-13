// Usage: node render.cjs video out.mp4 [fps] [t0] [t1] | stills outdir t... | events out.json | cover out.png t
// Serves this folder over a local HTTP server (ES modules need an http origin), then drives Chromium.
const { chromium } = require('playwright');
const { spawn } = require('child_process');
const http = require('http'), fs = require('fs'), path = require('path');
const ROOT = __dirname;
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.woff2': 'font/woff2', '.png': 'image/png', '.json': 'application/json' };
function serve() {
  return new Promise(res => {
    const srv = http.createServer((req, rsp) => {
      const p = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]));
      if (!p.startsWith(ROOT) || !fs.existsSync(p) || fs.statSync(p).isDirectory()) { rsp.writeHead(404); return rsp.end(); }
      rsp.writeHead(200, { 'Content-Type': TYPES[path.extname(p)] || 'application/octet-stream' });
      fs.createReadStream(p).pipe(rsp);
    }).listen(0, '127.0.0.1', () => res(srv));
  });
}
(async () => {
  const [mode, out, ...rest] = process.argv.slice(2);
  const srv = await serve();
  const port = srv.address().port;
  const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--font-render-hinting=none'] });
  const page = await browser.newPage({ viewport: { width: 1080, height: 1350 }, deviceScaleFactor: 1 });
  page.on('console', m => console.log('[page]', m.text()));
  page.on('pageerror', e => console.log('[pageerror]', e.message));
  await page.goto(`http://127.0.0.1:${port}/index.html`);
  await page.waitForFunction(() => window.READY === true, null, { timeout: 120000 });
  const dur = await page.evaluate(() => window.DURATION);
  if (mode === 'events') {
    fs.writeFileSync(out, JSON.stringify(await page.evaluate(() => ({ events: window.EVENTS, clock: window.CLOCK, duration: window.DURATION })), null, 1));
    console.log('duration', dur);
  } else if (mode === 'stills') {
    fs.mkdirSync(out, { recursive: true });
    for (const ts of rest) {
      const t = parseFloat(ts), t0 = Date.now();
      await page.evaluate(t => window.seek(t), t);
      await page.screenshot({ path: path.join(out, `t${t.toFixed(2).padStart(6, '0')}.png`) });
      console.log('still', t, (Date.now() - t0) + 'ms');
    }
  } else if (mode === 'cover') {
    await page.evaluate(t => { window.seek(t); window.hideForCover && window.hideForCover(); }, parseFloat(rest[0] || '1'));
    await page.screenshot({ path: out });
  } else if (mode === 'video') {
    const fps = parseInt(rest[0] || '30', 10);
    const t0 = parseFloat(rest[1] || '0'), t1 = parseFloat(rest[2] || String(dur));
    const n = Math.round((t1 - t0) * fps);
    const ff = spawn('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'image2pipe', '-framerate', String(fps), '-i', '-',
      '-c:v', 'libx264', '-preset', 'slow', '-crf', '15', '-pix_fmt', 'yuv420p', '-r', String(fps), out], { stdio: ['pipe', 'inherit', 'inherit'] });
    const started = Date.now();
    for (let f = 0; f < n; f++) {
      await page.evaluate(t => window.seek(t), t0 + f / fps);
      const buf = await page.screenshot({ type: 'png', timeout: 0 });
      if (!ff.stdin.write(buf)) await new Promise(r => ff.stdin.once('drain', r));
      if (f % 60 === 0) console.log(`frame ${f}/${n} (${((Date.now() - started) / 1000).toFixed(0)}s)`);
    }
    ff.stdin.end();
    await new Promise(r => ff.on('close', r));
    console.log('done', out, n, 'frames', ((Date.now() - started) / 1000).toFixed(0) + 's');
  }
  await browser.close(); srv.close();
})();
