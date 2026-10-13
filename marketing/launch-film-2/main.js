// "Väntläge" — deterministic 3D film. window.seek(t) renders the frame at t seconds.
// Every timing constant lives in SHOTS / CUE below; the audio mix reads window.EVENTS.
import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';

const W = 1080, H = 1350;
const BPM = 120, BEAT = 60 / BPM, BAR = 4 * BEAT;

// ---------------------------------------------------------------- math helpers
const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
const P = (t, a, b) => clamp((t - a) / (b - a));
const lerp = (a, b, x) => a + (b - a) * x;
const E = {
  outExpo: x => (x >= 1 ? 1 : 1 - Math.pow(2, -10 * x)),
  outCubic: x => 1 - Math.pow(1 - x, 3),
  inCubic: x => x * x * x,
  inOutCubic: x => (x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2),
  inOutSine: x => -(Math.cos(Math.PI * x) - 1) / 2,
  outBack: x => { const c1 = 1.6, c3 = c1 + 1; return 1 + c3 * Math.pow(x - 1, 3) + c1 * Math.pow(x - 1, 2); },
};
function rng(seed) { return () => { seed |= 0; seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
const V = (x, y, z) => new THREE.Vector3(x, y, z);
const $ = id => document.getElementById(id);

// ---------------------------------------------------------------- renderer + post
const canvas = $('gl');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: false, preserveDrawingBuffer: true });
renderer.setPixelRatio(1);
renderer.setSize(W, H, false);
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.0;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
const rt = new THREE.WebGLRenderTarget(W, H, { type: THREE.HalfFloatType, samples: 4 });
const composer = new EffectComposer(renderer, rt);
const renderPass = new RenderPass(new THREE.Scene(), new THREE.PerspectiveCamera());
const bloom = new UnrealBloomPass(new THREE.Vector2(W / 2, H / 2), 0.55, 0.6, 0.82);
const FinalShader = {
  uniforms: { tDiffuse: { value: null }, uFrame: { value: 0 }, uVig: { value: 0.32 }, uGrain: { value: 0.028 }, uFade: { value: 0 }, uFlash: { value: 0 } },
  vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
  fragmentShader: `uniform sampler2D tDiffuse; uniform float uFrame, uVig, uGrain, uFade, uFlash; varying vec2 vUv;
    float h(vec2 p){ return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
    void main(){ vec4 c = texture2D(tDiffuse, vUv); vec2 d = vUv - 0.5; d.x *= 0.82;
      c.rgb *= mix(1.0 - uVig, 1.0, smoothstep(0.82, 0.18, length(d)));
      c.rgb += (h(floor(vUv * vec2(1080.0, 1350.0)) + uFrame * 1.731) - 0.5) * uGrain;
      c.rgb = mix(c.rgb, vec3(1.0), uFlash); c.rgb *= 1.0 - uFade; gl_FragColor = c; }`,
};
const finalPass = new ShaderPass(FinalShader);
composer.addPass(renderPass);
composer.addPass(bloom);
composer.addPass(new OutputPass());
composer.addPass(finalPass);

// ---------------------------------------------------------------- canvas texture helpers
function canvasTex(w, h, draw) {
  const c = document.createElement('canvas'); c.width = w; c.height = h;
  const ctx = c.getContext('2d'); if (draw) draw(ctx, w, h);
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4;
  t.userData = { canvas: c, ctx };
  return t;
}
const FONT_MONO = "'JBM', monospace", FONT_SANS = "'SS3', sans-serif";

// ---------------------------------------------------------------- paper plane
function planeGeometry() {
  // a folded dart: two wing panels each split by a crease, plus a double keel
  const N = [0, 0, 1.0], T = [0, 0.0, -0.55], FL = [0.2, 0.05, -0.54], FR = [-0.2, 0.05, -0.54];
  const L = [0.64, 0.14, -0.52], R = [-0.64, 0.14, -0.52], K = [0, -0.2, -0.5], K2 = [0, -0.12, 0.2];
  const tris = [[N, FL, T], [N, L, FL], [N, T, FR], [N, FR, R], [N, K2, T], [K2, K, T]];
  const pos = [], uv = [];
  // u runs right-to-left in model x so the decal reads correctly from above
  for (const tri of tris) for (const p of tri) { pos.push(...p); uv.push((0.64 - p[0]) / 1.28, (p[2] + 0.55) / 1.55); }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.computeVertexNormals();
  return g;
}
const PLANE_GEO = planeGeometry();
const PLANE_GEO_M = (() => { const g = PLANE_GEO.clone(), uv = g.attributes.uv; for (let i = 0; i < uv.count; i++) uv.setX(i, 1 - uv.getX(i)); return g; })();
function paperTexture(label, wingNums = false) {
  return canvasTex(512, 512, (ctx, w, h) => {
    ctx.fillStyle = '#F7F4EC'; ctx.fillRect(0, 0, w, h);
    const gr = ctx.createLinearGradient(0, 0, 0, h); gr.addColorStop(0, 'rgba(255,255,255,.0)'); gr.addColorStop(1, 'rgba(120,105,80,.10)');
    ctx.fillStyle = gr; ctx.fillRect(0, 0, w, h);
    ctx.strokeStyle = 'rgba(70,110,170,.22)'; ctx.lineWidth = 2;
    for (let y = 30; y < h; y += 28) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke(); }
    ctx.strokeStyle = 'rgba(200,80,80,.25)'; ctx.beginPath(); ctx.moveTo(w * 0.86, 0); ctx.lineTo(w * 0.86, h); ctx.stroke();
    if (label) {
      ctx.fillStyle = '#15603F'; ctx.textAlign = 'center'; ctx.textBaseline = 'alphabetic';
      ctx.font = `800 46px ${FONT_SANS}`; ctx.fillText(label.word, w / 2, 318);
      ctx.font = `800 132px ${FONT_SANS}`; ctx.fillText(label.num, w / 2, 440);
      if (wingNums) { ctx.font = `800 78px ${FONT_SANS}`; ctx.fillText(label.num, w * 0.2, 440); ctx.fillText(label.num, w * 0.8, 440); }
    }
  });
}
function makePlane(label, scale = 1, color = 0xffffff, lights = true, wingNums = false) {
  const tex = paperTexture(label, wingNums);
  const mat = new THREE.MeshStandardMaterial({ map: tex, color, emissive: 0xffffff, emissiveMap: tex, emissiveIntensity: 0.2, roughness: 0.85, metalness: 0, side: THREE.FrontSide });
  const m = new THREE.Mesh(PLANE_GEO, mat); m.castShadow = true;
  const back = new THREE.Mesh(PLANE_GEO_M, mat.clone()); back.material.side = THREE.BackSide;
  const g = new THREE.Group(); g.add(m); g.add(back); g.scale.setScalar(scale);
  if (lights) {
    // navigation lights: red port (+x), green starboard (-x), white tail strobe
    const mk = (c, x, y, z) => { const b = new THREE.Mesh(NAV_GEO, new THREE.MeshBasicMaterial({ color: c, toneMapped: false })); b.material.color.multiplyScalar(3); b.position.set(x, y, z); g.add(b); return b; };
    g.userData.nav = [mk(0xff3b30, 0.64, 0.14, -0.52), mk(0x30ff7a, -0.64, 0.14, -0.52)];
    g.userData.strobe = mk(0xffffff, 0, 0.02, -0.56); g.userData.strobe.visible = false;
  }
  return g;
}
const NAV_GEO = new THREE.SphereGeometry(0.022, 8, 6);
// orient a plane along a velocity vector with bank
const _q = new THREE.Quaternion(), _m = new THREE.Matrix4();
function orient(obj, pos, vel, bank = 0) {
  obj.position.copy(pos);
  const f = vel.clone().normalize();
  _m.lookAt(V(0, 0, 0), f.clone().negate(), V(0, 1, 0));
  obj.quaternion.setFromRotationMatrix(_m);
  obj.rotateZ(bank);
}

// ---------------------------------------------------------------- sky dome
function skyDome(top, horizon, glow, sunDir = V(0, 0.2, -1), sunStrength = 0) {
  const mat = new THREE.ShaderMaterial({
    side: THREE.BackSide, depthWrite: false, fog: false,
    uniforms: { uTop: { value: new THREE.Color(top) }, uHor: { value: new THREE.Color(horizon) }, uGlow: { value: new THREE.Color(glow) }, uSun: { value: sunDir.clone().normalize() }, uSunS: { value: sunStrength } },
    vertexShader: 'varying vec3 vDir; void main(){ vDir = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
    fragmentShader: `uniform vec3 uTop,uHor,uGlow,uSun; uniform float uSunS; varying vec3 vDir;
      void main(){ float y = clamp(vDir.y, -0.2, 1.0); vec3 c = mix(uHor, uTop, smoothstep(0.0, 0.55, y));
        c += uGlow * pow(1.0 - abs(y), 6.0) * 0.6; float s = max(dot(normalize(vDir), uSun), 0.0);
        c += uGlow * pow(s, 8.0) * uSunS + vec3(1.0, 0.92, 0.75) * pow(s, 400.0) * uSunS * 3.0; gl_FragColor = vec4(c, 1.0); }`,
  });
  const m = new THREE.Mesh(new THREE.SphereGeometry(900, 32, 16), mat);
  m.renderOrder = -1;
  return m;
}
function stars(n, seed) {
  const r = rng(seed), pos = [];
  for (let i = 0; i < n; i++) { const a = r() * Math.PI * 2, y = 0.12 + r() * 0.88, rr = Math.sqrt(1 - y * y); pos.push(Math.cos(a) * rr * 850, y * 850, Math.sin(a) * rr * 850); }
  const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  return new THREE.Points(g, new THREE.PointsMaterial({ color: 0xdfe8ff, size: 1.6, sizeAttenuation: false, fog: false, transparent: true, opacity: 0.8 }));
}

