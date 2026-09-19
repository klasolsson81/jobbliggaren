import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import {mkdtempSync, writeFileSync, rmSync} from 'node:fs';
import http from 'node:http';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';

// No remote target option: only a newly created local Docker sandbox is tested.
const root = process.cwd();
const image = process.argv[2];
assert(image, 'Pass a locally available Caddy image ID or digest.');
const id = `edge-probe-${randomUUID()}`;
const scratch = mkdtempSync(path.join(os.tmpdir(), 'edge-probe-'));
const owned = [];
let networkCreated = false;
let port;
let count = 0;
const auth = `Basic ${Buffer.from('probe:synthetic-only').toString('base64')}`;
const rows = [];
function docker(...args) {
  return execFileSync('docker', args, {encoding:'utf8', timeout:60000, maxBuffer:2*1024*1024}).trim();
}
function request({method='GET', url='/', headers={}, bytes=0, chunked=false}={}) {
  assert(++count <= 100, 'Request ceiling exceeded');
  return new Promise((resolve, reject) => {
    const req = http.request({hostname:'127.0.0.1', port, path:url, method,
      headers:{Authorization:auth, Connection:'close', ...headers,
        ...(method === 'POST' && !chunked ? {'Content-Length':bytes} : {})}}, res => {
      let body = '';
      res.on('data', data => {body += data; if (body.length > 65536) req.destroy(new Error('Response ceiling'));});
      res.on('end', () => resolve({status:res.statusCode, headers:res.headers, body}));
    });
    req.setTimeout(15000, () => req.destroy(new Error('Request deadline')));
    req.on('error', reject);
    if (bytes) req.write(Buffer.alloc(bytes, 120));
    req.end();
  });
}
async function check(name, fn) {
  const start = performance.now();
  await fn();
  rows.push({name, status:'pass', milliseconds:Math.round(performance.now()-start)});
  console.log(JSON.stringify(rows.at(-1)));
}
function slowRequest(body, limit) {
  assert(++count <= 100);
  return new Promise((resolve, reject) => {
    const start = performance.now();
    const socket = net.connect({host:'127.0.0.1', port});
    let data = '';
    const timer = setTimeout(() => socket.destroy(new Error('Slow request exceeded ceiling')), limit+4000);
    socket.on('connect', () => socket.write(body));
    socket.on('data', part => {data += part;});
    socket.on('error', reject);
    socket.on('close', () => {clearTimeout(timer); resolve({elapsed:performance.now()-start, data});});
  });
}
try {
  assert(!process.env.DOCKER_HOST, 'Unset DOCKER_HOST: remote engines are not supported');
  const context = JSON.parse(docker('context','inspect'))[0];
  assert(/^(npipe|unix):/.test(context.Endpoints.docker.Host), 'Only a local Docker engine is permitted');
  const imageInfo = JSON.parse(docker('image','inspect',image))[0];
  const nodeImage = JSON.parse(docker('image','inspect','node:22-alpine'))[0].Id;
  console.log(JSON.stringify({imageId:imageInfo.Id, nodeImage, sourceCommit:execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim()}));
  const passwordHash = docker('run','--rm','--network','none',imageInfo.Id,'caddy','hash-password','--plaintext','synthetic-only');
  const env = ['-e','SITE_HOST=http://:8080','-e','ACME_EMAIL=probe@example.com','-e','ACME_CA=https://acme.invalid/directory','-e','BASIC_AUTH_USER=probe','-e',`BASIC_AUTH_HASH=${passwordHash}`];
  const mount = ['--mount',`type=bind,source=${path.join(root,'deploy/caddy/Caddyfile')},target=/etc/caddy/Caddyfile,readonly`,
    '--mount',`type=bind,source=${path.join(root,'deploy/caddy/challenge')},target=/etc/caddy/challenge,readonly`];
  const config = JSON.parse(docker('run','--rm','--network','none',...mount,...env,imageInfo.Id,'caddy','adapt','--config','/etc/caddy/Caddyfile'));
  const server = Object.values(config.apps.http.servers)[0];
  const handlers = [];
  function visit(v) {
    if (Array.isArray(v)) v.forEach(visit);
    else if (v && typeof v === 'object') {if (v.handler) handlers.push(v); Object.values(v).forEach(visit);}
  }
  visit(server.routes);
  const cap = handlers.find(h => h.handler === 'request_body').max_size;
  assert(cap > 0 && cap <= 25_000_000, 'Body probe safety ceiling');
  const headerMs = server.read_header_timeout/1e6;
  const bodyMs = server.read_timeout/1e6;
  assert(headerMs > 0 && headerMs <= 10000 && bodyMs > 0 && bodyMs <= 15000, 'Slow probe safety ceiling');
  writeFileSync(path.join(scratch,'upstream.cjs'), `
const http = require('node:http');
http.createServer((req,res) => {
  let bytes=0;
  req.on('data', data => bytes += data.length);
  req.on('error', () => res.destroy());
  req.on('end', () => {res.setHeader('Content-Type','application/json'); res.end(JSON.stringify({bytes,xff:req.headers['x-forwarded-for'],proto:req.headers['x-forwarded-proto']}));});
}).listen(3000,'0.0.0.0');
`);
  docker('network','create',id); networkCreated = true;
  const stub = docker('run','-d','--rm','--network',id,'--network-alias','web','--memory','96m','--cpus','0.5','--pids-limit','32',
    '--mount',`type=bind,source=${path.join(scratch,'upstream.cjs')},target=/probe.cjs,readonly`,nodeImage,'node','/probe.cjs');
  owned.push(stub);
  const edge = docker('run','-d','--rm','--network',id,'--publish','127.0.0.1::8080','--memory','128m','--cpus','1','--pids-limit','64',
    ...mount,...env,imageInfo.Id); owned.push(edge);
  port = Number(docker('port',edge,'8080/tcp').split(':').at(-1));
  assert(Number.isInteger(port) && port > 0);
  console.log(JSON.stringify({sandbox:{network:id,containers:[...owned],port}}));
  let ready = false;
  for (let i=0;i<20;i++) {
    try {if ((await request()).status === 200) {ready=true;break;}} catch {}
    await delay(500);
  }
  assert(ready,'Sandbox did not become ready');
  await check('normal request reaches synthetic upstream',async () => assert.equal((await request()).status,200));
  await check('unauthenticated burst stays at admission gate',async () => {
    for (let i=0;i<4;i++) assert.equal((await request({headers:{Authorization:''}})).status,401);
  });
  await check('unknown ACME path is answered locally',async () => {
    const r=await request({url:'/.well-known/acme-challenge/synthetic',headers:{Authorization:''}});
    assert.equal(r.status,404); assert(!r.body.includes('xff'));
  });
  await check('oversized unauthenticated headers are refused',async () => {
    assert.equal((await request({headers:{Authorization:'','X-Synthetic': 'x'.repeat(2*1024*1024)}})).status,431);
  });
  await check('forwarding headers are replaced at the edge',async () => {
    const baseline=JSON.parse((await request()).body);
    for (const value of ['198.51.100.7','198.51.100.7, 203.0.113.8','127.0.0.1']) {
      const r=await request({headers:{'X-Forwarded-For':value,'X-Forwarded-Proto':'https'}});
      assert.equal(r.status,200); const got=JSON.parse(r.body);
      assert.equal(got.xff,baseline.xff); assert.equal(got.proto,'http');
    }
  });
  for (const chunked of [false,true]) {
    await check(`body at cap accepted (chunked=${chunked})`,async () => {
      const r=await request({method:'POST',bytes:cap,chunked});
      assert.equal(r.status,200); assert.equal(JSON.parse(r.body).bytes,cap);
    });
    await check(`body over cap refused (chunked=${chunked})`,async () => {
      assert.equal((await request({method:'POST',bytes:cap+1,chunked})).status,413);
    });
  }
  await check('incomplete header connection closes within budget',async () => {
    const r=await slowRequest('GET / HTTP/1.1\r\nHost: localhost\r\nX-Incomplete: ',headerMs);
    assert(r.elapsed >= headerMs*0.8 && r.elapsed < headerMs+3500);
    assert(!r.data.includes('200 OK'));
  });
  await check('incomplete body is refused within budget',async () => {
    const r=await slowRequest(`POST / HTTP/1.1\r\nHost: localhost\r\nAuthorization: ${auth}\r\nContent-Length: 100\r\nConnection: close\r\n\r\nx`,bodyMs);
    assert(r.elapsed >= bodyMs*0.8 && r.elapsed < bodyMs+3500);
    assert(!r.data.includes('200 OK'));
  });
  await check('normal traffic recovers after refusals',async () => assert.equal((await request()).status,200));
  console.log(JSON.stringify({passed:rows.length, requests:count, boundary:'HTTP/1.1 plain local edge with synthetic upstream; no Next/API/TLS/provider verdict'}));
} finally {
  for (const container of owned.reverse()) {try {docker('rm','-f',container);} catch (error) {console.error('Owned container cleanup failed:',container);process.exitCode=1;}}
  if (networkCreated) {try {docker('network','rm',id);} catch {console.error('Owned network cleanup failed:',id);process.exitCode=1;}}
  assert(path.resolve(scratch).startsWith(path.resolve(os.tmpdir())+path.sep+'edge-probe-'));
  rmSync(scratch,{recursive:true,force:true});
}
