// Sprite factory: composes bitmap art assets (see assets/ATTRIBUTION.md) into
// cached offscreen canvases at boot; falls back to procedural generation for
// any asset that failed to load. Runtime rendering is pure drawImage.
import { makeRng, hashString } from '../core/rng.js';
import { SHIPS } from '../data/ships.js';
import { getImage } from './assets.js';

// ---- value noise / fBm --------------------------------------------------------
function makeNoise2D(seed) {
  const rng = makeRng(seed);
  const perm = new Uint8Array(512);
  const p = [...Array(256).keys()];
  for (let i = 255; i > 0; i--) { const j = Math.floor(rng() * (i + 1)); [p[i], p[j]] = [p[j], p[i]]; }
  for (let i = 0; i < 512; i++) perm[i] = p[i & 255];
  const vals = new Float32Array(256);
  for (let i = 0; i < 256; i++) vals[i] = rng();
  const valAt = (ix, iy) => vals[perm[(ix & 255) + perm[iy & 255]]];
  function noise(x, y) {
    const ix = Math.floor(x), iy = Math.floor(y);
    const fx = x - ix, fy = y - iy;
    const sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
    const a = valAt(ix, iy), b = valAt(ix + 1, iy), c = valAt(ix, iy + 1), d = valAt(ix + 1, iy + 1);
    return a + (b - a) * sx + (c - a) * sy + (a - b - c + d) * sx * sy;
  }
  function fbm(x, y, oct = 4) {
    let v = 0, amp = 0.5, f = 1;
    for (let i = 0; i < oct; i++) { v += amp * noise(x * f, y * f); amp *= 0.5; f *= 2; }
    return v;
  }
  function ridge(x, y, oct = 3) {
    let v = 0, amp = 0.5, f = 1;
    for (let i = 0; i < oct; i++) { v += amp * Math.abs(noise(x * f, y * f) - 0.5) * 2; amp *= 0.5; f *= 2; }
    return v;
  }
  return { noise, fbm, ridge };
}

function canvas(size) {
  const c = document.createElement('canvas');
  c.width = c.height = size;
  return c;
}
const cache = new Map();
function cached(key, maker) {
  if (!cache.has(key)) cache.set(key, maker());
  return cache.get(key);
}

// ---- Ships ----------------------------------------------------------------------
// Design languages per faction
const STYLE = {
  aurelian: { hull: '#8a7a55', dark: '#4a3f28', accent: '#ffd76a', trim: 'gold' },
  kaldari: { hull: '#5a6a7d', dark: '#2e3a48', accent: '#7ac2ff', trim: 'angular' },
  meridian: { hull: '#4f7a72', dark: '#2a4a44', accent: '#6affd8', trim: 'round' },
  varkhald: { hull: '#7d5f4a', dark: '#453226', accent: '#ff9a5a', trim: 'jagged' },
  sisters: { hull: '#9a958a', dark: '#55524a', accent: '#ffffff', trim: 'round' },
  directorate: { hull: '#6a6a72', dark: '#35353d', accent: '#ffd76a', trim: 'angular' },
  blood_reavers: { hull: '#6a3a42', dark: '#381e24', accent: '#ff4a5a', trim: 'jagged' },
  nathari: { hull: '#5a3a72', dark: '#2e1e40', accent: '#b46aff', trim: 'angular' },
  crimson_hand: { hull: '#72324a', dark: '#3d1a28', accent: '#ff5a7a', trim: 'curve' },
  ashfang: { hull: '#6a6a3a', dark: '#38381e', accent: '#d4e05a', trim: 'jagged' },
};
const SHIP_PX = { frigate: 72, destroyer: 88, cruiser: 116, battleship: 160 };

// ---- Bitmap art mapping ---------------------------------------------------------
// Ship art: MillionthVector (CC-BY 4.0). Source sprites point "up" by convention;
// `rot` is the degrees to rotate so the nose points +x (game convention).
// `hue` shifts the dominant color to match the faction identity.
const SET_ORANGE = { frigate: 'smallorange', destroyer: 'orangeship3', cruiser: 'orangeship2', battleship: 'orangeship' };
const SET_BLUE   = { frigate: 'blueship3', destroyer: 'blueship4', cruiser: 'blueship2', battleship: 'blueship1' };
const SET_GREEN  = { frigate: 'greenship4', destroyer: 'greenship3', cruiser: 'greenship2', battleship: 'greenship1' };
const SET_ALIEN  = { frigate: 'alien4', destroyer: 'alien3', cruiser: 'alien2', battleship: 'alien1' };
const SET_F5     = { frigate: 'f5s1', destroyer: 'f5s2', cruiser: 'f5s3', battleship: 'f5s4' };
const SET_RED    = { frigate: 'rd3', destroyer: 'rd2', cruiser: 'rd1', battleship: 'redship4' };
const SET_POLICE = { frigate: 'spshipspr1', destroyer: 'medfighter', cruiser: 'medfrighter', battleship: 'bgbattleship' };
const SET_ASH    = { frigate: 'aliensprite2', destroyer: 'aliensprite', cruiser: 'att2', battleship: 'alienspaceship' };