// ---------------------------------------------------------------- city
function windowTextures(seed, cols, rows, lit) {
  const r = rng(seed);
  const base = canvasTex(256, 512, (ctx, w, h) => {
    ctx.fillStyle = '#1c2a2c'; ctx.fillRect(0, 0, w, h);
    const cw = w / cols, ch = h / rows;
    for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) { ctx.fillStyle = '#0f1719'; ctx.fillRect(x * cw + cw * 0.18, y * ch + ch * 0.2, cw * 0.64, ch * 0.55); }
  });
  const emis = canvasTex(256, 512, (ctx, w, h) => {
    ctx.fillStyle = '#000'; ctx.fillRect(0, 0, w, h);
    const cw = w / cols, ch = h / rows;
    for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) {
      if (r() < lit) { const warm = r() < 0.78; ctx.fillStyle = warm ? `rgba(255,${200 + r() * 30 | 0},${130 + r() * 40 | 0},1)` : 'rgba(190,220,255,1)'; ctx.globalAlpha = 0.55 + r() * 0.45; ctx.fillRect(x * cw + cw * 0.18, y * ch + ch * 0.2, cw * 0.64, ch * 0.55); ctx.globalAlpha = 1; }
    }
  });
  return { base, emis };
}
function buildCity(scene, opts) {
  const r = rng(opts.seed || 7);
  const box = new THREE.BoxGeometry(1, 1, 1); box.translate(0, 0.5, 0);
  const roofMat = new THREE.MeshStandardMaterial({ color: 0x1a2526, roughness: 0.95 });
  const variants = [0, 1, 2, 3].map(i => {
    const tx = windowTextures(100 + i * 17, 6, 16, 0.3 + i * 0.06);
    const side = new THREE.MeshStandardMaterial({ map: tx.base, emissive: 0xffffff, emissiveMap: tx.emis, emissiveIntensity: 1.25, roughness: 0.9 });
    return [side, side, roofMat, roofMat, side, side];
  });
  const items = variants.map(() => []), all = [];
  const { x0, x1, z0, z1, step, clear } = opts;
  for (let x = x0; x <= x1; x += step) for (let z = z0; z <= z1; z += step) {
    if (clear.some(c => Math.abs(x - c[0]) < c[2] && Math.abs(z - c[1]) < c[3])) continue;
    if (r() < 0.1) continue;
    const d = Math.hypot(x, z + 40) / 160;
    const h = (4 + r() * 16 + (r() < 0.12 ? 14 : 0)) * (1.15 - 0.6 * clamp(d));
    const w = step * (0.5 + r() * 0.3), dd = step * (0.5 + r() * 0.3);
    const b = [x + (r() - 0.5) * 2, z + (r() - 0.5) * 2, w, h, dd];
    items[(r() * 4) | 0].push(b); all.push(b);
  }
  items.forEach((list, i) => {
    const im = new THREE.InstancedMesh(box, variants[i], list.length);
    list.forEach((b, k) => { _m.compose(V(b[0], 0, b[1]), new THREE.Quaternion(), V(b[2], b[3], b[4])); im.setMatrixAt(k, _m); });
    im.instanceMatrix.needsUpdate = true; im.receiveShadow = true;
    scene.add(im);
  });
  // roof furniture: parapets, plant rooms, and red aviation lights on the tall ones
  const roofs = [], av = [];
  all.forEach(b => {
    roofs.push([b[0], b[3], b[1], b[2] * 1.04, 0.5, b[4] * 1.04]);
    if (r() < 0.55) roofs.push([b[0] + (r() - 0.5) * b[2] * 0.4, b[3] + 0.5, b[1] + (r() - 0.5) * b[4] * 0.4, b[2] * (0.25 + r() * 0.2), 1.2 + r() * 1.6, b[4] * (0.25 + r() * 0.2)]);
    if (b[3] > 15) av.push(V(b[0], b[3] + 0.8, b[1]));
  });
  const roofIm = new THREE.InstancedMesh(box, roofMat, roofs.length);
  roofs.forEach((q, k) => { _m.compose(V(q[0], q[1], q[2]), new THREE.Quaternion(), V(q[3], q[4], q[5])); roofIm.setMatrixAt(k, _m); });
  scene.add(roofIm);
  const avIm = new THREE.InstancedMesh(new THREE.SphereGeometry(0.28, 8, 6), new THREE.MeshBasicMaterial({ color: new THREE.Color(0xff2a20).multiplyScalar(5), toneMapped: false }), av.length);
  av.forEach((v, k) => { _m.makeTranslation(v.x, v.y, v.z); avIm.setMatrixAt(k, _m); });
  scene.add(avIm);
  // ground: dark asphalt blocks, streets with sodium light pools
  const gtex = canvasTex(512, 512, (ctx, w, h) => {
    ctx.fillStyle = '#0b1212'; ctx.fillRect(0, 0, w, h);
    const n = rng(4); for (let i = 0; i < 4000; i++) { ctx.fillStyle = `rgba(255,255,255,${n() * 0.025})`; ctx.fillRect(n() * w, n() * h, 2, 2); }
  });
  gtex.wrapS = gtex.wrapT = THREE.RepeatWrapping; gtex.repeat.set(30, 30);
  const glow = canvasTex(256, 256, (ctx, w, h) => {
    ctx.fillStyle = '#000'; ctx.fillRect(0, 0, w, h);
    const pool = (x, y) => { const g = ctx.createRadialGradient(x, y, 0, x, y, 30); g.addColorStop(0, 'rgba(255,190,110,.9)'); g.addColorStop(0.25, 'rgba(255,170,90,.35)'); g.addColorStop(1, 'rgba(255,150,70,0)'); ctx.fillStyle = g; ctx.fillRect(x - 30, y - 30, 60, 60); };
    for (let i = 0; i < 256; i += 64) { pool(i + 32, 2); pool(2, i + 32); }
  });
  glow.wrapS = glow.wrapT = THREE.RepeatWrapping;
  const gRep = 1200 / step, fr = v => v - Math.floor(v); glow.repeat.set(gRep, gRep); glow.offset.set(-fr((x0 + step / 2 + 600) / step), -fr((600 - (z0 + step / 2)) / step));
  if (opts.ground !== false) {
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(1200, 1200), new THREE.MeshStandardMaterial({ map: gtex, emissive: 0xffffff, emissiveMap: glow, emissiveIntensity: 0.55, roughness: 1 }));
  ground.rotation.x = -Math.PI / 2; ground.receiveShadow = true;
  scene.add(ground);
  }
  if (opts.traffic === false) return { cars: null, av: avIm };
  // traffic: head- and tail-light streaks travelling along the street grid
  const cars = { list: [], white: null, red: null };
  const streak = new THREE.BoxGeometry(0.2, 0.16, 1.5);
  cars.white = new THREE.InstancedMesh(streak, new THREE.MeshBasicMaterial({ color: new THREE.Color(0xfff2d8).multiplyScalar(3.2), toneMapped: false }), 130);
  cars.red = new THREE.InstancedMesh(streak, new THREE.MeshBasicMaterial({ color: new THREE.Color(0xff3020).multiplyScalar(3.2), toneMapped: false }), 130);
  for (let i = 0; i < 260; i++) {
    const alongX = r() < 0.5;
    const lane = Math.round((alongX ? (z0 + r() * (z1 - z0)) : (x0 + r() * (x1 - x0))) / step) * step + step / 2;
    cars.list.push({ alongX, lane, off: r() * 1000, sp: 9 + r() * 7, dir: r() < 0.5 ? 1 : -1 });
  }
  cars.bounds = { x0, x1, z0, z1 };
  scene.add(cars.white); scene.add(cars.red);
  return { cars, av: avIm };
}
function updateTraffic(cars, t) {
  const { x0, x1, z0, z1 } = cars.bounds;
  cars.list.forEach((c, i) => {
    const lo = c.alongX ? x0 : z0, len = (c.alongX ? x1 : z1) - lo;
    const u = lo + (((c.off + t * c.sp * c.dir) % len) + len) % len;
    const side = c.dir * 0.9;
    const pos = c.alongX ? V(u, 0.35, c.lane + side) : V(c.lane + side, 0.35, u);
    _m.makeRotationY(c.alongX ? Math.PI / 2 : 0); _m.setPosition(pos);
    // half the cars face us (white), half away (red)
    (i % 2 ? cars.red : cars.white).setMatrixAt(i >> 1, _m);
  });
  cars.white.instanceMatrix.needsUpdate = true; cars.red.instanceMatrix.needsUpdate = true;
}
// ---------------------------------------------------------------- vortex ("VI ÅTERKOMMER")
function buildVortex(n, seed) {
  const r = rng(seed);
  const aR = [], aA = [], aY = [], aS = [], aC = [];
  for (let i = 0; i < n; i++) {
    const rr = 8 + 56 * Math.pow(r(), 0.8);
    const arm = (r() * 3) | 0;
    aR.push(rr); aA.push(arm * 2.094 + 2.4 * Math.log(rr / 8) + (r() - 0.5) * 0.9);
    aY.push((r() + r() + r() - 1.5) * (2.5 + rr * 0.1));
    aS.push(16 + r() * 34); aC.push(r());
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(new Array(n * 3).fill(0), 3));
  g.setAttribute('aR', new THREE.Float32BufferAttribute(aR, 1));
  g.setAttribute('aA', new THREE.Float32BufferAttribute(aA, 1));
  g.setAttribute('aY', new THREE.Float32BufferAttribute(aY, 1));
  g.setAttribute('aS', new THREE.Float32BufferAttribute(aS, 1));
  g.setAttribute('aC', new THREE.Float32BufferAttribute(aC, 1));
  const mat = new THREE.ShaderMaterial({
    transparent: true, depthWrite: false,
    uniforms: { uTime: { value: 0 }, uFlash: { value: 0 } },
    vertexShader: `attribute float aR, aA, aY, aS, aC; uniform float uTime; varying float vC; varying float vR;
      void main(){ float a = aA + uTime * (2.4 / (aR * 0.16 + 1.0)); vec3 p = vec3(cos(a) * aR, aY, sin(a) * aR);
        vec4 mv = modelViewMatrix * vec4(p, 1.0); gl_Position = projectionMatrix * mv; gl_PointSize = aS * (300.0 / -mv.z); vC = aC; vR = aR; }`,
    fragmentShader: `uniform float uFlash; varying float vC; varying float vR;
      void main(){ vec2 d = gl_PointCoord - 0.5; float a = smoothstep(0.5, 0.05, length(d));
        vec3 dark = vec3(0.01, 0.016, 0.018), edge = vec3(0.07, 0.115, 0.12);
        vec3 c = mix(dark, edge, vC * smoothstep(10.0, 58.0, vR));
        c += vec3(0.35, 0.45, 0.75) * uFlash * (0.3 + vC) * smoothstep(60.0, 10.0, vR) * 0.2;
        gl_FragColor = vec4(c, a * 0.2); }`,
  });
  const pts = new THREE.Points(g, mat);
  pts.frustumCulled = false;
  return pts;
}
// a cyclone deck: procedural spiral cloud bands on a disc, layered for parallax
function stormDeck(radius, seed, twist, speed, tint) {
  const mat = new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, side: THREE.DoubleSide, fog: false,
    uniforms: { uTime: { value: 0 }, uFlash: { value: 0 }, uSeed: { value: seed }, uTwist: { value: twist }, uSpeed: { value: speed }, uTint: { value: new THREE.Color(tint) }, uOp: { value: 1 } },
    vertexShader: 'varying vec2 vUv; void main(){ vUv = uv * 2.0 - 1.0; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
    fragmentShader: `uniform float uTime, uFlash, uSeed, uTwist, uSpeed, uOp; uniform vec3 uTint; varying vec2 vUv;
      float h(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7)) + uSeed) * 43758.5453); }
      float n(vec2 p){ vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
        return mix(mix(h(i), h(i + vec2(1, 0)), f.x), mix(h(i + vec2(0, 1)), h(i + vec2(1, 1)), f.x), f.y); }
      float fbm(vec2 p){ float s = 0.0, a = 0.5; for (int i = 0; i < 5; i++){ s += a * n(p); p = p * 2.03 + 17.1; a *= 0.5; } return s; }
      void main(){
        float r = length(vUv); if (r > 1.0) discard;
        float ang = uTwist * log(r + 0.06) - uTime * uSpeed * 0.16 / (r + 0.45);
        float c = cos(ang), s = sin(ang); vec2 q = mat2(c, -s, s, c) * vUv;
        float d = fbm(q * 3.2 + 4.0);
        float bands = 0.5 + 0.5 * sin(atan(q.y, q.x) * 3.0 + d * 2.5);
        float dens = smoothstep(0.42, 0.8, d * 0.8 + bands * 0.32);
        dens *= smoothstep(1.0, 0.62, r) * smoothstep(0.07, 0.26, r);
        float lit = smoothstep(0.55, 0.95, fbm(q * 6.0 + 9.0));
        vec3 col = mix(vec3(0.012, 0.02, 0.024), uTint, lit * (0.35 + 0.65 * smoothstep(0.2, 0.9, r)));
        col += vec3(0.5, 0.6, 0.95) * uFlash * (0.05 + lit) * smoothstep(0.95, 0.3, r) * 0.07;
        gl_FragColor = vec4(col, dens * 0.92 * uOp); }`,
  });
  const g = new THREE.PlaneGeometry(radius * 2, radius * 2); g.rotateX(-Math.PI / 2);
  const m = new THREE.Mesh(g, mat); m.frustumCulled = false;
  return m;
}
// a branching lightning bolt: tapered ribbons, main channel plus forks, a white core and a wider blue glow
function lightningBolt(seed, len, segs, wscale = 1) {
  const r = rng(seed), lines = [];
  const walk = (start, n, step, spread, w, depth) => {
    const pts = [start.clone()];
    for (let i = 1; i <= n; i++) {
      const prev = pts[i - 1];
      pts.push(V(prev.x + (r() - 0.5) * spread + (r() - 0.5) * spread * 0.6, prev.y - step * (0.7 + r() * 0.6), prev.z + (r() - 0.5) * 1.5));
      if (depth > 0 && i > 1 && i < n - 1 && r() < 0.55) walk(pts[i], Math.max(4, (n - i) >> 1), step * 0.85, spread * 1.5, w * 0.6, depth - 1);
    }
    lines.push([pts, w]);
  };
  walk(V(0, 0, 0), segs, len / segs, 6.5, 1.3 * wscale, 3);
  const build = (k) => {
    const pos = [], uv = [], idx = [];
    lines.forEach(([pts, w0]) => {
      const base = pos.length / 3;
      pts.forEach((p, i) => { const w = w0 * k * (1 - 0.75 * i / (pts.length - 1)); pos.push(p.x - w, p.y, p.z, p.x + w, p.y, p.z); uv.push(0, 0, 1, 0); if (i) { const q = base + i * 2; idx.push(q - 2, q - 1, q, q - 1, q + 1, q); } });
    });
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2)); g.setIndex(idx);
    return g;
  };
  // light falls off across each ribbon's width, so channel and glow read as light rather than flat bands
  const falloff = (pow) => canvasTex(64, 4, (ctx, w, h) => { for (let x = 0; x < w; x++) { const d = Math.abs(x / (w - 1) - 0.5) * 2, a = Math.pow(Math.max(0, 1 - d), pow); ctx.fillStyle = `rgba(255,255,255,${a})`; ctx.fillRect(x, 0, 1, h); } });
  const mat = (c, mul, pow) => { const m = new THREE.MeshBasicMaterial({ color: c, map: falloff(pow), transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false, fog: false, side: THREE.DoubleSide }); m.color.multiplyScalar(mul); return m; };
  const m = new THREE.Mesh(build(1.3), mat(0xffffff, 3.0, 0.8));
  const glow = new THREE.Mesh(build(3.4), mat(0x7f9aff, 1.6, 2.2)); glow.position.z = -0.3; m.add(glow); m.userData.glow = glow;
  // drawn after the cloud sprites so the channel shows through the storm
  m.renderOrder = 21; glow.renderOrder = 20;
  m.frustumCulled = false; glow.frustumCulled = false;
  return m;
}
// strike envelope: flash, gap, re-strike, afterglow
function boltEnv(t, t0) {
  const d = t - t0;
  if (d < 0 || d > 0.42) return 0;
  if (d < 0.034) return 1;
  if (d < 0.067) return 0.08;
  if (d < 0.134) return 0.95;
  return 0.5 * Math.exp(-(d - 0.134) * 11);
}
function neonSign(text, w, h) {
  const tex = canvasTex(2048, 400, (ctx, cw, ch) => {
    ctx.clearRect(0, 0, cw, ch);
  });
  const draw = (flick) => {
    const { ctx, canvas: c } = tex.userData;
    ctx.clearRect(0, 0, c.width, c.height);
    ctx.font = `800 260px ${FONT_SANS}`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    const letters = [...text];
    const total = ctx.measureText(text).width; let x = c.width / 2 - total / 2;
    letters.forEach((L, i) => {
      const lw = ctx.measureText(L).width;
      const on = !flick.dead && !(i === flick.idx && flick.off);
      ctx.fillStyle = on ? '#FFF1D6' : '#3a3530';
      ctx.shadowColor = on ? 'rgba(255,200,140,.9)' : 'transparent'; ctx.shadowBlur = on ? 30 : 0;
      ctx.fillText(L, x + lw / 2, c.height / 2 + 8); x += lw;
    });
    tex.needsUpdate = true;
  };
  const mat = new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthWrite: false, toneMapped: false, fog: false });
  mat.color.setScalar(1.6);
  const m = new THREE.Mesh(new THREE.PlaneGeometry(w, h), mat);
  m.userData.draw = draw;
  return m;
}

