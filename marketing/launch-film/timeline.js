// Deterministic timeline: window.seek(t) renders the frame at t seconds.
// Every timing constant lives here; the audio mix reads window.EVENTS so picture
// and sound share one clock.
(() => {
'use strict';

// ---------- clock ----------
const BPM = 120, BEAT = 60 / BPM, BAR = 4 * BEAT;
const MORPH = 3.8;          // spreadsheet leaves, hold music tape-stops
const M0 = 5.6;             // music bar 1 = the logo's check mark
const bar = n => M0 + (n - 1) * BAR;
// chapters of unequal length (5 / 6 / 7 / 6 s), all on the beat grid
const C1 = M0 + BAR, C2 = C1 + 5, C3 = C2 + 6, C4 = C3 + 7, TR = C4 + 6, EN = TR + 4, FIN = EN + 4;
const SC = {
  cold:  [0, MORPH + 0.45],
  logo:  [MORPH, C1 + 0.35],
  f1:    [C1 - 0.25, C2],
  f2:    [C2, C3],
  f3:    [C3, C4],
  f4:    [C4, TR],
  trash: [TR, EN + 0.35],
  end:   [EN - 0.25, FIN + 1.8],
};
const DURATION = SC.end[1];

// ---------- math ----------
const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
const P = (t, a, b) => clamp((t - a) / (b - a));
const lerp = (a, b, x) => a + (b - a) * x;
const E = {
  lin: x => x,
  outExpo: x => (x >= 1 ? 1 : 1 - Math.pow(2, -10 * x)),
  outCubic: x => 1 - Math.pow(1 - x, 3),
  inCubic: x => x * x * x,
  inOutCubic: x => (x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2),
  inOutQuint: x => (x < 0.5 ? 16 * x ** 5 : 1 - Math.pow(-2 * x + 2, 5) / 2),
  outBack: x => { const c1 = 1.5, c3 = c1 + 1; return 1 + c3 * Math.pow(x - 1, 3) + c1 * Math.pow(x - 1, 2); },
};
const $ = id => document.getElementById(id);
const hex = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
const mix = (a, b, x) => { const A = hex(a), B = hex(b); return `rgb(${A.map((v, i) => Math.round(lerp(v, B[i], x))).join(',')})`; };

// ---------- helpers ----------
function lines(root, t, t0, stagger = 0.075, dur = 0.8) {
  root.querySelectorAll('.ln>span').forEach((s, i) => {
    const x = E.outExpo(P(t, t0 + i * stagger, t0 + i * stagger + dur));
    s.style.transform = `translateY(${(1 - x) * 108}%)`;
  });
}
function fadeUp(el, t, t0, dur = 0.7, dist = 28, ease = E.outExpo) {
  const x = ease(P(t, t0, t0 + dur));
  el.style.opacity = x;
  el.style.transform = `translateY(${(1 - x) * dist}px)`;
  return x;
}
function pop(el, t, t0, dur = 0.35) {
  const x = P(t, t0, t0 + dur);
  el.style.opacity = clamp(x * 3);
  el.style.transform = `scale(${lerp(0.6, 1, E.outBack(x))})`;
}
function exitUp(el, t, t0, dur = 0.3) {
  const x = E.inCubic(P(t, t0, t0 + dur));
  el.style.opacity = 1 - x;
  el.style.transform = `translateY(${-x * 34}px)`;
}
function typed(el, text, t, t0, t1) {
  const n = Math.round(text.length * P(t, t0, t1));
  el.textContent = text.slice(0, n);
  return n;
}
function center(el) { const r = el.getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; }
function posIn(el, anc) {
  let x = 0, y = 0, e = el;
  while (e && e !== anc) { x += e.offsetLeft; y += e.offsetTop; e = e.offsetParent; }
  return { x, y, w: el.offsetWidth, h: el.offsetHeight };
}
const PW = 936, PH = 700;
function camera(panel, keys, t) {
  const cam = panel.querySelector('.cam');
  let [, s, cx, cy] = keys[0];
  for (let i = 0; i < keys.length - 1; i++) {
    const a = keys[i], b = keys[i + 1];
    if (t >= a[0] && t < b[0]) {
      const u = E.inOutQuint(P(t, a[0], b[0]));
      s = lerp(a[1], b[1], u); cx = lerp(a[2], b[2], u); cy = lerp(a[3], b[3], u);
      break;
    }
    if (t >= b[0]) { s = b[1]; cx = b[2]; cy = b[3]; }
  }
  const tx = clamp(PW / 2 - cx * s, PW - PW * s, 0), ty = clamp(PH / 2 - cy * s, PH - PH * s, 0);
  cam.style.transform = `translate(${tx}px,${ty}px) scale(${s})`;
}
function pathAt(keys, t) {
  if (t <= keys[0][0]) return [keys[0][1], keys[0][2]];
  for (let i = 0; i < keys.length - 1; i++) {
    const [t0, x0, y0] = keys[i], [t1, x1, y1] = keys[i + 1];
    if (t <= t1) {
      const u = E.inOutCubic(P(t, t0, t1));
      const arc = Math.sin(Math.PI * u) * Math.min(40, Math.hypot(x1 - x0, y1 - y0) * 0.08);
      const nx = -(y1 - y0), ny = x1 - x0, L = Math.hypot(nx, ny) || 1;
      return [lerp(x0, x1, u) + nx / L * arc, lerp(y0, y1, u) + ny / L * arc];
    }
  }
  const k = keys[keys.length - 1];
  return [k[1], k[2]];
}
let cursorSet = false, rippleSet = false, crossSet = false, cross = null;
function cursor(x, y, press = 0, op = 1) {
  const c = $('cursor');
  c.style.display = 'block';
  c.style.opacity = op;
  c.style.transform = `translate(${x - 3}px,${y - 3}px) scale(${1 - 0.14 * press})`;
  cursorSet = true;
}
function ripple(x, y, t, t0) {
  const u = P(t, t0, t0 + 0.55);
  if (u <= 0 || u >= 1) return;
  const r = $('ripple');
  r.style.display = 'block';
  r.style.left = x + 'px'; r.style.top = y + 'px';
  r.style.opacity = (1 - u) * 0.9;
  r.style.transform = `scale(${lerp(0.3, 1.5, E.outCubic(u))})`;
  rippleSet = true;
}
const pressAt = (t, tc) => (t >= tc - 0.04 && t < tc + 0.14 ? 1 : 0);

const EV = [];
const ev = (t, type, extra = {}) => EV.push(Object.assign({ t: +t.toFixed(4), type }, extra));

// ---------- COLD OPEN ----------
const COLS = [54, 336, 196, 300, 194];
const COLX = COLS.reduce((a, w, i) => (a.push(i ? a[i - 1] + COLS[i - 1] : 0), a), []);
const RH = 66, BODY_TOP = 248;
const ROWS = [
  ['Företag', 'Status', 'Anteckning', 'Skickad'],
  ['Nordvik Systems AB', 'Väntar', '', '14 sep'],
  ['Hallén & Berg', 'väntar?', 'ringa??', '15 sep'],
  ['Kommunal IT-service', 'söka??', 'sista dag??', '–'],
  ['Alvesta Digital', '???', 'vem var Lisa', '18 sep'],
  ['Vårddata Sverige', 'Skickat??', 'följ upp (när?)', '23 sep'],
  ['Mölndals Energi', 'Väntar', '', '24 sep'],
  ['Västkustens IT', 'intervju??', 'kolla mejl', '26 sep'],
  ['Partille Media', 'Nej?', 'samma som förra?', '30 sep'],
  ['Borås Textilsystem', '???', 'ringa igen??', '5 okt'],
];
const SCOL = 2, NCOL = 3, LAST_ROW = 10, TOTAL_ROWS = 18;
const DRAG = [0.15, 1.35];
const statusClass = s => (/^v[äa]ntar/i.test(s) ? 'wait' : /^(\?\?\?|Nej\?|söka\?\?)$/.test(s) ? 'huh' : '');
const cells = [];
function buildSheet() {
  ['', 'A', 'B', 'C', 'D'].forEach((l, i) => {
    const d = document.createElement('div'); d.style.width = COLS[i] + 'px'; d.textContent = l; $('xlCols').appendChild(d);
  });
  for (let r = 1; r <= TOTAL_ROWS; r++) {
    const row = document.createElement('div');
    row.className = 'xr' + (r === 1 ? ' hdr' : '');
    cells[r] = [];
    for (let c = 0; c < 5; c++) {
      const d = document.createElement('div');
      d.style.width = COLS[c] + 'px';
      if (c === 0) d.textContent = r;
      else {
        const v = (ROWS[r - 1] || [])[c - 1] || '';
        d.textContent = v;
        if (r > 1 && c === SCOL) d.className = statusClass(v);
        if (r > 1 && c === NCOL && v) d.className = 'note';
      }
      row.appendChild(d); cells[r][c] = d;
    }
    $('xlRows').appendChild(row);
  }
}
const cellRect = (r, c) => ({ x: COLX[c], y: BODY_TOP + (r - 1) * RH, w: COLS[c], h: RH });
function renderCold(t) {
  const u = P(t, DRAG[0], DRAG[1]);
  const filledTo = t < DRAG[0] ? 2 : 2 + Math.floor((LAST_ROW - 2) * E.inOutCubic(u) + 1e-6);
  for (let r = 3; r <= LAST_ROW; r++) {
    const v = r <= filledTo ? 'Väntar' : ROWS[r - 1][SCOL - 1];
    if (cells[r][SCOL].textContent !== v) cells[r][SCOL].textContent = v;
    cells[r][SCOL].className = statusClass(v);
  }
  const s = $('xlSel'), fb = $('xlFillBox');
  const b2 = cellRect(2, SCOL), last = cellRect(LAST_ROW, SCOL);
  const dragY = lerp(b2.y + RH, last.y + RH, E.inOutCubic(u));
  const done = t > DRAG[1] + 0.05;
  s.style.left = b2.x - 2 + 'px'; s.style.top = b2.y - BODY_TOP - 2 + 'px';
  s.style.width = b2.w + 3 + 'px';
  s.style.height = (done ? last.y + RH - b2.y : RH) + 3 + 'px';
  fb.style.display = t >= DRAG[0] && !done ? 'block' : 'none';
  fb.style.left = b2.x + 'px'; fb.style.top = b2.y - BODY_TOP + 'px';
  fb.style.width = b2.w + 'px'; fb.style.height = Math.max(RH, dragY - b2.y) + 'px';
  if (!cross) {
    cross = document.createElement('div');
    cross.innerHTML = '<svg width="38" height="38" viewBox="0 0 34 34"><path d="M17 3v28M3 17h28" stroke="#fff" stroke-width="6"/><path d="M17 3v28M3 17h28" stroke="#0C1A2E" stroke-width="2.4"/></svg>';
    cross.style.cssText = 'position:absolute;z-index:50;left:0;top:0;display:none';
    $('stage').appendChild(cross);
  }
  if (t < MORPH + 0.2) {
    cross.style.display = 'block';
    cross.style.opacity = 1 - P(t, MORPH - 0.1, MORPH + 0.15);
    const y = t < DRAG[0] ? b2.y + RH : dragY;
    cross.style.transform = `translate(${b2.x + b2.w - 17}px,${y - 17}px)`;
    crossSet = true;
  }
  // captions
  const k = $('capK'), c2 = $('cap2');
  const ki = E.outExpo(P(t, -0.6, -0.05)), ko = E.inCubic(P(t, MORPH - 0.05, MORPH + 0.2));
  k.style.clipPath = `inset(0 ${(1 - ki) * 100}% 0 0)`; k.style.opacity = 1 - ko;
  const ci = E.outExpo(P(t, 1.45, 1.95));
  c2.style.clipPath = `inset(0 ${(1 - ci) * 100}% 0 0)`; c2.style.opacity = 1 - ko;
  c2.style.transform = k.style.transform = `translateY(${-ko * 16}px)`;
  // sheet leaves
  const xo = E.inCubic(P(t, MORPH, MORPH + 0.4));
  const xl = $('xl');
  xl.style.opacity = 1 - xo;
  xl.style.transform = `scale(${1 - 0.08 * xo})`;
}

// ---------- LOGO ----------
function renderLogo(t) {
  const disc = $('sgDisc');
  disc.style.transformOrigin = '50px 50px';
  const d = P(t, M0 - 0.8, M0 - 0.42);
  disc.style.transform = `scale(${t < M0 - 0.8 ? 0 : E.outBack(d)})`;
  const ring = $('sgRing'), L = 2 * Math.PI * 37;
  ring.style.strokeDasharray = L;
  ring.style.strokeDashoffset = L * (1 - E.inOutCubic(P(t, M0 - 0.7, M0 - 0.2)));
  ['sgB1', 'sgB2', 'sgB3'].forEach((id, i) => {
    const x = E.outExpo(P(t, M0 - 0.6 + i * 0.08, M0 - 0.25 + i * 0.08));
    $(id).style.opacity = x;
    $(id).style.transform = `translateX(${(1 - x) * -16}px)`;
  });
  const ck = $('sgCheck'), CL = 12;
  ck.style.strokeDasharray = CL;
  ck.style.strokeDashoffset = CL * (1 - E.outCubic(P(t, M0 - 0.18, M0)));
  const sg = $('sigilBig');
  sg.style.transform = `scale(${1 + 0.035 * Math.sin(Math.PI * P(t, M0, M0 + 0.35))})`;
  // caption 3 holds on the dark field, then hands over to the wordmark
  const c3 = $('cap3');
  const ci = E.outExpo(P(t, MORPH + 0.2, MORPH + 0.65)), co = E.inCubic(P(t, M0 - 0.12, M0 + 0.12));
  c3.style.opacity = ci * (1 - co);
  c3.style.transform = `translateY(${(1 - ci) * 24 - co * 30}px)`;
  lines($('lgWord'), t, M0 + 0.05, 0, 0.85);
  $('lgWord').style.opacity = t >= M0 + 0.05 ? 1 : 0;
  lines($('lgNews'), t, M0 + 0.45, 0, 0.8);
  $('lgNews').style.opacity = t >= M0 + 0.45 ? 1 : 0;
}

// ---------- feature intro / exit ----------
function featIntro(root, lt) {
  fadeUp(root.querySelector('.kicker'), lt, 0.02, 0.6, 12);
  lines(root.querySelector('.headline'), lt, 0.08, 0.09, 0.85);
  lines(root.querySelector('.sub'), lt, 0.3, 0.07, 0.8);
  fadeUp(root.querySelector('.panel'), lt, 0.2, 0.8, 44);
}

// ---------- F1 ----------
let f1WhyH = 0;
function renderF1(t) {
  const root = $('sF1'), lt = t - C1;
  const w = E.inOutCubic(P(t, C1 - 0.25, C1 + 0.28));
  root.style.clipPath = `inset(${(1 - w) * 100}% 0 0 0)`;
  featIntro(root, lt);
  typed($('f1Query'), '.NET-utvecklare', lt, 0.45, 0.95);
  $('f1Caret').style.opacity = lt < 1.1 && Math.floor(lt * 3.5) % 2 === 0 ? 1 : 0;
  const sok = $('f1Sok'), pr = lt >= 1.02 && lt < 1.15;
  sok.style.transform = `scale(${pr ? 0.95 : 1})`; sok.style.background = pr ? '#24364F' : '#0C1A2E';
  fadeUp($('f1Lbl'), lt, 1.1, 0.6, 14);
  const jc = [$('jc1'), $('jc2'), $('jc3')];
  jc.forEach((c, i) => fadeUp(c, lt, 1.15 + i * 0.12, 0.7, 34));
  pop($('jc2b'), lt, 1.5); pop($('jc3b'), lt, 1.6);
  const pct = $('jcPct'), strike = $('jcStrike'), top = $('jcTop');
  strike.style.width = (pct.offsetWidth + 12) * E.outCubic(P(lt, 2.2, 2.45)) + 'px';
  const out = E.inCubic(P(lt, 2.65, 2.8));
  pct.style.opacity = 1 - out; strike.style.opacity = 1 - out;
  pct.style.transform = `scaleX(${1 - 0.4 * out})`;
  const tin = P(lt, 2.75, 3.0);
  top.style.opacity = clamp(tin * 4);
  top.style.transform = `scale(${lerp(0.4, 1, E.outBack(tin))})`;
  const why = $('jcWhy');
  if (!f1WhyH) { why.style.height = 'auto'; f1WhyH = why.scrollHeight; }
  const wx = E.inOutCubic(P(lt, 3.1, 3.55));
  why.style.height = f1WhyH * wx + 'px'; why.style.overflow = 'hidden'; why.style.opacity = clamp(wx * 1.5);
  why.querySelectorAll('.wchip').forEach((c, i) => pop(c, lt, 3.3 + i * 0.08, 0.4));
  jc[1].style.top = 40 + jc[0].offsetHeight + 16 + 'px';
  jc[2].style.top = 40 + jc[0].offsetHeight + 16 + jc[1].offsetHeight + 16 + 'px';
  camera($('f1Panel'), [[0, 1, 468, 350], [1.55, 1, 468, 350], [2.1, 1.33, 372, 437], [9, 1.33, 372, 437]], lt);
}

// ---------- F2 ----------
const Q_TOP = 96, Q_H = 124, Q_GAP = 8, SHEET_TOP = 28 + 56, SHEET_H = 56;
function renderF2(t) {
  const root = $('sF2'), lt = t - C2;
  featIntro(root, lt);
  fadeUp($('f2Mini'), lt, 0.3, 0.5, 12);
  $('f2Mini').style.opacity = Math.min(parseFloat($('f2Mini').style.opacity), 1 - P(lt, 0.85, 1.1));
  fadeUp($('qHead'), lt, 1.2, 0.6, 12);
  const CLICK = 2.8;
  const collapse = E.inOutCubic(P(lt, 3.3, 3.75));
  let y = Q_TOP;
  for (let i = 0; i < 4; i++) {
    const row = $('q' + i);
    const t0 = 0.85 + i * 0.06, u = E.inOutCubic(P(lt, t0, t0 + 0.6));
    const appear = fadeUp(row, lt, 0.3 + i * 0.05, 0.5, 12);
    let h = lerp(SHEET_H, Q_H, u);
    let top = lerp(SHEET_TOP + i * SHEET_H, y, u);
    if (i === 2) { h *= 1 - collapse; row.style.opacity = appear * (1 - collapse); }
    row.style.top = top + 'px'; row.style.height = h + 'px';
    row.style.borderTopColor = u > 0.5 ? '#E3E8F0' : 'transparent';
    row.querySelector('.sheetTxt').style.opacity = 1 - clamp(u * 2.2);
    row.querySelector('.in').style.opacity = clamp((u - 0.4) * 2.2);
    y += (i === 2 ? (Q_H + Q_GAP) * (1 - collapse) : Q_H + Q_GAP);
  }
  // count 4 -> 3
  const cu = E.outExpo(P(lt, 3.45, 3.9));
  $('qA').style.transform = $('qB').style.transform = `translateY(${-30 * cu}px)`;
  // the follow-up
  const lbl = $('q2lbl'), btn = $('q2btn'), row2 = $('q2');
  const done = lt >= CLICK + 0.08;
  if (done) {
    lbl.innerHTML = '<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>UPPFÖLJNING LOGGAD <em>idag</em>';
    lbl.style.color = 'var(--success)';
  } else {
    lbl.innerHTML = 'VÄNTAR PÅ SVAR <em>21 dagar</em>';
    lbl.style.color = 'var(--info)';
  }
  btn.style.opacity = 1 - P(lt, CLICK + 0.1, CLICK + 0.3);
  const fl = P(lt, CLICK + 0.08, CLICK + 0.6);
  row2.style.background = lt < CLICK + 0.08 ? (lt > 1.5 ? '#EEF3FA' : '#fff') : mix('#DFF3E5', '#F2F8F4', E.outCubic(fl));
  if (lt >= 1.85 && lt < 3.4) {
    const [bx, by] = center(btn);
    const [x, yy] = pathAt([[1.85, bx + 110, by + 120], [2.6, bx + 8, by + 6], [3.4, bx + 8, by + 6]], lt);
    cursor(x, yy, pressAt(lt, CLICK), clamp(P(lt, 1.85, 2.0)) * (1 - P(lt, 3.15, 3.4)));
    ripple(bx + 8, by + 6, lt, CLICK);
  }
}

// ---------- F3 ----------
const F3_CLICKS = [0.75, 1.2];
const FLURRY = Array.from({ length: 10 }, (_, i) => 1.62 + i * 0.095);
const F3_ALL = F3_CLICKS.concat(FLURRY);
function renderF3(t) {
  const root = $('sF3'), lt = t - C3;
  featIntro(root, lt);
  const A = $('f3A'), B = $('f3B');
  A.style.opacity = 1; A.style.transform = 'none';
  A.style.display = lt < 3.05 ? 'block' : 'none';
  B.style.display = lt >= 3.05 ? 'block' : 'none';
  // two calm reloads, then a frantic flurry: the counter runs away to 41
  let spin = 0, dip = 0, load = 0, loadOp = 0;
  F3_CLICKS.forEach(c => {
    spin += E.outCubic(P(lt, c, c + 0.45));
    const d = P(lt, c + 0.04, c + 0.42);
    if (d > 0 && d < 1) { dip = Math.max(dip, Math.sin(Math.PI * d)); load = E.outCubic(d); loadOp = 1 - clamp((d - 0.75) * 4); }
  });
  spin += 5 * E.inOutCubic(P(lt, 1.6, 2.7));
  FLURRY.forEach(c => { const d = P(lt, c, c + 0.12); if (d > 0 && d < 1) { dip = Math.max(dip, 0.6 * Math.sin(Math.PI * d)); load = d; loadOp = 1; } });
  let n = 12;
  if (lt >= F3_CLICKS[0] + 0.3) n = 13;
  if (lt >= F3_CLICKS[1] + 0.3) n = 14;
  if (lt >= 1.65) n = 14 + Math.round(27 * E.inCubic(P(lt, 1.65, 2.6)));
  $('f3Refresh').style.transform = `rotate(${spin * 360}deg)`;
  $('f3Page').style.opacity = 1 - 0.85 * dip;
  $('f3Load').style.width = load * 100 + '%';
  $('f3Load').style.opacity = loadOp;
  $('f3Count').textContent = n;
  const bump = Math.max(...F3_CLICKS.map(c => Math.sin(Math.PI * P(lt, c + 0.3, c + 0.5))), 0.6 * Math.sin(Math.PI * P(lt, 2.6, 2.9)));
  $('f3Counter').style.transform = `scale(${1 + 0.08 * bump})`;
  camera($('f3Panel'), [[0, 1, 468, 350], [1.5, 1, 468, 350], [1.95, 1.45, 370, 470], [2.85, 1.45, 370, 470], [3.1, 1, 468, 350]], lt);
  const WATCH = 3.6;
  if (lt >= 0.35 && lt < 4.1) {
    const rc = center($('f3Refresh'));
    const wc = lt >= 3.05 ? center($('f3Watch')) : [rc[0] + 500, rc[1] + 80];
    const [x, y] = pathAt([[0.35, rc[0] + 120, rc[1] + 130], [0.7, rc[0] + 2, rc[1] + 2], [2.9, rc[0] + 2, rc[1] + 2], [3.52, wc[0] - 10, wc[1] + 4], [4.1, wc[0] + 40, wc[1] + 90]], lt);
    const press = F3_ALL.reduce((m, c) => Math.max(m, lt >= c - 0.02 && lt < c + 0.05 ? 1 : 0), 0) || pressAt(lt, WATCH);
    const zoomHide = P(lt, 1.5, 1.62) * (1 - P(lt, 2.95, 3.08));
    cursor(x, y, press, clamp(P(lt, 0.35, 0.5)) * (1 - P(lt, 3.85, 4.1)) * (1 - zoomHide));
    F3_CLICKS.forEach(c => ripple(rc[0] + 2, rc[1] + 2, lt, c));
    if (lt >= 3.05) ripple(wc[0] - 10, wc[1] + 4, lt, WATCH);
  }
  fadeUp($('f3Bar'), lt, 3.08, 0.6, -14);
  fadeUp($('f3Co'), lt, 3.12, 0.6, 24);
  fadeUp($('f3List'), lt, 3.2, 0.6, 24);
  const on = lt >= WATCH + 0.06;
  $('f3Watch').className = 'btn watch' + (on ? ' on' : '');
  $('f3WatchTxt').innerHTML = on ? '<span style="display:inline-flex;align-items:center;gap:8px"><svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Bevakar</span>' : 'Bevaka';
  $('f3Body').style.opacity = 1 - 0.88 * E.outCubic(P(lt, 3.85, 4.15));
  $('f3Nav').style.opacity = $('f3Bar').querySelector('.brand').style.opacity = 1 - 0.6 * E.outCubic(P(lt, 3.85, 4.15));
  const later = $('f3Later');
  later.style.opacity = E.outCubic(P(lt, 3.85, 4.0)) * (1 - P(lt, 4.88, 5.0));
  const BELL = 5.0;
  const bw = P(lt, BELL, BELL + 0.8);
  $('f3Bell').style.transform = `rotate(${Math.sin(bw * Math.PI * 7) * 18 * (1 - bw)}deg)`;
  $('f3Bell').style.transformOrigin = '50% 10%';
  pop($('f3Badge'), lt, BELL, 0.35);
  const nt = E.outExpo(P(lt, BELL + 0.1, BELL + 0.6));
  const notif = $('f3Notif');
  notif.style.opacity = nt;
  notif.style.transform = `translateY(${(1 - nt) * -20}px)`;
  notif.style.clipPath = `inset(0 0 ${(1 - nt) * 100}% 0)`;
}

// ---------- F4 ----------
function renderF4(t) {
  const root = $('sF4'), lt = t - C4;
  featIntro(root, lt);
  const panel = $('f4Panel'), cam = panel.querySelector('.cam');
  const scanU = E.inOutCubic(P(lt, 0.55, 1.4));
  const cvTop = 24, cvH = PH - 48;
  const sy = cvTop + cvH * scanU;
  const scan = $('cvScan'), trail = $('cvTrail');
  const on = lt >= 0.55 && lt < 1.6;
  scan.style.display = trail.style.display = on ? 'block' : 'none';
  scan.style.top = sy + 'px'; trail.style.top = cvTop + 'px'; trail.style.height = sy - cvTop + 'px';
  trail.style.opacity = 1 - P(lt, 1.35, 1.6); scan.style.opacity = 1 - P(lt, 1.4, 1.55);
  const V = [['v2', 'cvProfile', '#FBE0E0', 'lk2', 1.5], ['v1', 'cvContact', '#DFF3E5', 'lk1', 2.3], ['v3', 'cvBullet', '#DFF3E5', 'lk3', 2.8]];
  V.forEach(([vid, cid, col, lid, t0]) => {
    const v = $(vid), c = $(cid), lk = $(lid);
    const x = E.outExpo(P(lt, t0, t0 + 0.7));
    v.style.opacity = x; v.style.transform = `translateX(${(1 - x) * 40}px)`;
    const h = P(lt, t0 + 0.05, t0 + 0.35);
    c.style.backgroundColor = h > 0 ? mix('#FFFFFF', col, E.outCubic(h)) : '';
    const vr = posIn(v, cam), cr = posIn(c, cam);
    const x1 = vr.x - (1 - x) * 40, y1 = vr.y + 44, x2 = Math.min(cr.x + cr.w, 24 + 396 - 6), y2 = cr.y + Math.min(cr.h / 2, 30);
    lk.setAttribute('d', `M${x1} ${y1} C${x1 - 22} ${y1}, ${x2 + 22} ${y2}, ${x2} ${y2}`);
    lk.style.strokeDashoffset = 1000 * (1 - E.inOutCubic(P(lt, t0 + 0.1, t0 + 0.5)));
    lk.style.opacity = h > 0 ? 1 : 0;
  });
}

// ---------- TRASH ----------
let trashOk = null, trashCancel = null;
function renderTrash(t) {
  const lt = t - TR;
  lines($('trQ'), lt, 0.05, 0, 0.8);
  const qOut = E.inCubic(P(lt, 2.3, 2.5));
  $('trQ').style.opacity = 1 - qOut; $('trQ').style.transform = `translateY(${-qOut * 30}px)`;
  lines($('trA'), lt, 2.45, 0, 0.8);
  $('trA').style.opacity = lt >= 2.45 ? 1 : 0;
  const fi = $('fileIco'), fn = $('fileName'), bin = $('bin');
  fadeUp(fi, lt, 0.1, 0.7, 30);
  fadeUp(fn, lt, 0.18, 0.7, 20);
  fadeUp(bin, lt, 0.2, 0.7, 20);
  const CLICK = 2.0;
  const dr = P(lt, CLICK + 0.1, CLICK + 0.55);
  if (dr > 0) {
    const dy = 975 - 442;
    fi.style.transform = `translateY(${E.inCubic(dr) * dy - Math.sin(Math.PI * dr) * 40}px) rotate(${lerp(-4, 14, dr)}deg) scale(${lerp(1, 0.3, E.outCubic(dr))})`;
    fi.style.opacity = 1 - clamp((dr - 0.82) * 6);
    fn.style.opacity = 1 - clamp(P(lt, CLICK + 0.05, CLICK + 0.2));
  }
  const lidOpen = E.outCubic(P(lt, CLICK + 0.15, CLICK + 0.3)) * (1 - E.outBack(P(lt, CLICK + 0.6, CLICK + 0.78)));
  const thump = Math.sin(Math.PI * P(lt, CLICK + 0.58, CLICK + 0.78));
  $('binLid').style.transform = `rotate(${-34 * lidOpen}deg)`;
  $('binLid').style.transformOrigin = '3px 7px';
  bin.style.transform = `translateY(${thump * 5}px) scale(${1 + 0.04 * thump}, ${1 - 0.05 * thump})`;
  fadeUp($('toast'), lt, CLICK + 0.75, 0.6, 20);
  const dl = $('dlg'), sc = $('scrim');
  const din = E.outExpo(P(lt, 0.4, 0.75)), dout = E.inCubic(P(lt, CLICK + 0.06, CLICK + 0.2));
  dl.style.opacity = din * (1 - dout); sc.style.opacity = din * (1 - dout);
  dl.style.transform = `scale(${lerp(0.94, 1, din) - 0.03 * dout})`;
  dl.style.display = sc.style.display = dout >= 1 ? 'none' : 'block';
  const ok = $('dlgOk'), cancel = $('dlgCancel');
  if (dl.style.display !== 'none' && din > 0.99) { trashOk = center(ok); trashCancel = center(cancel); }
  if (lt >= 0.55 && lt < CLICK + 0.8) {
    const oc = trashOk || center(ok), cc = trashCancel || center(cancel);
    const [x, y] = pathAt([[0.55, oc[0] + 130, oc[1] + 150], [1.0, oc[0] + 4, oc[1] + 6], [1.12, oc[0] + 4, oc[1] + 6], [1.38, cc[0] + 8, cc[1] + 8], [1.82, cc[0] + 8, cc[1] + 8], [CLICK - 0.06, oc[0] + 2, oc[1] + 4], [CLICK + 0.8, oc[0] + 40, oc[1] + 150]], lt);
    cursor(x, y, pressAt(lt, CLICK), clamp(P(lt, 0.55, 0.7)) * (1 - P(lt, CLICK + 0.4, CLICK + 0.8)));
    ripple(oc[0] + 2, oc[1] + 4, lt, CLICK);
    const hovC = lt >= 1.36 && lt < 1.86, hovO = (lt >= 0.98 && lt < 1.16) || (lt >= CLICK - 0.12 && lt < CLICK + 0.1);
    cancel.style.background = hovC ? '#E3E8F0' : '#fff';
    cancel.style.borderColor = hovC ? '#7C8AA0' : '';
    ok.style.background = hovO ? '#A11717' : '';
  }
}

// ---------- END ----------
function renderEnd(t) {
  const root = $('sEnd'), lt = t - EN;
  const w = E.inOutCubic(P(t, EN - 0.25, EN + 0.28));
  root.style.clipPath = `inset(${(1 - w) * 100}% 0 0 0)`;
  lines($('enNews'), lt, 0.12, 0, 0.8);
  lines($('enHead'), lt, 0.25, 0.1, 0.9);
  fadeUp($('enSub'), lt, 0.6, 0.8, 18);
  const u = $('enUrl');
  fadeUp(u, lt, 0.85, 0.8, 22);
  const sg = u.querySelector('svg'), sx = E.outBack(P(lt, 0.85, 1.3));
  sg.style.transform = `scale(${lerp(0.5, 1, sx)}) rotate(${(1 - sx) * -40}deg)`;
  fadeUp($('enBtn'), lt, 1.1, 0.8, 22);
  fadeUp($('enNote'), lt, 1.4, 0.8, 16);
  const CLICK = 3.0;
  const b = $('enBtn'), pr = pressAt(lt, CLICK);
  b.style.background = lt >= CLICK - 0.04 && lt < CLICK + 0.2 ? '#E3E8F0' : '#fff';
  if (pr) b.style.transform = 'scale(0.97)';
  if (lt >= 1.9) {
    const bc = center(b), tx = bc[0] + 110, ty = bc[1] + 10;
    const [x, y] = pathAt([[1.9, tx + 120, ty + 150], [CLICK - 0.1, tx, ty], [9, tx, ty]], lt);
    cursor(x, y, pr, clamp(P(lt, 1.9, 2.05)));
    ripple(tx, ty, lt, CLICK);
  }
}

// ---------- progress + footer ----------
const CHAP = [[C1, C2], [C2, C3], [C3, C4], [C4, TR]];
function renderChrome(t) {
  const pg = $('prog'), f = $('footer');
  const pOn = t >= C1 - 0.25 && t < TR;
  const fOn = t >= C1 - 0.25 && t < SC.trash[1];
  pg.style.display = pOn ? 'flex' : 'none';
  f.style.display = fOn ? 'flex' : 'none';
  const dark = (t >= C2 && t < C3) || (t >= C4 && t < TR);
  pg.className = dark ? 'dk' : ''; f.className = dark ? 'dk' : '';
  if (pOn) {
    pg.style.opacity = E.outCubic(P(t, C1 + 0.2, C1 + 0.6));
    [...pg.querySelectorAll('b')].forEach((b, i) => (b.style.width = P(t, CHAP[i][0], CHAP[i][1]) * 100 + '%'));
  }
  if (fOn) {
    fadeUp(f, t, C1 + 0.3, 0.7, 16);
    f.querySelector('.open').style.opacity = 1 - P(t, TR - 0.2, TR + 0.1);
  }
}

// ---------- SDH ----------
const SDH = [
  [0.3, MORPH, '[väntmusik]', 1158],
  [MORPH, MORPH + 1.3, '[väntmusiken tystnar]', 1158],
  [C2 + 1.5, C2 + 2.85, '[syrsor]', 1072],
  [C2 + 2.88, C2 + 4.3, '[syrsorna tystnar]', 1072],
];
function renderSDH(t) {
  const el = $('sdh'), cue = SDH.find(([a, b]) => t >= a && t < b);
  if (!cue) { el.style.display = 'none'; return; }
  el.style.display = 'flex'; el.style.top = cue[3] + 'px';
  $('sdhTxt').textContent = cue[2];
}

// ---------- master ----------
const SCENES = [
  ['sCold', 'cold', renderCold], ['sLogo', 'logo', renderLogo], ['sF1', 'f1', renderF1], ['sF2', 'f2', renderF2],
  ['sF3', 'f3', renderF3], ['sF4', 'f4', renderF4], ['sTrash', 'trash', renderTrash], ['sEnd', 'end', renderEnd],
];
function seek(t) {
  cursorSet = rippleSet = crossSet = false;
  for (const [id, key, fn] of SCENES) {
    const [a, b] = SC[key], on = t >= a && t < b;
    $(id).style.display = on ? 'block' : 'none';
    if (on) fn(t);
  }
  renderChrome(t);
  renderSDH(t);
  if (!cursorSet) $('cursor').style.display = 'none';
  if (!rippleSet) $('ripple').style.display = 'none';
  if (cross && !crossSet) cross.style.display = 'none';
}

function buildEvents() {
  ev(0, 'holdMusicStart');
  ev(DRAG[0] - 0.05, 'clickDown');
  for (let r = 3; r <= LAST_ROW; r++) {
    // the row fills when the eased drag passes it
    let lo = 0, hi = 1;
    for (let k = 0; k < 30; k++) { const m = (lo + hi) / 2; (2 + (LAST_ROW - 2) * E.inOutCubic(m) >= r) ? (hi = m) : (lo = m); }
    ev(lerp(DRAG[0], DRAG[1], hi), 'tickRow');
  }
  ev(DRAG[1] + 0.05, 'clickUp');
  ev(1.45, 'capIn');
  ev(MORPH, 'tapeStop');
  ev(MORPH + 0.6, 'riser', { dur: M0 - MORPH - 0.6 });
  ev(M0, 'logoTick');
  ev(C1 - 0.25, 'whooshUp');
  for (let k = 0; k < 15; k++) ev(C1 + 0.45 + (0.5 * k) / 15, 'keySoft');
  ev(C1 + 1.02, 'clickSoft');
  [1.15, 1.27, 1.39].forEach(x => ev(C1 + x, 'tickUI'));
  ev(C1 + 2.2, 'strike');
  ev(C1 + 2.75, 'pop');
  ev(C1 + 3.1, 'swell');
  [0.85, 0.91, 0.97, 1.03].forEach(x => ev(C2 + x, 'lift'));
  ev(C2 + 1.45, 'land');
  ev(C2 + 1.5, 'crickets', { dur: 2.8 - 1.5 + 0.06 });
  ev(C2 + 2.8, 'click');
  ev(C2 + 2.88, 'chime');
  ev(C2 + 3.3, 'swell');
  F3_CLICKS.forEach(c => { ev(C3 + c, 'click'); ev(C3 + c + 0.02, 'refresh'); });
  FLURRY.forEach((c, i) => ev(C3 + c, 'clickFast', { i }));
  ev(C3 + 1.6, 'spinUp', { dur: 1.1 });
  ev(C3 + 2.62, 'counterHit');
  ev(C3 + 3.6, 'click');
  ev(C3 + 3.66, 'toggle');
  ev(C3 + 3.85, 'tickUI');
  ev(C3 + 5.0, 'ding');
  ev(C4 + 0.55, 'scan', { dur: 0.85 });
  ev(C4 + 1.5, 'fail');
  ev(C4 + 2.3, 'pass');
  ev(C4 + 2.8, 'pass');
  ev(TR + 0.4, 'dialog');
  ev(TR + 1.38, 'tickUI');
  ev(TR + 2.0, 'click');
  ev(TR + 2.1, 'lift');
  ev(TR + 2.15, 'lid');
  ev(TR + 2.6, 'binThump');
  ev(TR + 2.75, 'tickUI');
  ev(EN - 0.25, 'whooshUp');
  ev(EN + 3.0, 'click');
  ev(DURATION, 'end');
  EV.sort((a, b) => a.t - b.t);
}

function init() {
  document.querySelectorAll('.panel').forEach(p => {
    const cam = document.createElement('div'); cam.className = 'cam';
    while (p.firstChild) cam.appendChild(p.firstChild);
    p.appendChild(cam);
  });
  $('sLogo').appendChild($('cap3'));
  document.querySelectorAll('.qrow').forEach(r => {
    const [co, stv, cls] = r.dataset.sheet.split('|');
    const d = document.createElement('div'); d.className = 'sheetTxt';
    d.innerHTML = `<div style="width:420px;padding:0 14px;height:100%;display:flex;align-items:center;border-right:1px solid #E1E4E8">${co}</div><div style="flex:1;padding:0 14px;height:100%;display:flex;align-items:center" class="${cls}">${stv}</div>`;
    d.querySelector('.' + cls).style.background = cls === 'wait' ? '#FFF4C2' : '#FBD5D5';
    if (cls === 'huh') { d.querySelector('.huh').style.color = '#8A1C1C'; d.querySelector('.huh').style.fontWeight = 700; }
    r.appendChild(d);
    r.style.padding = '0';
    r.querySelector('.in').style.padding = '0 20px';
  });
  buildSheet();
  buildEvents();
  window.seek = seek;
  window.DURATION = DURATION;
  window.EVENTS = EV;
  window.CLOCK = { BPM, BEAT, BAR, M0, MORPH, C1, C2, C3, C4, TR, EN, FIN, bars: Array.from({ length: 20 }, (_, i) => bar(i + 1)) };
  seek(0);
  window.READY = true;
}
Promise.all(['800 74px SS3', '700 40px SS3', '600 30px SS3', '400 30px SS3', '500 30px SS3', '600 26px JBM', '700 26px JBM', '500 26px JBM', '400 23px Carlito', '700 23px Carlito']
  .map(f => document.fonts.load(f))).then(() => document.fonts.ready).then(init);
})();