const SHIP_ART = {
  aurelian:     { set: SET_ORANGE, hue: 24 },   // orange -> gold
  kaldari:      { set: SET_BLUE,   hue: -13 },  // blue -> steel blue
  meridian:     { set: SET_GREEN,  hue: 63 },   // green -> teal
  varkhald:     { set: SET_ORANGE, hue: 0 },    // orange
  sisters:      { set: SET_F5,     hue: 0 },    // white hull
  nathari:      { set: SET_ALIEN,  hue: -23 },  // violet -> purple
  directorate:  { set: SET_POLICE, hue: 0 },    // blue-grey
  blood_reavers:{ set: SET_RED,    hue: -10 },  // deep red
  crimson_hand: { set: SET_ORANGE, hue: -40 },  // orange -> crimson
  ashfang:      { set: SET_ASH,    hue: 40, hueCls: { cruiser: 60, battleship: 0 } }, // olive fleet, grey flagship
};

// Stations: ring station tinted per faction; pirates get the makeshift base.
const STATION_ART = {
  empire:  { img: 'spacestation', hue: { aurelian: -57, kaldari: 108, meridian: 63, varkhald: -82 } },
  sisters: { img: 'spacestation', hue: { sisters: 0 } },
  police:  { img: 'spacestation', hue: { directorate: 108 } },
  pirate:  { img: 'mainbase', hue: {} },
};
const GATE_ART = { img: 'spacestation', hue: 88 }; // green -> cyan

// Rotate RGB around the luma axis (same math as SVG feColorMatrix hueRotate).
function hueShiftCanvas(c, deg) {
  if (!deg) return c;
  const ctx = c.getContext('2d');
  const img = ctx.getImageData(0, 0, c.width, c.height);
  const d = img.data;
  const a = deg * Math.PI / 180, cos = Math.cos(a), sin = Math.sin(a);
  const m = [
    0.213 + cos * 0.787 - sin * 0.213, 0.715 - cos * 0.715 - sin * 0.715, 0.072 - cos * 0.072 + sin * 0.928,
    0.213 - cos * 0.213 + sin * 0.143, 0.715 + cos * 0.285 + sin * 0.140, 0.072 - cos * 0.072 - sin * 0.283,
    0.213 - cos * 0.213 - sin * 0.787, 0.715 - cos * 0.715 + sin * 0.715, 0.072 + cos * 0.928 + sin * 0.072,
  ];
  for (let i = 0; i < d.length; i += 4) {
    if (d[i + 3] === 0) continue;
    const r = d[i], g = d[i + 1], b = d[i + 2];
    d[i]     = Math.min(255, Math.max(0, m[0] * r + m[1] * g + m[2] * b));
    d[i + 1] = Math.min(255, Math.max(0, m[3] * r + m[4] * g + m[5] * b));
    d[i + 2] = Math.min(255, Math.max(0, m[6] * r + m[7] * g + m[8] * b));
  }
  ctx.putImageData(img, 0, 0);
  return c;
}

// Compose a bitmap ship into an S×S canvas, nose pointing +x, length L along x.
function composeShip(img, cls, hue = 0, rot = 90) {
  const S = SHIP_PX[cls] ?? 72;
  const L = S * 0.86;
  const c = canvas(S);
  const ctx = c.getContext('2d');
  const fit = L / Math.max(img.width, img.height);
  const dw = img.width * fit, dh = img.height * fit;
  ctx.translate(S / 2, S / 2);
  ctx.rotate(rot * Math.PI / 180);
  ctx.drawImage(img, -dw / 2, -dh / 2, dw, dh);
  return hueShiftCanvas(c, hue);
}

function composeFlat(img, S, hue = 0) {
  const c = canvas(S);
  const ctx = c.getContext('2d');
  const fit = (S * 0.94) / Math.max(img.width, img.height);
  const dw = img.width * fit, dh = img.height * fit;
  ctx.drawImage(img, (S - dw) / 2, (S - dh) / 2, dw, dh);
  return hueShiftCanvas(c, hue);
}

// FX textures are used directly by effects/renderer.
export function getFxSprite(name) { return getImage(name); }