// ---------------------------------------------------------------- control tower
function sigilTexture() {
  return canvasTex(512, 512, (ctx, w, h) => {
    ctx.clearRect(0, 0, w, h); ctx.save(); ctx.scale(w / 100, h / 100);
    ctx.fillStyle = '#15603F'; ctx.beginPath(); ctx.arc(50, 50, 45, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = '#fff'; ctx.lineWidth = 2.4; ctx.beginPath(); ctx.arc(50, 50, 37, 0, Math.PI * 2); ctx.stroke();
    const rr = (x, y, ww, hh, r, c) => { ctx.fillStyle = c; ctx.beginPath(); ctx.roundRect(x, y, ww, hh, r); ctx.fill(); };
    rr(36, 39.5, 24, 4.5, 2, '#fff'); rr(33, 47.5, 30, 5.5, 2.5, '#E8C77B'); rr(36, 56.5, 18, 4.5, 2, '#fff');
    ctx.strokeStyle = '#15603F'; ctx.lineWidth = 1.7; ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    ctx.beginPath(); ctx.moveTo(56.5, 50.6); ctx.lineTo(59, 52.8); ctx.lineTo(64, 47.6); ctx.stroke(); ctx.restore();
  });
}
function buildTower() {
  const g = new THREE.Group();
  const concrete = new THREE.MeshStandardMaterial({ color: 0xd3d8d4, roughness: 0.75 });
  const dark = new THREE.MeshStandardMaterial({ color: 0x1b2324, roughness: 0.5, metalness: 0.4 });
  const shaftTex = canvasTex(256, 1024, (ctx, w, h) => {
    ctx.fillStyle = '#cfd4d0'; ctx.fillRect(0, 0, w, h);
    for (let y = 0; y < h; y += 64) { ctx.fillStyle = 'rgba(0,0,0,.06)'; ctx.fillRect(0, y, w, 2); }
    for (let x = 0; x < w; x += 32) { ctx.fillStyle = 'rgba(0,0,0,.08)'; ctx.fillRect(x, 0, 3, h); }
  });
  const shaft = new THREE.Mesh(new THREE.CylinderGeometry(1.9, 2.8, 34, 32), new THREE.MeshStandardMaterial({ map: shaftTex, roughness: 0.75 })); shaft.position.y = 17; g.add(shaft);
  // a lit stair-window slot running up the shaft
  const slotTex = canvasTex(32, 512, (ctx, w, h) => { ctx.fillStyle = '#000'; ctx.fillRect(0, 0, w, h); for (let y = 8; y < h; y += 24) { ctx.fillStyle = '#ffd9a0'; ctx.fillRect(6, y, 20, 12); } });
  const slot = new THREE.Mesh(new THREE.PlaneGeometry(0.5, 30), new THREE.MeshBasicMaterial({ map: slotTex, toneMapped: false, color: new THREE.Color(1.6, 1.6, 1.6) })); slot.position.set(0, 17, 2.42); slot.rotation.x = -0.027; g.add(slot);
  const band = new THREE.Mesh(new THREE.CylinderGeometry(2.0, 2.0, 0.6, 32), new THREE.MeshStandardMaterial({ color: 0x15603F, roughness: 0.6 })); band.position.y = 27; g.add(band);
  // two-tier cab: service deck, glass cab with mullions, balcony, roof, mast
  const deck = new THREE.Mesh(new THREE.CylinderGeometry(5.4, 3.0, 2.2, 12), concrete); deck.position.y = 34.6; g.add(deck);
  const cabTex = canvasTex(1024, 128, (ctx, w, h) => {
    ctx.fillStyle = '#9ff0c4'; ctx.fillRect(0, 0, w, h);
    const gr = ctx.createLinearGradient(0, 0, 0, h); gr.addColorStop(0, 'rgba(255,255,255,.55)'); gr.addColorStop(0.5, 'rgba(255,255,255,0)'); gr.addColorStop(1, 'rgba(0,40,20,.35)'); ctx.fillStyle = gr; ctx.fillRect(0, 0, w, h);
    for (let x = 0; x < w; x += w / 24) { ctx.fillStyle = '#122019'; ctx.fillRect(x, 0, 7, h); }
    ctx.fillStyle = '#122019'; ctx.fillRect(0, 0, w, 6); ctx.fillRect(0, h - 8, w, 8);
  });
  const glass = new THREE.Mesh(new THREE.CylinderGeometry(5.3, 4.7, 3.4, 24, 1, true), new THREE.MeshStandardMaterial({ map: cabTex, emissive: 0xffffff, emissiveMap: cabTex, emissiveIntensity: 0.9, roughness: 0.15, metalness: 0.3, side: THREE.DoubleSide })); glass.position.y = 37.4; g.add(glass);
  const rail = new THREE.Mesh(new THREE.TorusGeometry(5.9, 0.08, 6, 48), dark); rail.rotation.x = Math.PI / 2; rail.position.y = 36.4; g.add(rail);
  const balc = new THREE.Mesh(new THREE.CylinderGeometry(5.95, 5.95, 0.25, 32), concrete); balc.position.y = 35.75; g.add(balc);
  const roof = new THREE.Mesh(new THREE.CylinderGeometry(3.6, 5.6, 1.1, 24), dark); roof.position.y = 39.6; g.add(roof);
  const mast = new THREE.Mesh(new THREE.CylinderGeometry(0.18, 0.28, 4.5, 8), concrete); mast.position.y = 42.2; g.add(mast);
  const ant = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 6, 6), dark); ant.position.set(2.2, 42.5, 0); g.add(ant);
  const dish = new THREE.Group(); dish.position.y = 46.6; dish.scale.setScalar(1.3); g.add(dish);
  const sigTex = sigilTexture();
  const sig = new THREE.Mesh(new THREE.CircleGeometry(3.2, 48), new THREE.MeshStandardMaterial({ map: sigTex, transparent: true, emissive: 0xffffff, emissiveMap: sigTex, emissiveIntensity: 0.8, roughness: 0.5, side: THREE.DoubleSide }));
  dish.add(sig);
  const sigBack = new THREE.Mesh(new THREE.CylinderGeometry(3.3, 3.3, 0.3, 48), new THREE.MeshStandardMaterial({ color: 0x0f3b29, roughness: 0.4, metalness: 0.5 })); sigBack.rotation.x = Math.PI / 2; sigBack.position.z = -0.2; dish.add(sigBack);
  const lampMat = new THREE.MeshBasicMaterial({ color: new THREE.Color(0xff3030).multiplyScalar(4), toneMapped: false });
  const lamp = new THREE.Mesh(new THREE.SphereGeometry(0.32, 12, 8), lampMat); lamp.position.y = 44.6; g.add(lamp);
  const lamp2 = new THREE.Mesh(new THREE.SphereGeometry(0.2, 10, 8), lampMat); lamp2.position.set(2.2, 45.6, 0); g.add(lamp2);
  // volumetric sweeping beam: soft edges, falloff, drifting dust
  const beamMat = new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide, fog: false,
    uniforms: { uOp: { value: 1 }, uTime: { value: 0 } },
    vertexShader: 'varying vec2 vUv; varying vec3 vN, vV; void main(){ vUv = uv; vec4 mv = modelViewMatrix * vec4(position,1.0); vN = normalize(normalMatrix * normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix * mv; }',
    fragmentShader: `uniform float uOp, uTime; varying vec2 vUv; varying vec3 vN, vV;
      float h(vec2 p){ return fract(sin(dot(p, vec2(41.3, 289.1))) * 15731.7); }
      float n(vec2 p){ vec2 i = floor(p), f = fract(p); f = f*f*(3.0-2.0*f); return mix(mix(h(i), h(i+vec2(1,0)), f.x), mix(h(i+vec2(0,1)), h(i+vec2(1,1)), f.x), f.y); }
      void main(){ float edge = pow(abs(dot(normalize(vN), normalize(vV))), 1.8);
        float along = pow(vUv.y, 2.2);
        float dust = 0.65 + 0.35 * n(vec2(vUv.x * 40.0, vUv.y * 22.0 - uTime * 0.6)) + 0.25 * step(0.985, h(floor(vec2(vUv.x * 160.0, vUv.y * 90.0 - uTime * 3.0))));
        float a = along * edge * dust * 0.85 * uOp; gl_FragColor = vec4(vec3(0.45, 1.0, 0.7) * a, a); }`,
  });
  const beamGeo = new THREE.ConeGeometry(12, 170, 48, 1, true); beamGeo.translate(0, -85, 0); beamGeo.rotateZ(Math.PI / 2);
  const beam = new THREE.Mesh(beamGeo, beamMat); beam.position.y = 37.4; g.add(beam);
  g.userData = { dish, lamp, lamp2, beam, glass };
  return g;
}
// ---------------------------------------------------------------- split-flap board
const FLAP_CHARS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZÅÄÖ0123456789';
function buildBoard() {
  const tex = canvasTex(2048, 1280);
  const mat = new THREE.MeshStandardMaterial({ map: tex, emissive: 0xffffff, emissiveMap: tex, emissiveIntensity: 0.95, roughness: 0.55 });
  const board = new THREE.Mesh(new THREE.PlaneGeometry(10, 6.25), mat);
  const frame = new THREE.Mesh(new THREE.BoxGeometry(10.5, 6.75, 0.4), new THREE.MeshStandardMaterial({ color: 0x15191a, roughness: 0.4, metalness: 0.6 }));
  frame.position.z = -0.22;
  const g = new THREE.Group(); g.add(frame); g.add(board);
  g.userData = { tex };
  return g;
}
// one row spec: {text, start, color(i) -> css}
function drawBoard(tex, spec, t) {
  const { ctx, canvas: c } = tex.userData;
  const COLS = spec.cols || 25, GX = 6, CW = Math.floor((1950 + GX) / COLS) - GX, CH = 110, GY = 22;
  const X0 = (c.width - COLS * (CW + GX) + GX) / 2, Y0 = 200, SC = spec.stagger ?? 0.025, SR = spec.rowStagger ?? 0.05;
  ctx.fillStyle = '#0d1112'; ctx.fillRect(0, 0, c.width, c.height);
  // header
  ctx.fillStyle = '#E8C77B'; ctx.font = `700 70px ${FONT_MONO}`; ctx.textBaseline = 'middle'; ctx.textAlign = 'left';
  ctx.fillText(spec.title, X0, 110);
  ctx.textAlign = 'right'; ctx.fillText(spec.clock || '', c.width - X0, 110);
  ctx.fillStyle = 'rgba(255,255,255,.14)'; ctx.fillRect(X0, 168, c.width - 2 * X0, 3);
  spec.rows.forEach((row, ri) => {
    const y = Y0 + ri * (CH + GY) + 16;
    const target = row.text.padEnd(COLS).slice(0, COLS);
    const from = (row.from || '').padEnd(COLS).slice(0, COLS);
    for (let ci = 0; ci < COLS; ci++) {
      const x = X0 + ci * (CW + GX);
      const t0 = row.start + ci * SC + ri * SR;
      const a = from[ci], b = target[ci];
      const force = row.reflip && ci >= ST && b !== ' ';
      const nFlip = (a === b && !force) ? 0 : 3 + ((ci * 7 + ri * 3) % 4);
      const fd = 0.065;
      let cur = a, nxt = a, p = 1;
      if (nFlip && t >= t0) {
        const k = Math.floor((t - t0) / fd);
        const seq = [a]; for (let j = 0; j < nFlip - 1; j++) seq.push(FLAP_CHARS[(FLAP_CHARS.indexOf(b) + 37 * (j + 1) + ci) % FLAP_CHARS.length]); seq.push(b);
        if (k >= nFlip) { cur = nxt = b; p = 1; } else { cur = seq[k]; nxt = seq[k + 1]; p = ((t - t0) % fd) / fd; }
      }
      const col = row.color ? row.color(ci, t) : '#F2F2EE';
      drawCell(ctx, x, y, CW, CH, cur, nxt, p, col);
    }
  });
  tex.needsUpdate = true;
}
function drawCell(ctx, x, y, w, h, c1, c2, p, color) {
  const half = h / 2;
  const face = (yy, hh) => { ctx.fillStyle = '#1b2022'; ctx.beginPath(); ctx.roundRect(x, yy, w, hh, 5); ctx.fill(); };
  const glyph = (ch, clipY, clipH, scaleY = 1, pivot = y + half) => {
    if (ch === ' ') return;
    ctx.save(); ctx.beginPath(); ctx.rect(x, clipY, w, clipH); ctx.clip();
    ctx.translate(0, pivot); ctx.scale(1, scaleY); ctx.translate(0, -pivot);
    ctx.fillStyle = color; ctx.font = `700 ${Math.min(h * 0.74, w * 1.12)}px ${FONT_MONO}`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText(ch, x + w / 2, y + half + h * 0.04); ctx.restore();
  };
  face(y, h);
  if (p >= 1 || c1 === c2) { glyph(c2, y, h); }
  else {
    // static: new top half, old bottom half
    glyph(c2, y, half); glyph(c1, y + half, half);
    if (p < 0.5) { const s = Math.cos(p * Math.PI); ctx.save(); ctx.fillStyle = '#1b2022'; ctx.fillRect(x, y + half - half * s, w, half * s); ctx.restore(); glyph(c1, y, half, s); ctx.fillStyle = `rgba(0,0,0,${0.35 * (1 - s)})`; ctx.fillRect(x, y + half - half * s, w, half * s); }
    else { const s = -Math.cos(p * Math.PI); ctx.save(); ctx.fillStyle = '#1b2022'; ctx.fillRect(x, y + half, w, half * s); ctx.restore(); glyph(c2, y + half, half * s + 0.5, s); ctx.fillStyle = `rgba(255,255,255,${0.08 * (1 - s)})`; ctx.fillRect(x, y + half, w, half * s); }
  }
  ctx.fillStyle = '#060808'; ctx.fillRect(x, y + half - 1.5, w, 3);
}

