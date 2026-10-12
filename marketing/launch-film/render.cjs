// Usage: node render.cjs video out.mp4 [fps] | node render.cjs stills outdir t1 t2 ... | node render.cjs events out.json
const { chromium } = require('playwright');
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
(async () => {
  const [mode, out, ...rest] = process.argv.slice(2);
  const browser = await chromium.launch({ args: ['--font-render-hinting=none', '--disable-lcd-text'] });
  const page = await browser.newPage({ viewport: { width: 1080, height: 1350 }, deviceScaleFactor: 1 });
  page.on('console', m => console.log('[page]', m.text()));
  page.on('pageerror', e => console.log('[pageerror]', e.message));
  await page.goto('file://' + path.join(__dirname, 'index.html'));
  await page.waitForFunction(() => window.READY === true, null, { timeout: 30000 });
  const dur = await page.evaluate(() => window.DURATION);
  if (mode === 'events') {
    const data = await page.evaluate(() => ({ events: window.EVENTS, clock: window.CLOCK, duration: window.DURATION }));
    fs.writeFileSync(out, JSON.stringify(data, null, 1));
    console.log('duration', dur, 'events', data.events.length);
  } else if (mode === 'stills') {
    fs.mkdirSync(out, { recursive: true });
    for (const ts of rest) {
      const t = parseFloat(ts);
      await page.evaluate(t => window.seek(t), t);
      await page.screenshot({ path: path.join(out, `t${t.toFixed(2).padStart(6, '0')}.png`) });
    }
  } else if (mode === 'cover') {
    // poster frame for a custom LinkedIn thumbnail: the hook frame without the SDH line or the cursor
    await page.evaluate(t => { window.seek(t); for (const id of ['sdh', 'cursor', 'ripple']) document.getElementById(id).style.display = 'none'; document.querySelectorAll('#stage > div[style*="z-index:50"]').forEach(e => (e.style.display = 'none')); }, parseFloat(rest[0] || '2.6'));
    await page.screenshot({ path: out });
  } else if (mode === 'video') {
    const fps = parseInt(rest[0] || '30', 10);
    const t0 = parseFloat(rest[1] || '0'), t1 = parseFloat(rest[2] || String(dur));
    const n = Math.round((t1 - t0) * fps);
    const ff = spawn('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'image2pipe', '-framerate', String(fps), '-i', '-',
      '-c:v', 'libx264', '-preset', 'slow', '-crf', '14', '-pix_fmt', 'yuv420p', '-r', String(fps), out], { stdio: ['pipe', 'inherit', 'inherit'] });
    const started = Date.now();
    for (let f = 0; f < n; f++) {
      const t = t0 + f / fps;
      await page.evaluate(t => window.seek(t), t);
      const buf = await page.screenshot({ type: 'png' });
      if (!ff.stdin.write(buf)) await new Promise(r => ff.stdin.once('drain', r));
      if (f % 150 === 0) console.log(`frame ${f}/${n} (${((Date.now() - started) / 1000).toFixed(0)}s)`);
    }
    ff.stdin.end();
    await new Promise(r => ff.on('close', r));
    console.log('done', out, n, 'frames');
  }
  await browser.close();
})();