function hullPath(ctx, cls, L, W) {
  // detailed silhouette, pointing +x, centered at 0,0
  ctx.beginPath();
  if (cls === 'frigate') {
    ctx.moveTo(L * 0.5, 0);
    ctx.quadraticCurveTo(L * 0.2, -W * 0.42, -L * 0.18, -W * 0.5);
    ctx.lineTo(-L * 0.42, -W * 0.28);
    ctx.quadraticCurveTo(-L * 0.32, 0, -L * 0.42, W * 0.28);
    ctx.lineTo(-L * 0.18, W * 0.5);
    ctx.quadraticCurveTo(L * 0.2, W * 0.42, L * 0.5, 0);
  } else if (cls === 'destroyer') {
    ctx.moveTo(L * 0.5, 0);
    ctx.lineTo(L * 0.28, -W * 0.3);
    ctx.lineTo(-L * 0.05, -W * 0.32);
    ctx.lineTo(-L * 0.2, -W * 0.55);
    ctx.lineTo(-L * 0.38, -W * 0.5);
    ctx.lineTo(-L * 0.3, -W * 0.18);
    ctx.lineTo(-L * 0.45, -W * 0.12);
    ctx.lineTo(-L * 0.45, W * 0.12);
    ctx.lineTo(-L * 0.3, W * 0.18);
    ctx.lineTo(-L * 0.38, W * 0.5);
    ctx.lineTo(-L * 0.2, W * 0.55);
    ctx.lineTo(-L * 0.05, W * 0.32);
    ctx.lineTo(L * 0.28, W * 0.3);
    ctx.closePath();
  } else if (cls === 'cruiser') {
    ctx.moveTo(L * 0.5, -W * 0.1);
    ctx.quadraticCurveTo(L * 0.3, -W * 0.28, L * 0.05, -W * 0.32);
    ctx.lineTo(-L * 0.15, -W * 0.55);
    ctx.lineTo(-L * 0.35, -W * 0.5);
    ctx.quadraticCurveTo(-L * 0.28, -W * 0.2, -L * 0.42, -W * 0.12);
    ctx.lineTo(-L * 0.42, W * 0.12);
    ctx.quadraticCurveTo(-L * 0.28, W * 0.2, -L * 0.35, W * 0.5);
    ctx.lineTo(-L * 0.15, W * 0.55);
    ctx.lineTo(L * 0.05, W * 0.32);
    ctx.quadraticCurveTo(L * 0.3, W * 0.28, L * 0.5, W * 0.1);
    ctx.quadraticCurveTo(L * 0.42, 0, L * 0.5, -W * 0.1);
  } else { // battleship
    ctx.moveTo(L * 0.52, 0);
    ctx.lineTo(L * 0.38, -W * 0.22);
    ctx.lineTo(L * 0.1, -W * 0.3);
    ctx.lineTo(L * 0.02, -W * 0.52);
    ctx.lineTo(-L * 0.18, -W * 0.55);
    ctx.lineTo(-L * 0.22, -W * 0.34);
    ctx.lineTo(-L * 0.42, -W * 0.3);
    ctx.lineTo(-L * 0.48, -W * 0.12);
    ctx.lineTo(-L * 0.48, W * 0.12);
    ctx.lineTo(-L * 0.42, W * 0.3);
    ctx.lineTo(-L * 0.22, W * 0.34);
    ctx.lineTo(-L * 0.18, W * 0.55);
    ctx.lineTo(L * 0.02, W * 0.52);
    ctx.lineTo(L * 0.1, W * 0.3);
    ctx.lineTo(L * 0.38, W * 0.22);
    ctx.closePath();
  }
}

export function getShipSprite(shipId, cls, faction) {
  return cached(`ship:${shipId}:${faction}`, () => {
    const art = SHIP_ART[faction];
    const imgName = art?.set[cls];
    const img = imgName && getImage(imgName);
    if (img) return composeShip(img, cls, art.hueCls?.[cls] ?? art.hue);
    return makeProceduralShip(shipId, cls, faction);
  });
}