// ---------------------------------------------------------------- radar
function buildRadar() {
  const tex = canvasTex(2048, 2048);
  const disc = new THREE.Mesh(new THREE.CircleGeometry(4, 96), new THREE.MeshBasicMaterial({ map: tex, toneMapped: true }));
  disc.rotation.x = -Math.PI / 2;
  const rim = new THREE.Mesh(new THREE.TorusGeometry(4.12, 0.16, 16, 96), new THREE.MeshStandardMaterial({ color: 0x1d2526, roughness: 0.35, metalness: 0.7 }));
  rim.rotation.x = -Math.PI / 2;
  const desk = new THREE.Mesh(new THREE.BoxGeometry(14, 0.6, 12), new THREE.MeshStandardMaterial({ color: 0x101718, roughness: 0.7 }));
  desk.position.y = -0.32;
  const g = new THREE.Group(); g.add(desk); g.add(disc); g.add(rim);
  g.userData = { tex };
  return g;
}
const BLIPS = [
  { id: 'bA', r: 0.56, a: -0.75, kind: 'top' },
  { id: 'bB', r: 0.5, a: 2.75, kind: 's' },
  { id: 'bC', r: 0.66, a: 1.2, kind: 'g' },
  { id: 'bE', r: 0.62, a: 0.5, kind: 'b' },
  { id: 'bD', r: 0.78, a: -2.3, kind: 'n' },
];
// the ice-cream van job drifts off the edge of the radar
const OFFR = lt => 0.42 * E.inCubic(P(lt, 1.35, 2.5));
function drawRadar(tex, lt) {
  const { ctx, canvas: c } = tex.userData;
  const S = c.width, R = S / 2;
  ctx.save();
  const bg = ctx.createRadialGradient(R, R, 0, R, R, R);
  bg.addColorStop(0, '#0f3a29'); bg.addColorStop(1, '#06170f');
  ctx.fillStyle = bg; ctx.fillRect(0, 0, S, S);
  ctx.strokeStyle = 'rgba(116,194,154,.28)'; ctx.lineWidth = 4;
  for (let k = 1; k <= 4; k++) { ctx.beginPath(); ctx.arc(R, R, R * k / 4.2, 0, Math.PI * 2); ctx.stroke(); }
  ctx.beginPath(); ctx.moveTo(R, R * 0.05); ctx.lineTo(R, S - R * 0.05); ctx.moveTo(R * 0.05, R); ctx.lineTo(S - R * 0.05, R); ctx.stroke();
  const sweep = lt * 1.9 - 1.2;
  for (let i = 0; i < 40; i++) {
    const a0 = sweep - i * 0.022;
    ctx.fillStyle = `rgba(116,234,170,${0.16 * (1 - i / 40)})`;
    ctx.beginPath(); ctx.moveTo(R, R); ctx.arc(R, R, R * 0.97, a0 - 0.022, a0); ctx.closePath(); ctx.fill();
  }
  ctx.strokeStyle = 'rgba(160,255,200,.9)'; ctx.lineWidth = 6; ctx.beginPath(); ctx.moveTo(R, R); ctx.lineTo(R + Math.cos(sweep) * R * 0.97, R + Math.sin(sweep) * R * 0.97); ctx.stroke();
  // route to the top match
  const A = BLIPS[0], ax = R + Math.cos(A.a) * A.r * R, ay = R + Math.sin(A.a) * A.r * R;
  const rp = E.inOutCubic(P(lt, 2.2, 2.9));
  if (rp > 0) {
    ctx.setLineDash([26, 22]); ctx.lineDashOffset = -lt * 60; ctx.strokeStyle = 'rgba(232,199,123,.95)'; ctx.lineWidth = 10;
    ctx.beginPath(); ctx.moveTo(R, R); ctx.lineTo(lerp(R, ax, rp), lerp(R, ay, rp)); ctx.stroke(); ctx.setLineDash([]);
  }
  BLIPS.forEach(b => {
    let rr = b.r, al = 1;
    if (b.kind === 'n') { rr = b.r + OFFR(lt); al = 1 - P(lt, 2.2, 2.6); }
    const x = R + Math.cos(b.a) * rr * R, y = R + Math.sin(b.a) * rr * R;
    const lit = 1 - clamp((((sweep - b.a) % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2) / 3.0);
    const appear = P(lt, 0.3 + BLIPS.indexOf(b) * 0.14, 0.6 + BLIPS.indexOf(b) * 0.14);
    const grey = b.kind === 'n' ? P(lt, 1.15, 1.4) : 0;
    const col = b.kind === 'n' ? (grey > 0.5 ? '120,128,136' : '232,199,123') : b.kind === 'top' ? '120,255,170' : '232,199,123';
    ctx.fillStyle = `rgba(${col},${(0.45 + 0.55 * lit) * appear * al})`;
    ctx.beginPath(); ctx.arc(x, y, (b.kind === 'top' ? 34 : 26), 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = `rgba(${col},${0.5 * appear * al})`; ctx.lineWidth = 5; ctx.beginPath(); ctx.arc(x, y, 54 + 20 * Math.sin(lt * 4 + b.a), 0, Math.PI * 2); ctx.stroke();
  });
  { const b = BLIPS[4], sp = P(lt, 1.13, 1.27);
    if (sp > 0 && sp < 1) { const x = R + Math.cos(b.a) * (b.r + OFFR(lt)) * R, y = R + Math.sin(b.a) * (b.r + OFFR(lt)) * R;
      ctx.fillStyle = `rgba(255,255,255,${Math.max(0, 1 - sp * 3)})`; ctx.beginPath(); ctx.arc(x, y, 46, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = `rgba(255,224,160,${1 - sp})`; ctx.lineWidth = 9; ctx.lineCap = 'round';
      for (let k = 0; k < 5; k++) { const a = k * 1.2566 + 0.5; ctx.beginPath(); ctx.moveTo(x + Math.cos(a) * (36 + 70 * sp), y + Math.sin(a) * (36 + 70 * sp)); ctx.lineTo(x + Math.cos(a) * (60 + 120 * sp), y + Math.sin(a) * (60 + 120 * sp)); ctx.stroke(); } } }
  // our plane at the centre
  ctx.translate(R, R); ctx.rotate(-0.75 + Math.PI / 2);
  ctx.fillStyle = '#fff'; ctx.beginPath(); ctx.moveTo(0, -60); ctx.lineTo(40, 44); ctx.lineTo(0, 22); ctx.lineTo(-40, 44); ctx.closePath(); ctx.fill();
  ctx.restore();
  tex.needsUpdate = true;
}
function blipWorld(b, lt, radar) {
  let rr = b.r; if (b.kind === 'n') rr = b.r + OFFR(lt);
  const x = Math.cos(b.a) * rr * 4, z = Math.sin(b.a) * rr * 4;
  return radar.localToWorld(V(x, 0.05, z));
}

// ---------------------------------------------------------------- facade with lit windows (company building)
const FACADE_COLS = 21, FACADE_ROWS = 12;
const PIX = { // 3x5 pixel font
  V: ['101', '101', '101', '101', '010'], I: ['111', '010', '010', '010', '111'], S: ['111', '100', '111', '001', '111'],
  Ö: ['101', '000', '111', '101', '111'], K: ['101', '110', '100', '110', '101'], E: ['111', '100', '110', '100', '111'], R: ['110', '101', '110', '101', '101'],
};
function facadeMask(word, row0) {
  const set = new Set(); let col = Math.floor((FACADE_COLS - (word.length * 4 - 1)) / 2);
  for (const L of word) { const g = PIX[L]; g.forEach((line, y) => [...line].forEach((ch, x) => { if (ch === '1') set.add(`${col + x},${row0 + y}`); })); col += 4; }
  return set;
}
const MASK_VI = facadeMask('VI', 0), MASK_SOKER = facadeMask('SÖKER', 6);
function buildCompany() {
  const tex = canvasTex(1024, 640);
  const sideTx = windowTextures(555, 6, 12, 0.2);
  const side = new THREE.MeshStandardMaterial({ map: sideTx.base, emissive: 0xffffff, emissiveMap: sideTx.emis, emissiveIntensity: 1.0, roughness: 0.9 });
  const front = new THREE.MeshStandardMaterial({ map: tex, emissive: 0xffffff, emissiveMap: tex, emissiveIntensity: 1.0, roughness: 0.85 });
  const roof = new THREE.MeshStandardMaterial({ color: 0x141f20, roughness: 0.95 });
  const geo = new THREE.BoxGeometry(34, 21, 12); geo.translate(0, 10.5, 0);
  const b = new THREE.Mesh(geo, [side, side, roof, roof, front, side]);
  const signTex = canvasTex(2048, 200, (ctx, w, h) => {
    ctx.fillStyle = '#0c1213'; ctx.fillRect(0, 0, w, h);
    ctx.fillStyle = '#F4EEDC'; ctx.font = `700 120px ${FONT_SANS}`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText('TROLLHÄTTANS DATASERVICE', w / 2, h / 2 + 6);
  });
  const sign = new THREE.Mesh(new THREE.BoxGeometry(30, 2.6, 0.5), [roof, roof, roof, roof, new THREE.MeshStandardMaterial({ map: signTex, emissive: 0xffffff, emissiveMap: signTex, emissiveIntensity: 0.9 }), roof]);
  sign.position.set(0, 22.6, 5.6);
  const beacon = new THREE.Group();
  const ring = new THREE.Mesh(new THREE.TorusGeometry(2.2, 0.35, 12, 48), new THREE.MeshBasicMaterial({ color: 0x45d68a, toneMapped: false }));
  ring.rotation.x = -Math.PI / 2; beacon.add(ring);
  const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.2, 0.2, 3, 8), new THREE.MeshStandardMaterial({ color: 0x9aa6a2 })); pole.position.y = 1.5; beacon.add(pole);
  const bulb = new THREE.Mesh(new THREE.SphereGeometry(0.7, 16, 12), new THREE.MeshBasicMaterial({ color: 0x45d68a, toneMapped: false })); bulb.position.y = 3.2; beacon.add(bulb);
  beacon.position.set(-10, 21, -3);
  const shock = new THREE.Mesh(new THREE.TorusGeometry(1, 0.09, 8, 64), new THREE.MeshBasicMaterial({ color: new THREE.Color(0x45d68a).multiplyScalar(3), transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false }));
  shock.position.set(-10, 27.6, -3);
  const g = new THREE.Group(); g.add(b); g.add(sign); g.add(beacon); g.add(shock);
  g.userData = { tex, beacon, bulb, ring, shock };
  return g;
}
function drawFacade(tex, mode, lt) {
  const { ctx, canvas: c } = tex.userData;
  const cw = c.width / FACADE_COLS, ch = c.height / FACADE_ROWS;
  ctx.fillStyle = '#1c2a2c'; ctx.fillRect(0, 0, c.width, c.height);
  const r = rng(91);
  for (let y = 0; y < FACADE_ROWS; y++) for (let x = 0; x < FACADE_COLS; x++) {
    const base = r() < 0.18;
    const key = `${x},${y}`;
    const word = MASK_VI.has(key) || MASK_SOKER.has(key);
    const wr = y < 5 ? y : y - 1, t0 = Math.floor(wr * 6 / 10) * 0.09;
    let on = base ? 0.35 : 0;
    if (mode > 0 && word) on = Math.max(on, P(mode, t0, t0 + 0.12));
    if (mode > 0 && !word) on = on * (1 - 0.7 * P(mode, 0, 0.3));
    const wx = x * cw + cw * 0.14, wy = y * ch + ch * 0.16, ww = cw * 0.72, wh = ch * 0.62;
    ctx.fillStyle = '#2b3a3c'; ctx.fillRect(wx - 3, wy - 3, ww + 6, wh + 6);
    ctx.fillStyle = '#0b1214'; ctx.fillRect(wx, wy, ww, wh);
    if (on > 0) { ctx.fillStyle = word ? `rgba(255,206,120,${on})` : `rgba(255,226,180,${on * 0.8})`; ctx.fillRect(wx, wy, ww, wh); }
    ctx.fillStyle = 'rgba(10,16,18,.85)'; ctx.fillRect(wx + ww / 2 - 1.5, wy, 3, wh); ctx.fillStyle = 'rgba(0,0,0,.35)'; ctx.fillRect(wx, wy, ww, 4);
  }
  tex.needsUpdate = true;
}

// ---------------------------------------------------------------- contrails for the holding pattern
function buildTrails(n, seg) {
  const vc = n * (seg + 1) * 2, pos = new Float32Array(vc * 3), col = new Float32Array(vc * 3), idx = [];
  for (let i = 0; i < n; i++) for (let k = 0; k < seg; k++) { const a = (i * (seg + 1) + k) * 2; idx.push(a, a + 1, a + 2, a + 1, a + 3, a + 2); }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(pos, 3)); g.setAttribute('color', new THREE.BufferAttribute(col, 3)); g.setIndex(idx);
  const m = new THREE.Mesh(g, new THREE.MeshBasicMaterial({ vertexColors: true, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide, fog: false, toneMapped: false }));
  m.frustumCulled = false; m.userData = { n, seg };
  return m;
}
function updateTrails(t, op) {
  const { n, seg } = trails.userData, pos = trails.geometry.attributes.position.array, col = trails.geometry.attributes.color.array;
  trails.visible = op > 0.001;
  if (!trails.visible) return;
  for (let i = 0; i < n; i++) {
    const p = holdPlanes[i];
    for (let k = 0; k <= seg; k++) {
      const a = holdPos(p, t - k * 0.07), b = holdPos(p, t - k * 0.07 - 0.02);
      const side = a.clone().sub(b).cross(V(0, 1, 0)).normalize().multiplyScalar(0.55 * (1 - k / seg) + 0.08);
      const o = (i * (seg + 1) + k) * 6, f = Math.pow(1 - k / seg, 1.6) * 0.45 * op;
      pos[o] = a.x - side.x; pos[o + 1] = a.y - side.y; pos[o + 2] = a.z - side.z;
      pos[o + 3] = a.x + side.x; pos[o + 4] = a.y + side.y; pos[o + 5] = a.z + side.z;
      for (let c = 0; c < 6; c++) col[o + c] = f * (c % 3 === 2 ? 1.0 : 0.92);
    }
  }
  trails.geometry.attributes.position.needsUpdate = true; trails.geometry.attributes.color.needsUpdate = true;
}
// cumulonimbus mass around the cyclone: a ring wall plus a flattened anvil above
function buildStormPuffs() {
  const r = rng(61), pts = [];
  for (let i = 0; i < 1050; i++) { const a = r() * Math.PI * 2, rad = 40 + r() * 70; pts.push([Math.cos(a) * rad, -14 + r() * 26 + (rad - 40) * 0.12, Math.sin(a) * rad * 0.7 - 10, 26 + r() * 30, r()]); }
  for (let i = 0; i < 620; i++) { const a = r() * Math.PI * 2, rad = Math.sqrt(r()) * 130; pts.push([Math.cos(a) * rad, 26 + r() * 9, Math.sin(a) * rad * 0.6 - 20, 30 + r() * 34, r()]); }
  pts.sort((a, b) => a[2] - b[2]);
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pts.flatMap(q => [q[0], q[1], q[2]]), 3));
  g.setAttribute('aS', new THREE.Float32BufferAttribute(pts.map(q => q[3]), 1));
  g.setAttribute('aC', new THREE.Float32BufferAttribute(pts.map(q => q[4]), 1));
  const m = new THREE.Points(g, new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, fog: false,
    uniforms: { uTex: { value: puffTexture() }, uFlash: { value: 0 } },
    vertexShader: 'attribute float aS, aC; varying float vC, vY; void main(){ vec4 mv = modelViewMatrix * vec4(position,1.0); vC = aC; vY = position.y; gl_Position = projectionMatrix * mv; gl_PointSize = aS * (900.0 / -mv.z); }',
    fragmentShader: `uniform sampler2D uTex; uniform float uFlash; varying float vC, vY;
      void main(){ vec4 t = texture2D(uTex, gl_PointCoord); float top = smoothstep(0.9, 0.1, gl_PointCoord.y);
        vec3 c = mix(vec3(0.016, 0.026, 0.03), vec3(0.11, 0.16, 0.17), top * (0.4 + 0.6 * vC));
        c += vec3(0.5, 0.58, 0.9) * uFlash * (0.1 + 0.6 * (1.0 - top)) * (0.3 + vC) * 0.12;
        gl_FragColor = vec4(c, min(t.a * 2.0, 0.9)); }`,
  }));
  m.frustumCulled = false;
  return m;
}
// rain streaks that follow the storm camera
function buildRain() {
  const n = 1400, im = new THREE.InstancedMesh(new THREE.PlaneGeometry(0.035, 2.4), new THREE.MeshBasicMaterial({ color: 0x9fb2c0, transparent: true, opacity: 0.32, depthWrite: false, fog: false, blending: THREE.AdditiveBlending }), n);
  const r = rng(73); im.userData.seeds = Array.from({ length: n }, () => [r() * 2 - 1, r(), r() * 2 - 1, 0.7 + r() * 0.6]);
  im.frustumCulled = false; im.visible = false;
  return im;
}
function updateRain(cam, t) {
  rain.visible = true;
  const f = new THREE.Vector3(); cam.getWorldDirection(f);
  const right = f.clone().cross(V(0, 1, 0)).normalize();
  rain.userData.seeds.forEach((q, i) => {
    const depth = 3 + q[3] * 22, y = ((q[1] - t * 1.6 * q[3]) % 1 + 1) % 1;
    const p = cam.position.clone().addScaledVector(f, depth).addScaledVector(right, q[0] * depth * 0.55).add(V(0, (y - 0.5) * depth * 1.2, 0));
    _m.makeRotationFromEuler(new THREE.Euler(0, Math.atan2(f.x, f.z), 0.12)); _m.setPosition(p);
    rain.setMatrixAt(i, _m);
  });
  rain.instanceMatrix.needsUpdate = true;
}
// soft puff texture shared by dust and clouds
function puffTexture() {
  return canvasTex(128, 128, (ctx, w, h) => {
    const n = rng(12); ctx.clearRect(0, 0, w, h);
    for (let i = 0; i < 26; i++) { const x = 34 + n() * 60, y = 34 + n() * 60, r = 16 + n() * 26; const g = ctx.createRadialGradient(x, y, 0, x, y, r); g.addColorStop(0, 'rgba(255,255,255,.35)'); g.addColorStop(1, 'rgba(255,255,255,0)'); ctx.fillStyle = g; ctx.fillRect(0, 0, w, h); }
  });
}
// ---------------------------------------------------------------- scenes
const scenes = {};
let stormDecks = [], bolts = [], cityCars, trails, dust, popLight, cloudPts, stormPuffs, rain, endPlane;
let player, holdPlanes = [], swarm = [], cityTower, company, vortex, sign, radar, board, runwayPlane, luggage, luggageTex, skyPlane, cloudField;

function lightRig(scene, { amb = 0x223338, ambI = 0.6, sun = 0xbfd4ff, sunI = 0.6, sunPos = V(60, 120, 80), hemiTop = 0x2a4a5a, hemiBot = 0x0a1210, hemiI = 0.5, shadow = false } = {}) {
  scene.add(new THREE.AmbientLight(amb, ambI));
  scene.add(new THREE.HemisphereLight(hemiTop, hemiBot, hemiI));
  const d = new THREE.DirectionalLight(sun, sunI); d.position.copy(sunPos);
  if (shadow) { d.castShadow = true; d.shadow.mapSize.set(1024, 1024); const s = d.shadow.camera; s.left = -20; s.right = 20; s.top = 20; s.bottom = -20; s.near = 1; s.far = 400; }
  scene.add(d);
  return d;
}

function buildScenes() {
  // ---- city at night (S2, S3, S4, F3)
  const city = new THREE.Scene();
  city.background = new THREE.Color(0x061210);
  city.fog = new THREE.Fog(0x0e2827, 90, 470);
  city.add(skyDome(0x040b10, 0x0e2827, 0x2a5f58));
  city.add(stars(900, 3));
  lightRig(city, { amb: 0x1a2a2e, ambI: 0.7, sun: 0x9fb8ff, sunI: 0.35, hemiI: 0.45 });
  cityCars = buildCity(city, { seed: 11, x0: -306, x1: 306, z0: -470, z1: 106, step: 12, clear: [[0, -60, 9, 9], [-12, -38, 14, 26], [45, 0, 26, 14], [62, 38, 30, 40]] }).cars;
  cityTower = buildTower(); cityTower.position.set(0, 0, -60); city.add(cityTower);
  company = buildCompany(); company.position.set(45, 0, 0); city.add(company);
  vortex = buildVortex(6000, 21); vortex.position.set(0, 58, -230); vortex.rotation.x = 0.95; city.add(vortex);
  [[78, 1.3, 2.2, 0.9, 0x31474c, -5], [66, 2.3, 2.6, 1.4, 0x40585c, 1], [52, 4.1, 3.0, 2.0, 0x52696b, 6]].forEach(([rad, sd, tw, sp, tint, y]) => {
    const d = stormDeck(rad, sd, tw, sp, tint); d.position.y = y; vortex.add(d); stormDecks.push(d);
  });
  [[10, 47, -182, 7, 48, 0.32], [30, 56, -200, 19, 50, 1]].forEach(([x, y, z, sd, len, ws]) => { const b = lightningBolt(sd, len, 14, ws); b.position.set(x, y, z); city.add(b); bolts.push(b); });
  stormPuffs = buildStormPuffs(); stormPuffs.position.copy(vortex.position); city.add(stormPuffs);
  rain = buildRain(); city.add(rain);
  const core = new THREE.Mesh(new THREE.SphereGeometry(10, 24, 16), new THREE.MeshBasicMaterial({ color: 0x020405, fog: false })); core.position.copy(vortex.position); core.scale.set(1, 0.55, 1); city.add(core);
  sign = neonSign('VI ÅTERKOMMER', 58, 11.3); sign.position.set(0, 60, -195); sign.renderOrder = 10; city.add(sign);
  const flashLight = new THREE.PointLight(0x9fb4ff, 0, 150, 1.2); flashLight.position.set(10, 95, -205); city.add(flashLight);
  city.userData.flash = flashLight;
  player = makePlane({ word: 'ANSÖKAN', num: '37' }, 1.0); city.add(player);
  trails = buildTrails(48, 22); city.add(trails);
  const r = rng(5);
  for (let i = 0; i < 48; i++) { const bs = 3.4 + r() * 1.0, p = makePlane(null, bs, 0xf0ece2); Object.assign(p.userData, { base: bs, phase: r() * Math.PI * 2, rad: 70 + r() * 16, y: (r() - 0.5) * 18, sp: 0.32 + r() * 0.1 }); holdPlanes.push(p); city.add(p); }
  for (let i = 0; i < 26; i++) { const p = makePlane(null, 3.0 + r() * 0.8, 0xf0ece2); Object.assign(p.userData, { phase: r() * Math.PI * 2, rad: 23 + r() * 8, y: 5 + r() * 12, sp: 1.6 + r() * 0.8, out: V(r() - 0.5, 0.3 + r() * 0.5, r() - 0.5).normalize() }); swarm.push(p); city.add(p); }
  scenes.city = city;

  // ---- terminal board (S1, F2, C2)
  const term = new THREE.Scene();
  term.background = new THREE.Color(0x070b0b);
  term.fog = new THREE.Fog(0x070b0b, 20, 70);
  lightRig(term, { amb: 0x2a3335, ambI: 0.5, sun: 0xffe6c4, sunI: 0.5, sunPos: V(3, 10, 12), hemiTop: 0x4a5a5a, hemiBot: 0x101414, hemiI: 0.35 });
  const wall = new THREE.Mesh(new THREE.PlaneGeometry(80, 40), new THREE.MeshStandardMaterial({ color: 0x1a2122, roughness: 0.9 })); wall.position.set(0, 0, -0.6); term.add(wall);
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(80, 80), new THREE.MeshStandardMaterial({ color: 0x0d1213, roughness: 0.35, metalness: 0.2 })); floor.rotation.x = -Math.PI / 2; floor.position.y = -7; term.add(floor);
  for (let i = -3; i <= 3; i++) { const strip = new THREE.Mesh(new THREE.BoxGeometry(0.5, 0.15, 30), new THREE.MeshBasicMaterial({ color: 0xfff1d8, toneMapped: false })); strip.position.set(i * 6, 9, 10); term.add(strip); }
  board = buildBoard(); term.add(board);
  endPlane = makePlane({ word: 'ANSÖKAN', num: '37' }, 2.2); endPlane.visible = false; term.add(endPlane);
  const spot = new THREE.SpotLight(0xffefd6, 40, 40, 0.6, 0.6, 1.2); spot.position.set(0, 8, 10); spot.target = board; term.add(spot);
  scenes.term = term;

  // ---- radar console (F1)
  const rad = new THREE.Scene();
  rad.background = new THREE.Color(0x030807);
  lightRig(rad, { amb: 0x203030, ambI: 0.4, sun: 0xcfe8dc, sunI: 0.3, hemiI: 0.3 });
  radar = buildRadar(); rad.add(radar);
  const glow = new THREE.PointLight(0x2E8B63, 30, 30, 1.5); glow.position.set(0, 3, 0); rad.add(glow);
  scenes.radar = rad;

  // ---- runway at dawn (F4)
  const run = new THREE.Scene();
  run.background = new THREE.Color(0x1a2a3a);
  run.fog = new THREE.Fog(0x8a7470, 140, 760);
  run.add(skyDome(0x1a2f55, 0xd8a07e, 0xffb98a, V(-0.5, 0.05, 1), 0.85));
  const sunL = lightRig(run, { amb: 0x5a6070, ambI: 0.65, sun: 0xffc48a, sunI: 2.6, sunPos: V(-75, 13, 150), hemiTop: 0x9ab0d0, hemiBot: 0x3a3228, hemiI: 0.75, shadow: true });
  sunL.shadow.mapSize.set(2048, 2048); Object.assign(sunL.shadow.camera, { left: -14, right: 14, top: 14, bottom: -14 }); sunL.shadow.camera.updateProjectionMatrix(); sunL.shadow.bias = -0.0005;
  const grassTex = canvasTex(512, 512, (ctx, w, h) => { ctx.fillStyle = '#3c4c3c'; ctx.fillRect(0, 0, w, h); const n = rng(8); for (let i = 0; i < 9000; i++) { ctx.fillStyle = n() < 0.5 ? 'rgba(80,100,70,.18)' : 'rgba(10,16,12,.25)'; ctx.fillRect(n() * w, n() * h, 2, 3); } });
  grassTex.wrapS = grassTex.wrapT = THREE.RepeatWrapping; grassTex.repeat.set(80, 80);
  const grass = new THREE.Mesh(new THREE.PlaneGeometry(1400, 1400), new THREE.MeshStandardMaterial({ map: grassTex, roughness: 1 })); grass.rotation.x = -Math.PI / 2; grass.receiveShadow = true; run.add(grass);
  const rwTex = canvasTex(512, 2048, (ctx, w, h) => {
    ctx.fillStyle = '#545a5c'; ctx.fillRect(0, 0, w, h);
    const n = rng(21); for (let i = 0; i < 26000; i++) { const v = n(); ctx.fillStyle = v < 0.5 ? `rgba(255,255,255,${n() * 0.07})` : `rgba(0,0,0,${n() * 0.12})`; ctx.fillRect(n() * w, n() * h, 2, 2); }
    // rubber deposits from landings
    for (let i = 0; i < 40; i++) { ctx.fillStyle = `rgba(10,10,10,${0.05 + n() * 0.08})`; const x = w * (0.32 + n() * 0.36); ctx.fillRect(x, n() * h, 10 + n() * 18, 60 + n() * 220); }
    ctx.fillStyle = '#ece8dc'; for (let y = 0; y < h; y += 170) ctx.fillRect(w / 2 - 7, y, 14, 96);
    ctx.fillRect(18, 0, 10, h); ctx.fillRect(w - 28, 0, 10, h);
  });
  rwTex.wrapT = THREE.RepeatWrapping; rwTex.repeat.set(1, 9); rwTex.anisotropy = 8;
  const runway = new THREE.Mesh(new THREE.PlaneGeometry(14, 900), new THREE.MeshStandardMaterial({ map: rwTex, roughness: 0.55, metalness: 0.05 }));
  runway.rotation.x = -Math.PI / 2; runway.position.set(0, 0.02, -150); runway.receiveShadow = true; run.add(runway);
  const lightsGeo = new THREE.SphereGeometry(0.14, 10, 8), lm = new THREE.MeshBasicMaterial({ color: new THREE.Color(0xffe2a8).multiplyScalar(5), toneMapped: false });
  const edge = new THREE.InstancedMesh(lightsGeo, lm, 240);
  let ei = 0;
  for (let i = 0; i < 120; i++) for (let s2 = 0; s2 < 2; s2++) { const z = 300 - i * 7.5; if (s2 && z === -7.5) continue; _m.makeTranslation((s2 ? 7.4 : -7.4), 0.22, z); edge.setMatrixAt(ei++, _m); }
  edge.count = ei; run.add(edge);
  popLight = new THREE.Mesh(lightsGeo, new THREE.MeshBasicMaterial({ color: new THREE.Color(0xffe2a8).multiplyScalar(5), toneMapped: false })); popLight.position.set(7.4, 0.22, -7.5); run.add(popLight);
  buildCity(run, { seed: 33, x0: -200, x1: 200, z0: -520, z1: -380, step: 16, clear: [], ground: false, traffic: false });
  buildCity(run, { seed: 47, x0: -260, x1: 260, z0: 330, z1: 420, step: 18, clear: [[-30, 360, 30, 40]], ground: false, traffic: false });
  const term2 = new THREE.Mesh(new THREE.BoxGeometry(110, 9, 22), new THREE.MeshStandardMaterial({ color: 0x2a2f36, emissive: 0xffd9a0, emissiveIntensity: 0.06, roughness: 0.8 }));
  term2.position.set(-90, 4.5, 270); run.add(term2);
  const runTower = buildTower(); runTower.position.set(-135, 0, 290); runTower.scale.setScalar(0.9); runTower.userData.beam.visible = false; run.add(runTower);
  runwayPlane = makePlane({ word: 'ANSÖKAN', num: '37' }, 2.4, 0xffffff, true, true); run.add(runwayPlane);
  luggageTex = canvasTex(1024, 512);
  luggage = new THREE.Mesh(new THREE.BoxGeometry(1.4, 1, 1), new THREE.MeshStandardMaterial({ map: luggageTex, roughness: 0.8 }));
  luggage.castShadow = true; runwayPlane.add(luggage);
  // dust puff kicked up by the failed take-off
  const dn = 60, dpos = new Float32Array(dn * 3), dr = rng(31);
  dust = new THREE.Points(new THREE.BufferGeometry(), new THREE.PointsMaterial({ map: puffTexture(), color: 0xc8b49a, size: 2.4, transparent: true, depthWrite: false, opacity: 0 }));
  dust.geometry.setAttribute('position', new THREE.BufferAttribute(dpos, 3));
  dust.userData.dirs = Array.from({ length: dn }, () => V(dr() - 0.5, dr() * 0.35, (dr() - 0.5) * 0.6).normalize().multiplyScalar(0.6 + dr() * 0.9));
  dust.frustumCulled = false; run.add(dust);
  scenes.run = run;

  // ---- above the clouds at sunrise (C1)
  const sky = new THREE.Scene();
  sky.background = new THREE.Color(0x24406a);
  sky.fog = new THREE.Fog(0xd9a88e, 160, 900);
  sky.add(skyDome(0x1a3a6a, 0xe8b08a, 0xffb27a, V(0, 0.08, -1), 0.8));
  lightRig(sky, { amb: 0x7a7f9a, ambI: 0.6, sun: 0xffd9b0, sunI: 2.0, sunPos: V(0, 20, -200), hemiTop: 0xa8c0e8, hemiBot: 0x6a5a50, hemiI: 0.7 });
  // a sea of soft cloud puffs, sun-lit from above and behind
  const cn = 5200, cpos = [], csz = [], cr = rng(77);
  const puffs = [];
  for (let i = 0; i < cn; i++) { const x = (cr() - 0.5) * 700, z = 60 - cr() * 760, y = -14 - cr() * 9 + Math.sin(x * 0.02) * 3 + Math.cos(z * 0.015) * 3; puffs.push([x, y, z, 14 + cr() * 26]); }
  puffs.sort((a, b) => a[2] - b[2]).forEach(q => { cpos.push(q[0], q[1], q[2]); csz.push(q[3]); });
  const cg = new THREE.BufferGeometry(); cg.setAttribute('position', new THREE.Float32BufferAttribute(cpos, 3)); cg.setAttribute('aS', new THREE.Float32BufferAttribute(csz, 1));
  cloudPts = new THREE.Points(cg, new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, fog: false,
    uniforms: { uTex: { value: puffTexture() }, uSun: { value: new THREE.Color(0xffe2c4) }, uShade: { value: new THREE.Color(0x7d7f9c) }, uFog: { value: new THREE.Color(0xe2b494) } },
    vertexShader: 'attribute float aS; varying float vD; void main(){ vec4 mv = modelViewMatrix * vec4(position,1.0); vD = -mv.z; gl_Position = projectionMatrix * mv; gl_PointSize = aS * (700.0 / -mv.z); }',
    fragmentShader: `uniform sampler2D uTex; uniform vec3 uSun, uShade, uFog; varying float vD;
      void main(){ vec4 t = texture2D(uTex, gl_PointCoord); float top = smoothstep(0.85, 0.15, gl_PointCoord.y);
        vec3 c = mix(uShade, uSun, top * 0.85 + 0.1); c = mix(c, uFog, smoothstep(120.0, 700.0, vD));
        gl_FragColor = vec4(c, min(t.a * 2.2, 0.96)); }`,
  }));
  cloudPts.frustumCulled = false; sky.add(cloudPts);
  skyPlane = makePlane({ word: 'ANSÖKAN', num: '37' }, 1.6); sky.add(skyPlane);
  scenes.sky = sky;
}