function makeProceduralShip(shipId, cls, faction) {
    const st = STYLE[faction] ?? STYLE.aurelian;
    const S = SHIP_PX[cls] ?? 72;
    const c = canvas(S);
    const ctx = c.getContext('2d');
    const rng = makeRng(hashString('ship' + shipId));
    const L = S * 0.86, W = S * (cls === 'battleship' ? 0.5 : 0.44);
    ctx.translate(S / 2, S / 2);

    // hull base with metallic gradient (light from top-left)
    hullPath(ctx, cls, L, W);
    const g = ctx.createLinearGradient(-L / 2, -W / 2, L / 2, W / 2);
    g.addColorStop(0, '#c8ccd4');
    g.addColorStop(0.35, st.hull);
    g.addColorStop(1, st.dark);
    ctx.fillStyle = g;
    ctx.fill();
    ctx.save();
    hullPath(ctx, cls, L, W);
    ctx.clip();

    // panel shading: darker belly
    const belly = ctx.createLinearGradient(0, -W / 2, 0, W / 2);
    belly.addColorStop(0, 'rgba(255,255,255,0.10)');
    belly.addColorStop(0.5, 'rgba(0,0,0,0)');
    belly.addColorStop(1, 'rgba(0,0,0,0.35)');
    ctx.fillStyle = belly;
    ctx.fillRect(-L / 2, -W / 2, L, W);

    // panel lines
    ctx.strokeStyle = 'rgba(0,0,0,0.35)';
    ctx.lineWidth = 1;
    for (let i = 0; i < 4; i++) {
      const x = -L * 0.3 + i * L * 0.18 + rng.range(-2, 2);
      ctx.beginPath();
      ctx.moveTo(x, -W * 0.4);
      ctx.lineTo(x + rng.range(-3, 3), W * 0.4);
      ctx.stroke();
    }
    // faction accent stripes
    ctx.fillStyle = st.accent;
    ctx.globalAlpha = 0.85;
    if (st.trim === 'gold' || st.trim === 'angular') {
      ctx.fillRect(-L * 0.05, -W * 0.36, L * 0.3, W * 0.07);
      ctx.fillRect(-L * 0.05, W * 0.29, L * 0.3, W * 0.07);
    } else if (st.trim === 'round') {
      ctx.beginPath(); ctx.ellipse(L * 0.1, 0, L * 0.16, W * 0.16, 0, 0, Math.PI * 2); ctx.fill();
    } else { // jagged
      for (let i = 0; i < 3; i++) {
        ctx.beginPath();
        const x = -L * 0.2 + i * L * 0.16;
        ctx.moveTo(x, -W * 0.3); ctx.lineTo(x + L * 0.08, -W * 0.18); ctx.lineTo(x, -W * 0.06);
        ctx.fill();
      }
    }
    ctx.globalAlpha = 1;
    // cockpit glow
    ctx.fillStyle = '#bfe8ff';
    ctx.shadowColor = '#bfe8ff'; ctx.shadowBlur = 4;
    ctx.beginPath(); ctx.ellipse(L * 0.28, 0, L * 0.05, W * 0.08, 0, 0, Math.PI * 2); ctx.fill();
    ctx.shadowBlur = 0;
    // windows
    ctx.fillStyle = 'rgba(255,240,200,0.9)';
    for (let i = 0; i < (cls === 'battleship' ? 14 : cls === 'cruiser' ? 9 : 5); i++) {
      ctx.fillRect(rng.range(-L * 0.35, L * 0.2), rng.range(-W * 0.25, W * 0.25), 1.4, 1.4);
    }
    ctx.restore();
    // outline
    hullPath(ctx, cls, L, W);
    ctx.strokeStyle = 'rgba(10,12,18,0.9)';
    ctx.lineWidth = 1.5;
    ctx.stroke();
    // engine nozzle plate (rear)
    ctx.fillStyle = '#1a1e28';
    ctx.fillRect(-L * 0.47, -W * 0.16, L * 0.06, W * 0.32);
    return c;
}

// ---- Planets ---------------------------------------------------------------------
const PLANET_DEF = {
  temperate: { ocean: [26, 68, 138], land: [74, 110, 60], land2: [130, 110, 70], atm: [120, 180, 255], clouds: true, caps: true },
  barren: { base: [122, 106, 88], base2: [88, 76, 64], atm: [150, 130, 110], craters: true },
  'gas giant': { bands: [[214, 178, 106], [168, 122, 70], [230, 205, 150], [140, 96, 60]], atm: [230, 200, 140] },
  ice: { base: [196, 224, 244], base2: [150, 190, 230], atm: [180, 220, 255], cracks: [90, 140, 200] },
  lava: { base: [40, 32, 30], base2: [70, 48, 40], atm: [255, 120, 60], glow: [255, 110, 40] },
  oceanic: { base: [20, 60, 140], base2: [40, 110, 190], atm: [100, 160, 255], swirl: true },
  toxic: { base: [110, 150, 50], base2: [80, 110, 40], atm: [160, 220, 90], swirl: true },
};

export function getPlanetSprite(ptype, variant = 0) {
  return cached(`planet:${ptype}:${variant}`, () => {
    const def = PLANET_DEF[ptype] ?? PLANET_DEF.barren;
    const S = 160, R = 68, cx = S / 2, cy = S / 2;
    const c = canvas(S);
    const ctx = c.getContext('2d');
    const nz = makeNoise2D(hashString('pl' + ptype + variant * 977));
    const img = ctx.createImageData(S, S);
    const d = img.data;
    // light direction (top-left-front)
    const lx = -0.55, ly = -0.55, lz = 0.62;
    for (let y = 0; y < S; y++) {
      for (let x = 0; x < S; x++) {
        const dx = (x - cx) / R, dy = (y - cy) / R;
        const r2 = dx * dx + dy * dy;
        const i = (y * S + x) * 4;
        if (r2 > 1) { d[i + 3] = 0; continue; }
        const z = Math.sqrt(1 - r2);
        let r, g, b, emissive = 0;
        if (def.bands) {
          const turb = nz.fbm(dx * 2.5 + 7, dy * 5, 4) * 0.7;
          const band = Math.sin((dy + turb) * 9 + variant * 2);
          const idx = Math.abs(Math.floor((dy + turb) * 4.5 + 20)) % def.bands.length;
          const c2 = def.bands[idx];
          const mix = 0.5 + 0.5 * band;
          r = c2[0] * (0.8 + 0.25 * mix); g = c2[1] * (0.8 + 0.25 * mix); b = c2[2] * (0.8 + 0.25 * mix);
        } else if (ptype === 'temperate') {
          const h = nz.fbm(dx * 2.6 + 3, dy * 2.6, 5);
          if (h > 0.52) {
            const t = Math.min(1, (h - 0.52) * 5);
            r = def.land[0] + (def.land2[0] - def.land[0]) * t;
            g = def.land[1] + (def.land2[1] - def.land[1]) * t;
            b = def.land[2] + (def.land2[2] - def.land[2]) * t;
          } else {
            const depth = Math.min(1, (0.52 - h) * 4);
            r = def.ocean[0] * (1 - depth * 0.5); g = def.ocean[1] * (1 - depth * 0.4); b = def.ocean[2] * (1 - depth * 0.2);
          }
          if (def.caps && Math.abs(dy) > 0.72 + nz.noise(dx * 4, 0) * 0.08) { r = 235; g = 240; b = 245; }
          if (def.clouds) {
            const cl = nz.fbm(dx * 3.2 + 40, dy * 3.2 + 9, 4);
            if (cl > 0.55) { const a = Math.min(1, (cl - 0.55) * 4) * 0.85; r = r * (1 - a) + 255 * a; g = g * (1 - a) + 255 * a; b = b * (1 - a) + 255 * a; }
          }
        } else if (ptype === 'lava') {
          const h = nz.fbm(dx * 3, dy * 3, 4);
          r = def.base[0] + h * 40; g = def.base[1] + h * 30; b = def.base[2] + h * 25;
          const cr = nz.ridge(dx * 4 + 11, dy * 4, 3);
          if (cr < 0.14) { const glow = (0.14 - cr) / 0.14; r = def.glow[0]; g = def.glow[1] * glow; b = 20; emissive = glow; }
        } else if (ptype === 'ice') {
          const h = nz.fbm(dx * 3, dy * 2.4, 4);
          r = def.base[0] - h * 40; g = def.base[1] - h * 30; b = def.base[2] - h * 15;
          const cr = nz.ridge(dx * 5 + 5, dy * 5, 3);
          if (cr < 0.1) { r = def.cracks[0]; g = def.cracks[1]; b = def.cracks[2]; }
        } else {
          // barren / oceanic / toxic generic fbm
          const h = nz.fbm(dx * 3 + variant, dy * 3, 5);
          const b1 = def.base ?? def.ocean, b2 = def.base2 ?? def.land;
          r = b1[0] + (b2[0] - b1[0]) * h; g = b1[1] + (b2[1] - b1[1]) * h; b = b1[2] + (b2[2] - b1[2]) * h;
          if (def.swirl) {
            const s = nz.fbm(dx * 5 + 20, dy * 5, 3);
            r += (s - 0.5) * 50; g += (s - 0.5) * 50; b += (s - 0.5) * 40;
          }
        }
        // sphere lighting
        const diff = Math.max(0, dx * lx + dy * ly + z * lz);
        const light = 0.22 + 0.95 * diff;
        r *= light; g *= light; b *= light;
        // emissive (lava cracks ignore darkness)
        if (emissive > 0) { r = Math.max(r, def.glow[0] * emissive); g = Math.max(g, def.glow[1] * emissive * 0.6); }
        // atmosphere rim
        const rim = Math.pow(1 - z, 2.2) * 0.7;
        r += def.atm[0] * rim; g += def.atm[1] * rim; b += def.atm[2] * rim;
        d[i] = Math.min(255, r); d[i + 1] = Math.min(255, g); d[i + 2] = Math.min(255, b); d[i + 3] = 255;
      }
    }
    ctx.putImageData(img, 0, 0);
    // craters for barren/moon
    if (def.craters) {
      const rng = makeRng(hashString('cr' + ptype + variant));
      ctx.save();
      ctx.beginPath(); ctx.arc(cx, cy, R, 0, Math.PI * 2); ctx.clip();
      for (let i = 0; i < 22; i++) {
        const a = rng.range(0, Math.PI * 2), rr = rng.range(0, R * 0.85);
        const px = cx + Math.cos(a) * rr, py = cy + Math.sin(a) * rr, pr = rng.range(2, 7);
        ctx.fillStyle = 'rgba(0,0,0,0.25)';
        ctx.beginPath(); ctx.arc(px, py, pr, 0, Math.PI * 2); ctx.fill();
        ctx.strokeStyle = 'rgba(255,255,255,0.12)';
        ctx.beginPath(); ctx.arc(px - pr * 0.2, py - pr * 0.2, pr, Math.PI * 0.9, Math.PI * 1.9); ctx.stroke();
      }
      ctx.restore();
    }
    return c;
  });
}