// ---------------------------------------------------------------- shots
const cams = {};
function cam(fov) { const c = new THREE.PerspectiveCamera(fov, W / H, 0.1, 2000); return c; }
function lookFrom(c, pos, target, roll = 0) { c.position.copy(pos); c.up.set(Math.sin(roll), Math.cos(roll), 0); c.lookAt(target); }
// chase camera in the subject's own frame: back / right / up offsets, look ahead along the heading
function chase(c, p, d, back, right, up, ahead, lift = 0, roll = 0) {
  const f = d.clone().normalize(), r = f.clone().cross(V(0, 1, 0)).normalize(), u = r.clone().cross(f).normalize();
  lookFrom(c, p.clone().addScaledVector(f, -back).addScaledVector(r, right).addScaledVector(u, up), p.clone().addScaledVector(f, ahead).addScaledVector(u, lift), roll);
}

// player flight path over the city (S2 → S3)
const FLIGHT = new THREE.CatmullRomCurve3([V(-8, 34, 70), V(-2, 38, 30), V(4, 40, -10), V(2, 44, -60), V(-6, 50, -110), V(0, 56, -145)]);
function holdPos(p, t) {
  const u = p.userData, a = u.phase + t * u.sp;
  return V(Math.cos(a) * u.rad, u.y + Math.sin(a * 2) * 2, Math.sin(a) * u.rad * 0.55).add(vortex.position);
}

// ---------------------------------------------------------------- timeline (seconds)
const TL = {
  open: [0, 2.8], lift: [2.8, 6.0], storm: [6.0, 12.0], tower: [12.0, 16.0],
  radar: [16.0, 21.0], follow: [21.0, 26.0], watch: [26.0, 31.0], runway: [31.0, 37.0],
  sky: [37.0, 37.9], arrive: [37.9, 39.2], end: [39.2, 44.0],
  thunder: 8.3, slam: 32.72, logo: 14.0, chime: 8.6, crickets: 9.95,
};
const DUR = TL.end[1];
const ST = 15; // first status column on the boards

const OPEN_ROWS = ['30 SKOGSÅS     ', '31 TORPVIKEN   ', '32 ÅLUNDA      ', '33 VÅRDDATA    ', '34 NORDVIK     ', '35 HALLÉN&BERG ', '36 ALVESTA     '];

const SHOTS = [];
function shot(name, [a, b], scene, setup) { SHOTS.push({ name, a, b, scene, setup }); }
const shake = (t, t0, amp) => { const k = Math.exp(-Math.max(0, t - t0) * 14) * (t >= t0 ? 1 : 0); return V(Math.sin(t * 91) * amp * k, Math.cos(t * 77) * amp * k, 0); };