// ---- Asteroids ---------------------------------------------------------------------
export function getAsteroidSprite(variant) {
  return cached(`ast:${variant}`, () => {
    const S = 56;
    const c = canvas(S);
    const ctx = c.getContext('2d');
    const rng = makeRng(hashString('ast' + variant));
    const cx = S / 2, cy = S / 2, R = S * 0.42;
    const verts = [];
    const n = 11;
    for (let i = 0; i < n; i++) {
      const a = (i / n) * Math.PI * 2;
      verts.push([Math.cos(a) * R * rng.range(0.62, 1.05), Math.sin(a) * R * rng.range(0.62, 1.05)]);
    }
    ctx.beginPath();
    verts.forEach(([x, y], i) => i === 0 ? ctx.moveTo(cx + x, cy + y) : ctx.lineTo(cx + x, cy + y));
    ctx.closePath();
    const g = ctx.createLinearGradient(cx - R, cy - R, cx + R, cy + R);
    g.addColorStop(0, '#a89a86');
    g.addColorStop(0.5, '#6f6459');
    g.addColorStop(1, '#3a352f');
    ctx.fillStyle = g;
    ctx.fill();
    ctx.save();
    ctx.clip();
    // shading facets
    ctx.fillStyle = 'rgba(255,255,255,0.07)';
    ctx.beginPath(); ctx.moveTo(cx - R, cy - R); ctx.lineTo(cx + R * 0.3, cy - R * 0.2); ctx.lineTo(cx - R * 0.2, cy + R * 0.4); ctx.closePath(); ctx.fill();
    // craters
    for (let i = 0; i < 6; i++) {
      const px = cx + rng.range(-R * 0.6, R * 0.6), py = cy + rng.range(-R * 0.6, R * 0.6), pr = rng.range(1.5, 4.5);
      ctx.fillStyle = 'rgba(0,0,0,0.3)';
      ctx.beginPath(); ctx.arc(px, py, pr, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = 'rgba(255,255,255,0.15)';
      ctx.beginPath(); ctx.arc(px - pr * 0.2, py - pr * 0.2, pr, Math.PI, Math.PI * 1.8); ctx.stroke();
    }
    ctx.restore();
    ctx.strokeStyle = 'rgba(20,16,12,0.8)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    verts.forEach(([x, y], i) => i === 0 ? ctx.moveTo(cx + x, cy + y) : ctx.lineTo(cx + x, cy + y));
    ctx.closePath(); ctx.stroke();
    return c;
  });
}

// ---- Stars ---------------------------------------------------------------------------
export function getStarSprite(color) {
  return cached(`star:${color}`, () => {
    const S = 256;
    const c = canvas(S);
    const ctx = c.getContext('2d');
    const cx = S / 2, cy = S / 2;
    // outer glow
    let g = ctx.createRadialGradient(cx, cy, 0, cx, cy, S / 2);
    g.addColorStop(0, color);
    g.addColorStop(0.12, color + 'ee');
    g.addColorStop(0.35, color + '55');
    g.addColorStop(1, 'transparent');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, S, S);
    // hot core
    g = ctx.createRadialGradient(cx, cy, 0, cx, cy, S * 0.14);
    g.addColorStop(0, '#ffffff');
    g.addColorStop(0.6, color);
    g.addColorStop(1, color + '00');
    ctx.fillStyle = g;
    ctx.beginPath(); ctx.arc(cx, cy, S * 0.14, 0, Math.PI * 2); ctx.fill();
    // cross flare
    ctx.globalAlpha = 0.5;
    const flare = ctx.createLinearGradient(cx - S * 0.45, cy, cx + S * 0.45, cy);
    flare.addColorStop(0, 'transparent'); flare.addColorStop(0.5, '#ffffffaa'); flare.addColorStop(1, 'transparent');
    ctx.fillStyle = flare;
    ctx.fillRect(cx - S * 0.45, cy - 1.5, S * 0.9, 3);
    ctx.save();
    ctx.translate(cx, cy); ctx.rotate(Math.PI / 2); ctx.translate(-cx, -cy);
    ctx.fillRect(cx - S * 0.45, cy - 1.5, S * 0.9, 3);
    ctx.restore();
    ctx.globalAlpha = 1;
    return c;
  });
}