function defineShots() {
  // S1 — a wall of VÄNTAR; only 37 departs
  cams.s1 = cam(36);
  shot('board-open', TL.open, 'term', (t) => {
    endPlane.visible = false;
    const amber = '#E2B04E';
    const spec = { title: 'AVGÅNGAR', clock: '', rows: OPEN_ROWS.map((n, i) => ({ from: n + 'VÄNTAR', text: n + 'VÄNTAR', start: -0.18 + i * 0.17, reflip: true, color: (ci) => ci < ST ? '#F2F2EE' : amber })) };
    spec.rows.push({ from: '37 LINDHOLMEN  VÄNTAR', text: '37 LINDHOLMEN  AVGÅR', start: -0.55 + 0.2, color: (ci, tt) => ci < ST ? '#FFFFFF' : (tt < 0.75 ? amber : (Math.floor(tt * 3.2) % 2 ? '#8CF0B4' : '#2f6b4a')) });
    drawBoard(board.userData.tex, spec, t);
    const u = E.inOutSine(P(t, 0, 2.8));
    lookFrom(cams.s1, V(lerp(-2.3, -1.7, u), lerp(-0.15, -0.3, u), lerp(16.6, 17.3, u)), V(lerp(-0.62, -0.56, u), lerp(-0.3, -0.38, u), 0), lerp(0.02, 0.008, u));
    return cams.s1;
  });
  // S2 — Ansökan 37 takes off over the city
  cams.s2 = cam(46);
  shot('lift', TL.lift, 'city', (t) => {
    const lt = t - TL.lift[0];
    const p = FLIGHT.getPointAt(clamp(lt / 5.0 * 0.62)), d = FLIGHT.getTangentAt(clamp(lt / 5.0 * 0.62));
    orient(player, p, d, Math.sin(lt * 1.6) * 0.16);
    player.position.y += Math.sin(lt * 2.3) * 0.2;
    player.scale.setScalar(1);
    const w = E.inOutSine(P(lt, 0.6, 3.2));
    chase(cams.s2, p, d, lerp(2.4, 5.2, w), lerp(0.25, 0.6, w), lerp(1.5, 1.35, w), lerp(0.4, 3.0, w), lerp(-0.2, 0.35, w), -0.04);
    common(t);
    return cams.s2;
  });
  // S3 — into the storm, then the wide holding-pattern shot
  cams.s3 = cam(48);
  shot('vortex', TL.storm, 'city', (t) => {
    const lt = t - TL.storm[0];
    common(t);
    cams.s3.fov = lt < 1.6 ? 48 : 42; cams.s3.updateProjectionMatrix();
    if (lt < 1.6) {
      const u = (3.2 + lt) / 5.0 * 0.62;
      const p = FLIGHT.getPointAt(clamp(u + lt * 0.06)), d = FLIGHT.getTangentAt(clamp(u));
      orient(player, p, d, Math.sin(lt * 1.6) * 0.16);
      player.scale.setScalar(1);
      chase(cams.s3, p, d, 5.4 + lt * 4.0, 0.6, 0.9 + lt * 0.7, 30, 3.5 + lt * 2.6, 0);
    } else {
      const j = E.inOutCubic(P(lt, 1.6, 3.2));
      const ring = holdPos({ userData: { phase: 1.9, rad: 72, y: 2, sp: 0.34 } }, t);
      const start = FLIGHT.getPointAt(0.95);
      const pos = start.clone().lerp(ring, j);
      const ring2 = holdPos({ userData: { phase: 1.9, rad: 72, y: 2, sp: 0.34 } }, t + 0.05);
      orient(player, pos, j < 1 ? ring.clone().sub(start).lerp(ring2.clone().sub(ring), j) : ring2.clone().sub(ring), 0.35 * j);
      player.scale.setScalar(lerp(1, 2.6, j));
      const w = E.inOutSine(P(lt, 1.6, 6.0));
      const sh = shake(t, TL.thunder, 0.9);
      lookFrom(cams.s3, V(lerp(36, 58, w), lerp(56, 60, w), lerp(-110, -92, w)).add(sh), V(-4, lerp(60, 62, w), -230), 0);
    }
    updateRain(cams.s3, t);
    return cams.s3;
  });
  // S4 — the tower rises; the holding pattern becomes an orderly queue; 37 breaks free
  cams.s4 = cam(52);
  shot('tower', TL.tower, 'city', (t) => {
    const lt = t - TL.tower[0];
    common(t);
    cityTower.position.y = lerp(-46, 0, E.outCubic(P(lt, 0.0, 1.4)));
    const u = E.inOutSine(P(lt, 0, 4));
    lookFrom(cams.s4, V(lerp(-22, -15, u), lerp(6, 9, u), lerp(-18, -13, u)).add(shake(t, TL.tower[0], 0.25)), V(0, lerp(27, 36, u), -60), 0.04);
    const QA = V(70, 74, -170), QB = V(-26, 44, -70), qd = QB.clone().sub(QA);
    holdPlanes.forEach((p, i) => {
      const k = E.inOutCubic(P(lt, 0.5 + (i % 12) * 0.07, 2.8 + (i % 12) * 0.07));
      const f = (i / holdPlanes.length + (t - TL.tower[0]) * 0.035) % 1;
      const a = holdPos(p, t), target = QA.clone().addScaledVector(qd, f);
      const pos = a.clone().lerp(target, k);
      orient(p, pos, k < 0.999 ? target.clone().sub(a).lerp(qd, k) : qd, 0);
      p.scale.setScalar(lerp(p.userData.base, 1.7, k));
    });
    // 37 sweeps through the foreground, label up
    const pk = E.inOutSine(P(lt, 1.0, 3.6));
    const pp = V(lerp(-31, -6, pk), lerp(17.5, 21, pk), lerp(-24, -34, pk));
    orient(player, pp, V(1, 0.1, -0.35), -0.55);
    player.scale.setScalar(1.6);
    return cams.s4;
  });
  // F1 — the radar
  cams.f1 = cam(42);
  shot('radar', TL.radar, 'radar', (t) => {
    const lt = t - TL.radar[0];
    drawRadar(radar.userData.tex, lt);
    const a = lerp(-0.25, 0.1, E.inOutSine(P(lt, 0, 5)));
    const dist = lerp(13.2, 12.3, E.inOutSine(P(lt, 0, 5)));
    lookFrom(cams.f1, V(Math.sin(a) * 3.0, dist, Math.cos(a) * 3.0 - 0.4), V(0, 0, -2.1), 0);
    return cams.f1;
  });
  // F2 — the board again, now tracking every application
  cams.f2 = cam(38);
  shot('board-follow', TL.follow, 'term', (t) => {
    endPlane.visible = false;
    const lt = t - TL.follow[0];
    const done = lt >= 3.6;
    const ok = (ci) => ci < ST ? '#F2F2EE' : '#7FE0A8';
    const r36 = done
      ? { from: '36 ALVESTA     FÖRSENAD 14 DGR', text: '36 ALVESTA     UPPFÖLJD', start: 3.6 - ST * 0.012 - 3 * 0.03, color: (ci, tt) => ci < ST ? '#F2F2EE' : (tt > 3.85 ? '#7FE0A8' : '#FF9A4D') }
      : { from: '', text: '36 ALVESTA     FÖRSENAD 14 DGR', start: 0.1, color: (ci, tt) => ci < ST ? '#F2F2EE' : ci > 23 ? '#FFB27A' : (tt > 0.9 && Math.floor(tt * 2.5) % 2 ? '#7a4a26' : '#FF9A4D') };
    const spec = { title: 'DINA ANSÖKNINGAR', clock: '10:02', cols: 30, stagger: 0.012, rowStagger: 0.03, rows: [
      { from: '', text: '33 VÅRDDATA    INTERVJU', start: 0.0, color: ok },
      { from: '', text: '34 NORDVIK     ERBJUDANDE', start: 0.03, color: ok },
      { from: '', text: '35 HALLÉN&BERG SKICKAD', start: 0.06 },
      r36,
      { from: '', text: '37 LINDHOLMEN  SKICKAD', start: 0.12 },
    ] };
    drawBoard(board.userData.tex, spec, lt);
    const u = E.inOutSine(P(lt, 0, 5));
    lookFrom(cams.f2, V(lerp(-3.0, -1.4, u), lerp(0.6, 0.1, u), lerp(20.4, 19.6, u)), V(lerp(0.25, 0.1, u), 1.0, 0), 0.0);
    return cams.f2;
  });
  // F3 — circling the careers page, then watching the company
  cams.f3 = cam(46);
  shot('watch', TL.watch, 'city', (t) => {
    const lt = t - TL.watch[0];
    common(t);
    cityTower.position.y = -60;
    const B = company.position;
    const watched = lt >= 1.6;
    const disperse = E.inCubic(P(lt, 1.9, 3.0));
    swarm.forEach(p => {
      const u = p.userData, a = u.phase + t * u.sp;
      const base = V(Math.cos(a) * u.rad, u.y + Math.sin(a * 3) * 1.5, Math.sin(a) * u.rad * 0.7).add(B);
      const pos = base.clone().add(u.out.clone().multiplyScalar(disperse * 120));
      const vel = V(-Math.sin(a), 0, Math.cos(a) * 0.7).add(u.out.clone().multiplyScalar(disperse * 3));
      orient(p, pos, vel, 0.5);
      p.scale.setScalar(2.0);
      p.visible = disperse < 0.98;
    });
    const be = company.userData;
    const bin = E.outBack(P(lt, 1.6, 2.05));
    // big on landing and for the ping, then it settles low on the roof, clear of the copy
    be.beacon.scale.setScalar(watched ? Math.max(0.001, bin * lerp(2.0, 0.75, E.inOutSine(P(lt, 2.3, 2.7)))) : 0.001);
    const sk = P(lt, 1.62, 2.35); be.shock.scale.setScalar(1 + 5.5 * E.outCubic(sk)); be.shock.material.opacity = sk > 0 && sk < 1 ? (1 - sk) : 0;
    be.shock.quaternion.copy(cams.f3.quaternion);
    be.bulb.material.color.setHSL(0.41, 0.65, 0.45 + 0.25 * (Math.sin(t * 6) * 0.5 + 0.5));
    drawFacade(be.tex, lt >= 2.6 ? lt - 2.6 : 0, lt);
    const ease = E.inOutSine(P(lt, 0, 3.6));
    const ang = lerp(0.6, 0.0, ease), dd = lerp(74, 62, ease);
    lookFrom(cams.f3, V(B.x + Math.sin(ang) * dd, lerp(26, 14, ease), B.z + Math.cos(ang) * dd), V(B.x, lerp(17, 13, ease), B.z), 0);
    return cams.f3;
  });
  // F4 — overweight on the runway, then the profile text slims down and it flies
  cams.f4 = cam(38);
  shot('runway', TL.runway, 'run', (t) => {
    const lt = t - TL.runway[0];
    const trimmed = lt >= 4.2;
    const sc = trimmed ? lerp(1, 0.42, E.outBack(P(lt, 4.2, 4.6))) : 1;
    // the overloaded case sags the plane; the bounce squashes it
    const squash = 1 - 0.12 * Math.exp(-Math.max(0, t - TL.slam) * 9) * (t >= TL.slam ? 1 : 0);
    luggage.scale.set(sc * 0.5, sc * 0.5 * squash, sc * 0.5);
    luggage.position.set(0, 0.12 + 0.5 * sc * 0.5 * 0.5 * squash, 0.02);
    const { ctx, canvas: lc } = luggageTex.userData;
    ctx.fillStyle = '#b98a52'; ctx.fillRect(0, 0, lc.width, lc.height);
    const cr = rng(5); for (let i = 0; i < 1400; i++) { ctx.fillStyle = cr() < 0.5 ? 'rgba(90,60,30,.10)' : 'rgba(255,240,210,.08)'; ctx.fillRect(cr() * lc.width, cr() * lc.height, 3, 1 + cr() * 2); }
    for (let x = 0; x < lc.width; x += 9) { ctx.fillStyle = 'rgba(80,55,25,.05)'; ctx.fillRect(x, 0, 2, lc.height); }
    ctx.fillStyle = 'rgba(214,190,140,.92)'; ctx.fillRect(0, lc.height * 0.44, lc.width, 54);
    ctx.fillStyle = 'rgba(255,255,255,.18)'; ctx.fillRect(0, lc.height * 0.44, lc.width, 6);
    const eg = ctx.createLinearGradient(0, 0, lc.width, 0); eg.addColorStop(0, 'rgba(50,30,10,.45)'); eg.addColorStop(0.06, 'rgba(50,30,10,0)'); eg.addColorStop(0.94, 'rgba(50,30,10,0)'); eg.addColorStop(1, 'rgba(50,30,10,.45)');
    ctx.fillStyle = eg; ctx.fillRect(0, 0, lc.width, lc.height);
    ctx.fillStyle = '#fff8ea'; ctx.fillRect(140, 90, 744, 300); ctx.strokeStyle = '#BE1B1B'; ctx.lineWidth = 8; ctx.strokeRect(140, 90, 744, 300);
    ctx.save(); ctx.translate(60, 300); ctx.rotate(-0.16); ctx.fillStyle = '#F4EEDC'; ctx.beginPath(); ctx.roundRect(0, 0, 130, 170, 10); ctx.fill(); ctx.strokeStyle = '#15603F'; ctx.lineWidth = 6; ctx.stroke();
    ctx.fillStyle = '#15603F'; ctx.font = `800 30px ${FONT_SANS}`; ctx.textAlign = 'center'; ctx.fillText('ANSÖKAN', 65, 50); ctx.font = `800 84px ${FONT_SANS}`; ctx.fillText('37', 65, 130); ctx.restore();
    ctx.fillStyle = '#0C1A2E'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.font = `800 96px ${FONT_SANS}`; ctx.fillText('PROFILTEXT', 512, 180);
    ctx.font = `700 110px ${FONT_MONO}`; ctx.fillStyle = trimmed ? '#15603F' : '#BE1B1B'; ctx.fillText(trimmed ? '86 ORD' : '224 ORD', 512, 310);
    luggageTex.needsUpdate = true;
    const ts = TL.slam - TL.runway[0];
    let z = 0, pitch = 0, y = 0.35, roll = 0;
    if (lt < 4.2) {
      const strain = P(lt, 0.0, 0.9);
      roll = Math.sin(lt * 38) * 0.035 * strain * (lt < ts ? 1 : 0);
      z = -6.6 * E.inCubic(P(lt, 0.85, ts));
      const up = E.outCubic(P(lt, 1.2, 1.38)), down = E.inCubic(P(lt, 1.62, ts));
      pitch = 0.4 * up * (1 - down);
      y = 0.35 + 0.42 * up * (1 - down);
      if (lt >= ts) { const b = P(lt, ts, ts + 0.42); y = 0.35 + 0.16 * Math.abs(Math.sin(b * Math.PI * 2)) * (1 - b); pitch = -0.05 * (1 - b) * Math.sin(b * Math.PI * 2); z = -6.6; }
    } else {
      const k = P(lt, 4.5, 6.0);
      z = -6.6 - 64 * E.inCubic(k);
      y = 0.35 + 24 * Math.pow(Math.max(0, k - 0.3) / 0.7, 2);
      pitch = 0.34 * E.outCubic(P(lt, 4.9, 5.4));
    }
    runwayPlane.position.set(0, y, z);
    runwayPlane.rotation.set(pitch, Math.PI, roll);
    runwayPlane.children[0].castShadow = y < 2.0;
    luggage.castShadow = y < 2.0;
    popLight.visible = !(t > TL.slam + 0.04 && !(t > TL.slam + 0.12 && t < TL.slam + 0.17));
    // dust
    const dp = dust.geometry.attributes.position.array, age = t - TL.slam;
    dust.material.opacity = age > 0 ? 0.55 * Math.exp(-age * 2.2) : 0;
    dust.userData.dirs.forEach((d, i) => { const s2 = age > 0 ? 1 - Math.exp(-age * 3.2) : 0; dp[i * 3] = d.x * s2 * 3.2; dp[i * 3 + 1] = 0.25 + d.y * s2 * 1.6; dp[i * 3 + 2] = -6.6 + d.z * s2 * 3.2; });
    dust.geometry.attributes.position.needsUpdate = true;
    // camera: low and ahead of the plane, looking back down the runway towards the terminal and the tower
    const sh = shake(t, TL.slam, 0.1);
    const fz = lt < ts ? z : -6.6;
    if (lt < 4.5) {
      const c = E.inOutSine(P(lt, 0, 4.5));
      lookFrom(cams.f4, V(lerp(4.6, 4.2, c), lerp(2.2, 2.0, c), fz - lerp(6.6, 6.1, c)).add(sh), V(-0.4, 1.05, fz + 1.4), 0.0);
    } else {
      // the plane comes at us and passes just overhead
      const f = E.inOutSine(P(lt, 4.5, 5.6));
      const cam = V(2.6, 1.0, -13.6);
      lookFrom(cams.f4, cam, V(lerp(-0.2, 0, f), lerp(0.7, y + 0.4, f), lerp(-5.7, z, f)), 0.0);
    }
    return cams.f4;
  });
  // C1 — above the clouds: a barrel roll of relief
  cams.c1 = cam(46);
  shot('sky', TL.sky, 'sky', (t) => {
    const lt = t - TL.sky[0];
    const dir = V(0.25, 0.06, -1);
    const p = dir.clone().multiplyScalar(lt * 18).add(V(-4, 8 + Math.sin(lt * 1.8) * 0.4, 0));
    const roll = Math.PI * 2 * E.inOutCubic(P(lt, 0.08, 0.62));
    orient(skyPlane, p, dir, -0.1 + roll);
    const w = E.inOutSine(P(lt, 0, 0.9));
    chase(cams.c1, p, dir, lerp(5.6, 6.4, w), lerp(-0.6, 0.4, w), lerp(1.4, 2.0, w), 2.0, 0.4, 0);
    return cams.c1;
  });
  // C2 — arrivals: on time; then the board becomes the end card
  cams.c2 = cam(38);
  shot('board-final', [TL.arrive[0], TL.end[1]], 'term', (t) => {
    const lt = t - TL.arrive[0], le = t - TL.end[0];
    const spec = le >= 0 ? { title: 'JOBBLIGGAREN', clock: '', rows: [
      { from: '37 LINDHOLMEN  INTERVJU', text: '    VÄLKOMMEN OMBORD', start: -0.1, color: () => '#E8C77B' },
      { from: '               I TID', text: '', start: -0.3, color: () => '#7FE0A8' },
      { from: '', text: '30 SKOGSÅS     VÄNTAR', start: 0.1, color: () => '#7d7258' },
      { from: '', text: '31 TORPVIKEN   VÄNTAR', start: 0.15, color: () => '#7d7258' },
      { from: '', text: '37 LINDHOLMEN  INTERVJU', start: 0.2, color: (ci) => ci < ST ? '#F2F2EE' : '#7FE0A8' },
      { from: '', text: '38 DIN ANSÖKAN AVGÅR', start: 0.3, color: (ci, tt) => ci < ST ? '#FFFFFF' : (tt > 3.6 || Math.floor(tt * 3.2) % 2 ? '#9CFFC2' : '#4f9a72') },
    ] } : { title: 'ANKOMSTER', clock: '13:55', rows: [
      { from: '37 LINDHOLMEN  SKICKAD', text: '37 LINDHOLMEN  INTERVJU', start: -0.3, color: (ci) => ci < ST ? '#F2F2EE' : '#7FE0A8' },
      { from: '', text: '               I TID', start: 0.25, color: () => '#7FE0A8' },
    ] };
    drawBoard(board.userData.tex, spec, le >= 0 ? le : lt);
    const u = E.inOutSine(P(t, TL.arrive[0], TL.end[0] + 0.9)), push = E.inOutSine(P(t, TL.end[0], TL.end[1]));
    lookFrom(cams.c2, V(lerp(0.2, 0, u), lerp(-0.4, -2.8, u), lerp(18.4, 22.6, u) - push * 0.9), V(0, lerp(-0.4, -3.15, u), 0), 0);
    // 37 flies past the welcome board
    const fk = P(le, 0.9, 2.9);
    endPlane.visible = fk > 0 && fk < 1;
    if (endPlane.visible) { const ex = lerp(-8.5, 8.5, E.inOutSine(fk)); orient(endPlane, V(ex, -4.2 + Math.sin(fk * Math.PI) * 0.5, 2.6), V(1, 0.08 * Math.cos(fk * Math.PI), 0.15), -0.25 + Math.sin(fk * Math.PI * 2) * 0.12); }
    return cams.c2;
  });
}
// shared city animation (vortex spin, sign flicker, holding planes, lightning, traffic)
function common(t) {
  const inWatch = t >= TL.watch[0] && t < TL.watch[1];
  holdPlanes.forEach(p => { p.visible = !inWatch; });
  swarm.forEach(p => { p.visible = inWatch; });
  player.visible = !inWatch;
  vortex.material.uniforms.uTime.value = t;
  const fl = (x, c) => Math.exp(-Math.pow((x - c) / 0.05, 2));
  const strike = boltEnv(t, TL.thunder), far = boltEnv(t, 5.2) * 0.5, late = boltEnv(t, 11.45) * 0.55;
  const flash = Math.max(strike, far, late);
  vortex.material.uniforms.uFlash.value = flash;
  stormDecks.forEach(d => { d.material.uniforms.uTime.value = t; d.material.uniforms.uFlash.value = flash; });
  stormPuffs.material.uniforms.uFlash.value = flash;
  stormPuffs.rotation.y = t * 0.02;
  rain.visible = false;
  bolts[0].material.opacity = strike; bolts[0].userData.glow.material.opacity = strike * 0.3;
  bolts[1].material.opacity = Math.max(far, late) * 1.6; bolts[1].userData.glow.material.opacity = Math.max(far, late) * 0.3;
  scenes.city.userData.flash.intensity = flash * 6000;
  // the dead letter keeps failing; in the silence it gives up completely for a moment
  const dim = t >= TL.logo ? 0.12 : t >= TL.tower[0] ? 0.3 : 1;
  sign.material.opacity = dim;
  const hire = TL.watch[0] + 2.6, dead = t >= hire && t < TL.watch[1] && !(t > hire + 0.06 && t < hire + 0.12);
  sign.visible = !dead;
  sign.userData.draw({ idx: 9, off: (Math.floor(t * 7.3) % 5 === 0) || (t > 10.7 && t < 11.15), dead });
  if (t < TL.tower[0]) holdPlanes.forEach(p => { const a = holdPos(p, t), b = holdPos(p, t + 0.05); orient(p, a, b.clone().sub(a), 0.35); p.scale.setScalar(p.userData.base); });
  const blink = Math.floor(t * 1.4) % 2 === 0;
  holdPlanes.concat(swarm, [player]).forEach((p, i) => { if (p.userData.strobe) p.userData.strobe.visible = ((t * 1.1 + i * 0.137) % 1) < 0.12; });
  updateTrails(t, t < TL.storm[0] ? 0 : t < TL.tower[0] ? 1 : 1 - P(t, TL.tower[0], TL.tower[0] + 0.6));
  updateTraffic(cityCars, t);
  if (t < TL.tower[0] - 0.05) cityTower.position.y = -60;
  const tw = cityTower.userData;
  tw.dish.rotation.y = -0.38 + (t - TL.logo) * 0.9;
  tw.beam.rotation.y = t * 0.9;
  tw.beam.material.uniforms.uTime.value = t;
  tw.beam.material.uniforms.uOp.value = t > TL.tower[0] - 0.1 ? 0.6 : 0;
  tw.lamp.visible = blink; tw.lamp2.visible = !blink;
}