// ---- Stations -------------------------------------------------------------------------
export function getStationSprite(factionType, faction) {
  return cached(`station:${factionType}:${faction}`, () => {
    const art = STATION_ART[factionType];
    const img = art && getImage(art.img);
    if (img) return composeFlat(img, 128, art.hue[faction] ?? 0);
    return makeProceduralStation(factionType, faction);
  });
}

function makeProceduralStation(factionType, faction) {
    const st = STYLE[faction] ?? STYLE.aurelian;
    const S = 128;
    const c = canvas(S);
    const ctx = c.getContext('2d');
    const cx = S / 2, cy = S / 2;
    const rng = makeRng(hashString('st' + faction));
    const metal = (x0, y0, x1, y1) => {
      const g = ctx.createLinearGradient(x0, y0, x1, y1);
      g.addColorStop(0, '#b8bcc4'); g.addColorStop(0.5, st.hull); g.addColorStop(1, st.dark);
      return g;
    };
    if (factionType === 'police') {
      // fortress: square core + 4 towers
      ctx.fillStyle = metal(cx - 20, cy - 20, cx + 20, cy + 20);
      ctx.fillRect(cx - 18, cy - 18, 36, 36);
      for (const [dx, dy] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) {
        ctx.fillStyle = metal(cx + dx * 30 - 8, cy + dy * 30 - 8, cx + dx * 30 + 8, cy + dy * 30 + 8);
        ctx.fillRect(cx + dx * 30 - 7, cy + dy * 30 - 7, 14, 14);
        ctx.strokeStyle = '#14181f'; ctx.strokeRect(cx + dx * 30 - 7, cy + dy * 30 - 7, 14, 14);
        ctx.beginPath(); ctx.moveTo(cx + dx * 12, cy + dy * 12); ctx.lineTo(cx + dx * 24, cy + dy * 24);
        ctx.strokeStyle = '#2a2f3a'; ctx.lineWidth = 4; ctx.stroke(); ctx.lineWidth = 1;
      }
    } else if (factionType === 'sisters') {
      // white double ring sanctuary
      for (const [r, w] of [[34, 5], [22, 4]]) {
        ctx.strokeStyle = metal(cx - r, cy - r, cx + r, cy + r);
        ctx.lineWidth = w;
        ctx.beginPath(); ctx.arc(cx, cy, r, 0, Math.PI * 2); ctx.stroke();
      }
      ctx.lineWidth = 1;
      ctx.fillStyle = metal(cx - 8, cy - 8, cx + 8, cy + 8);
      ctx.beginPath(); ctx.arc(cx, cy, 9, 0, Math.PI * 2); ctx.fill();
    } else if (factionType === 'pirate') {
      // makeshift asymmetric
      ctx.fillStyle = metal(cx - 20, cy - 14, cx + 20, cy + 14);
      ctx.fillRect(cx - 22, cy - 10, 34, 20);
      ctx.fillRect(cx - 4, cy - 26, 16, 14);
      ctx.fillRect(cx - 2, cy + 12, 22, 12);
      ctx.strokeStyle = '#14181f';
      ctx.strokeRect(cx - 22, cy - 10, 34, 20);
      ctx.strokeRect(cx - 4, cy - 26, 16, 14);
      ctx.strokeRect(cx - 2, cy + 12, 22, 12);
      // girder
      ctx.strokeStyle = '#2a2f3a'; ctx.lineWidth = 3;
      ctx.beginPath(); ctx.moveTo(cx + 12, cy); ctx.lineTo(cx + 34, cy - 16); ctx.stroke();
      ctx.lineWidth = 1;
      ctx.fillStyle = metal(cx + 28, cy - 22, cx + 40, cy - 10);
      ctx.fillRect(cx + 28, cy - 22, 10, 10);
    } else {
      // empire spindle + ring + arms
      ctx.strokeStyle = metal(cx - 30, cy - 30, cx + 30, cy + 30);
      ctx.lineWidth = 6;
      ctx.beginPath(); ctx.arc(cx, cy, 30, 0, Math.PI * 2); ctx.stroke();
      ctx.lineWidth = 1;
      ctx.fillStyle = metal(cx - 6, cy - 40, cx + 6, cy + 40);
      ctx.fillRect(cx - 5, cy - 42, 10, 84);
      ctx.strokeStyle = '#14181f'; ctx.strokeRect(cx - 5, cy - 42, 10, 84);
      for (let i = 0; i < 4; i++) {
        const a = i * Math.PI / 2 + Math.PI / 4;
        ctx.strokeStyle = '#2a2f3a'; ctx.lineWidth = 4;
        ctx.beginPath(); ctx.moveTo(cx + Math.cos(a) * 8, cy + Math.sin(a) * 8);
        ctx.lineTo(cx + Math.cos(a) * 27, cy + Math.sin(a) * 27); ctx.stroke();
        ctx.lineWidth = 1;
      }
      // solar panels
      ctx.fillStyle = '#1a2a4a';
      ctx.fillRect(cx - 38, cy - 3, 12, 6);
      ctx.fillRect(cx + 26, cy - 3, 12, 6);
      ctx.strokeStyle = '#3a5a8a';
      ctx.strokeRect(cx - 38, cy - 3, 12, 6);
      ctx.strokeRect(cx + 26, cy - 3, 12, 6);
    }
    // windows & accent lights
    ctx.fillStyle = 'rgba(255,240,200,0.95)';
    for (let i = 0; i < 12; i++) ctx.fillRect(cx + rng.range(-24, 24), cy + rng.range(-24, 24), 1.3, 1.3);
    ctx.fillStyle = st.accent;
    ctx.shadowColor = st.accent; ctx.shadowBlur = 6;
    ctx.beginPath(); ctx.arc(cx, cy, 2.5, 0, Math.PI * 2); ctx.fill();
    ctx.shadowBlur = 0;
    return c;
}