// ---------------------------------------------------------------- overlays (HTML)
function lines(root, t, t0, stagger = 0.08, dur = 0.8) {
  root.querySelectorAll('.ln>span').forEach((s, i) => { const x = E.outExpo(P(t, t0 + i * stagger, t0 + i * stagger + dur)); s.style.transform = `translateY(${(1 - x) * 110}%)`; });
}
function show(id, t, a, b, how = 'fade', dist = 26) {
  const el = $(id);
  const i = E.outExpo(P(t, a, a + 0.5)), o = E.inCubic(P(t, b - 0.22, b));
  const v = t >= a && t < b ? i * (1 - o) : 0;
  el.style.opacity = v;
  if (how === 'up') el.style.transform = `translateY(${(1 - i) * dist - o * 16}px)`;
  if (how === 'wipe') el.style.clipPath = `inset(0 ${(1 - i) * 100}% 0 0)`;
  return v;
}
function chapterHead(t, n, [a, b], subA) {
  show('k' + n, t, a + 0.02, b, 'up', 12);
  const h = $('h' + n); h.style.opacity = t >= a && t < b ? 1 - E.inCubic(P(t, b - 0.22, b)) : 0; lines(h, t, a + 0.06, 0.08, 0.75);
  if ($('s' + n)) show('s' + n, t, subA, b, 'up', 18);
}
function overlays(t) {
  $('scrimTop').style.opacity = (t >= TL.radar[0] && t < TL.sky[0]) ? 1 : 0;
  $('scrimBot').style.opacity = 0.85;
  $('bug').style.opacity = (t >= TL.radar[0] + 0.4 && t < TL.sky[0] - 0.2) ? 0.85 : 0;
  // act 1
  const day = $('cDay'); day.style.opacity = t < TL.open[1] ? 1 - E.inCubic(P(t, TL.open[1] - 0.2, TL.open[1])) : 0;
  show('cLift', t, 3.0, 5.9, 'wipe'); lines($('cLift'), t, 3.0, 0, 0.6);
  show('cHope', t, 3.75, 5.9, 'wipe');
  show('paWait', t, TL.chime + 0.15, TL.storm[1] - 0.12, 'up', 30);
  show('sdhCrick', t, TL.crickets + 0.1, TL.storm[1] - 0.12);
  const c0 = TL.tower[0] + 0.25, c1 = TL.logo - 0.1;
  const ct = $('cTurn'); ct.style.opacity = t >= c0 && t < c1 ? 1 - E.inCubic(P(t, c1 - 0.25, c1)) : 0; lines(ct, t, c0, 0.1, 0.7);
  const lk = $('lock'); lk.style.opacity = t >= TL.logo && t < TL.tower[1] ? 1 - E.inCubic(P(t, TL.tower[1] - 0.2, TL.tower[1])) : 0; lines(lk, t, TL.logo, 0.18, 0.8);
  // chapters
  chapterHead(t, 1, TL.radar, TL.radar[0] + 2.6);
  chapterHead(t, 2, TL.follow);
  chapterHead(t, 3, TL.watch, TL.watch[0] + 2.7);
  chapterHead(t, 4, TL.runway, TL.runway[0] + 4.5);
  // F1 radar labels follow their blips
  const lt1 = t - TL.radar[0];
  BLIPS.forEach((b, i) => {
    const el = $(b.id);
    if (t < TL.radar[0] || t >= TL.radar[1]) { el.style.opacity = 0; return; }
    const w = blipWorld(b, lt1, radar).project(cams.f1);
    const x = (w.x * 0.5 + 0.5) * W, y = (-w.y * 0.5 + 0.5) * H;
    const a = E.outExpo(P(lt1, 0.45 + i * 0.14, 0.9 + i * 0.14)) * (b.kind === 'n' ? 1 - P(lt1, 2.2, 2.6) : 1) * (1 - E.inCubic(P(t, TL.radar[1] - 0.22, TL.radar[1])));
    el.style.opacity = a;
    el.style.left = x + 'px'; el.style.top = (y - 34) + 'px';
    if (b.kind === 'n') $('bDx').style.opacity = E.outExpo(P(lt1, 1.25, 1.6));
  });
  show('why', t, TL.radar[0] + 2.6, TL.radar[1], 'up', 24);
  show('paLate', t, TL.follow[0] + 1.25, TL.follow[0] + 4.4, 'up', 30);
  show('notif', t, TL.watch[0] + 3.2, TL.watch[1], 'up', 30);
  show('paLoad', t, TL.slam + 0.13, TL.slam + 1.33, 'up', 30);
  show('check', t, TL.slam + 1.23, TL.slam + 3.23, 'up', 30);
  ['ck1', 'ck2', 'ck3'].forEach((id, i) => { const v = E.outExpo(P(t, TL.slam + 1.38 + i * 0.25, TL.slam + 1.73 + i * 0.25)); $(id).style.opacity = v; $(id).style.transform = `translateX(${(1 - v) * 24}px)`; });
  const en = $('endc'); en.style.opacity = t >= TL.end[0] + 0.2 ? E.outExpo(P(t, TL.end[0] + 0.2, TL.end[0] + 0.8)) : 0; lines(en, t, TL.end[0] + 0.2, 0, 0.7);
  ['e2', 'eUrl', 'e3'].forEach((id, i) => { const v = E.outExpo(P(t, TL.end[0] + 0.55 + i * 0.25, TL.end[0] + 1.05 + i * 0.25)); $(id).style.opacity = v; $(id).style.transform = `translateY(${(1 - v) * 18}px)`; });
}

// ---------------------------------------------------------------- master
let frame = 0;
function seek(t) {
  const s = SHOTS.find(x => t >= x.a && t < x.b) || SHOTS[SHOTS.length - 1];
  const camera = s.setup(t);
  renderPass.scene = scenes[s.scene];
  renderPass.camera = camera;
  bloom.strength = s.scene === 'city' ? 0.75 : s.scene === 'radar' ? 0.9 : s.scene === 'term' ? 0.5 : s.scene === 'run' ? 0.45 : 0.3;
  renderer.toneMappingExposure = s.scene === 'term' ? 1.15 : 1.0;
  finalPass.uniforms.uFrame.value = (frame++ % 997);
  finalPass.uniforms.uFade.value = 0;
  finalPass.uniforms.uFlash.value = Math.exp(-Math.pow((t - TL.logo) / 0.06, 2)) * 0.45 + Math.exp(-Math.pow((t - TL.tower[0] + 0.02) / 0.035, 2)) * 0.35;
  composer.render();
  overlays(t);
}

// ---------------------------------------------------------------- audio cue list
const EV = [];
const ev = (t, type, extra = {}) => EV.push(Object.assign({ t: +t.toFixed(4), type }, extra));
function buildEvents() {
  ev(0, 'terminalAmbience', { dur: 2.8 });
  OPEN_ROWS.forEach((_, i) => ev(Math.max(0, 0.03 + i * 0.17), 'flapRow', { n: 6, dur: 0.38 }));
  ev(0.33, 'flapRow', { n: 6, dur: 0.38 });
  ev(TL.lift[0], 'whooshUp'); ev(TL.lift[0], 'windFlight', { dur: 5.4 });
  ev(5.2, 'thunderSoft');
  ev(6.2, 'dramaSwell');
  ev(TL.thunder, 'thunder');
  ev(TL.chime, 'chime');
  ev(TL.crickets, 'crickets', { dur: TL.storm[1] - TL.crickets });
  ev(11.45, 'thunderFar');
  ev(TL.storm[1] - 0.45, 'radarSweep');
  ev(TL.tower[0], 'towerRise', { dur: 1.4 });
  ev(TL.logo, 'logoHit');
  ev(TL.radar[0], 'cut');
  BLIPS.forEach((_, i) => ev(TL.radar[0] + 0.3 + i * 0.14, 'blip'));
  ev(TL.radar[0] + 1.15, 'fizzle');
  ev(TL.radar[0] + 2.2, 'routeLine');
  ev(TL.radar[0] + 2.6, 'popCard');
  ev(TL.follow[0], 'cut');
  for (let r = 0; r < 5; r++) ev(TL.follow[0] + 0.05 + r * 0.08, 'flapRow', { n: 14, dur: 0.5 });
  ev(TL.follow[0] + 1.2, 'paSoft');
  ev(TL.follow[0] + 3.6, 'flapRow', { n: 10, dur: 0.45 });
  ev(TL.follow[0] + 3.95, 'okTick');
  ev(TL.watch[0], 'cut'); ev(TL.watch[0], 'swarm', { dur: 2.0 });
  ev(TL.watch[0] + 1.6, 'beacon'); ev(TL.watch[0] + 1.9, 'disperse');
  ev(TL.watch[0] + 2.6, 'facadeLights');
  ev(TL.watch[0] + 3.2, 'ding');
  ev(TL.runway[0], 'cut');
  ev(TL.runway[0] + 0.05, 'strain', { dur: 1.6 });
  ev(TL.runway[0] + 0.85, 'rollStart', { dur: 0.85 });
  ev(TL.slam, 'thud'); ev(TL.slam + 0.05, 'pop');
  ev(TL.slam + 0.1, 'paSoft');
  [1.38, 1.63].forEach(x => ev(TL.slam + x, 'okTick')); ev(TL.slam + 1.88, 'failTick');
  ev(TL.runway[0] + 4.2, 'shrink');
  ev(TL.runway[0] + 4.5, 'takeoff', { dur: 1.5 });
  ev(TL.sky[0], 'cut'); ev(TL.sky[0] + 0.05, 'rollWhoosh');
  ev(TL.arrive[0], 'cut');
  ev(TL.arrive[0] + 0.05, 'flapRow', { n: 10, dur: 0.5 }); ev(TL.arrive[0] + 0.3, 'flapRow', { n: 6, dur: 0.35 }); ev(TL.arrive[0] + 0.65, 'okTick');
  ev(TL.end[0], 'flapRow', { n: 18, dur: 0.8 }); ev(TL.end[0] + 0.12, 'flapRow', { n: 14, dur: 0.7 });
  ev(TL.end[0] + 0.3, 'finalHit');
  EV.sort((a, b) => a.t - b.t);
}

// ---------------------------------------------------------------- boot
(async () => {
  await Promise.all(['800 60px SS3', '700 60px SS3', '600 40px SS3', '700 60px JBM', '500 30px JBM'].map(f => document.fonts.load(f)));
  await document.fonts.ready;
  buildScenes();
  defineShots();
  buildEvents();
  window.seek = seek;
  window.DURATION = DUR;
  window.EVENTS = EV;
  window.CLOCK = { BPM, BEAT, BAR };
  window.hideForCover = () => {};
  window.__dbg = (t) => { seek(t); const s = SHOTS.find(x => t >= x.a && t < x.b); const c = renderPass.camera; const w = player.getWorldPosition(new THREE.Vector3()); const pr = w.clone().project(c); return { shot: s.name, player: w.toArray().map(v => +v.toFixed(2)), screen: [((pr.x * 0.5 + 0.5) * W) | 0, ((-pr.y * 0.5 + 0.5) * H) | 0, +pr.z.toFixed(3)], cam: c.position.toArray().map(v => +v.toFixed(2)), visible: player.visible, scale: player.scale.x }; };
  seek(0.0);
  window.READY = true;
})();