// ---- Stargate ---------------------------------------------------------------------------
export function getGateSprite() {
  return cached('gate', () => {
    const img = getImage(GATE_ART.img);
    if (img) return composeFlat(img, 112, GATE_ART.hue);
    return makeProceduralGate();
  });
}

function makeProceduralGate() {
    const S = 112;
    const c = canvas(S);
    const ctx = c.getContext('2d');
    const cx = S / 2, cy = S / 2;
    for (let i = 0; i < 3; i++) {
      const a = (i / 3) * Math.PI * 2 - Math.PI / 2;
      const px = cx + Math.cos(a) * 34, py = cy + Math.sin(a) * 34;
      // strut
      ctx.strokeStyle = '#2a3140'; ctx.lineWidth = 5;
      ctx.beginPath(); ctx.moveTo(cx + Math.cos(a) * 12, cy + Math.sin(a) * 12); ctx.lineTo(px, py); ctx.stroke();
      // pylon
      const g = ctx.createLinearGradient(px - 8, py - 8, px + 8, py + 8);
      g.addColorStop(0, '#9aa4b4'); g.addColorStop(1, '#3a4252');
      ctx.fillStyle = g;
      ctx.save();
      ctx.translate(px, py); ctx.rotate(a + Math.PI / 2);
      ctx.fillRect(-5, -12, 10, 24);
      ctx.strokeStyle = '#14181f'; ctx.lineWidth = 1; ctx.strokeRect(-5, -12, 10, 24);
      ctx.fillStyle = '#7ac2ff';
      ctx.shadowColor = '#7ac2ff'; ctx.shadowBlur = 5;
      ctx.fillRect(-2, -14, 4, 3);
      ctx.restore();
    }
    ctx.shadowBlur = 0;
    // inner ring
    ctx.strokeStyle = '#4a5a72'; ctx.lineWidth = 3;
    ctx.beginPath(); ctx.arc(cx, cy, 16, 0, Math.PI * 2); ctx.stroke();
    return c;
}

// ---- Distant galaxies (background) ---------------------------------------------------------
export function getGalaxySprite(variant) {
  return cached(`galaxy:${variant}`, () => {
    const S = 120;
    const c = canvas(S);
    const ctx = c.getContext('2d');
    const rng = makeRng(hashString('gal' + variant));
    const cx = S / 2, cy = S / 2;
    const hue = rng.pick(['#aabbdd', '#ddccaa', '#ccaadd', '#aaddcc']);
    ctx.save();
    ctx.translate(cx, cy);
    ctx.rotate(rng.range(0, Math.PI));
    ctx.scale(1, rng.range(0.3, 0.55));
    const g = ctx.createRadialGradient(0, 0, 0, 0, 0, S * 0.4);
    g.addColorStop(0, hue + '66');
    g.addColorStop(0.4, hue + '2a');
    g.addColorStop(1, 'transparent');
    ctx.fillStyle = g;
    ctx.beginPath(); ctx.arc(0, 0, S * 0.4, 0, Math.PI * 2); ctx.fill();
    ctx.restore();
    return c;
  });
}

export function prewarm() {
  // generate common sprites at boot
  for (const ship of Object.values(SHIPS)) getShipSprite(ship.id, ship.cls, ship.faction);
  for (const t of Object.keys(PLANET_DEF)) for (let v = 0; v < 3; v++) getPlanetSprite(t, v);
  for (let i = 0; i < 8; i++) getAsteroidSprite(i);
  for (let i = 0; i < 4; i++) getGalaxySprite(i);
  getGateSprite();
}
