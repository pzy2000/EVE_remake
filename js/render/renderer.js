// Canvas renderer: sprite-based ships & celestials, layered parallax background.
import { FACTIONS } from '../data/factions.js';
import { SHIPS } from '../data/ships.js';
import { makeRng, hashString } from '../core/rng.js';
import { clamp, formatDistance } from '../core/utils.js';
import { disposition } from '../systems/standings.js';
import { routeBetween } from '../data/universe.js';
import { qualityPreset, getSettings } from '../core/settings.js';
import * as sprites from './sprites.js';

const SHIP_WORLD_LEN = { frigate: 26, destroyer: 34, cruiser: 46, battleship: 62 };
const REGION_TINT = {
  empire: 'rgba(40,70,140,0.10)', lowsec: 'rgba(140,100,40,0.10)',
  nullsec: 'rgba(120,40,80,0.13)', sisters: 'rgba(60,140,130,0.10)',
};

export class Renderer {
  constructor(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.bgCache = null; this.bgKey = '';
    this.resize();
  }
  resize() {
    this.canvas.width = window.innerWidth;
    this.canvas.height = window.innerHeight;
    this.bgCache = null;
  }
  w2s(cam, x, y) {
    const dx = x - cam.x, dy = y - cam.y;
    const c = Math.cos(cam.rot), s = Math.sin(cam.rot);
    return [(dx * c + dy * s) * cam.zoom + this.canvas.width / 2,
            (-dx * s + dy * c) * cam.zoom + this.canvas.height / 2];
  }
  // Inverse of w2s: screen -> world (for input picking).
  s2w(cam, sx, sy) {
    const dx = (sx - this.canvas.width / 2) / cam.zoom;
    const dy = (sy - this.canvas.height / 2) / cam.zoom;
    const c = Math.cos(cam.rot), s = Math.sin(cam.rot);
    return [dx * c - dy * s + cam.x, dx * s + dy * c + cam.y];
  }

  buildBackground(sys) {
    const W = this.canvas.width, H = this.canvas.height;
    const tile = Math.hypot(W, H) + 300; // diagonal so rotated corners stay covered
    const rng = makeRng(hashString('bg' + sys.id));
    const qp = qualityPreset();
    const layers = [];
    for (let l = 0; l < 3; l++) {
      const stars = [];
      const n = Math.round((150 - l * 35) * qp.starMult);
      for (let i = 0; i < n; i++) {
        stars.push({
          x: rng.range(0, tile), y: rng.range(0, tile),
          r: rng.range(0.5, 1.8 - l * 0.35),
          c: rng.chance(0.85) ? '#ffffff' : rng.pick(['#aaccff', '#ffddaa', '#ffaacc']),
          tw: rng.range(0, Math.PI * 2),
        });
      }
      layers.push({ stars, factor: [0.12, 0.3, 0.55][l] });
    }
    // near dust layer
    const dust = [];
    for (let i = 0; i < qp.dust; i++) {
      dust.push({ x: rng.range(0, tile), y: rng.range(0, tile), r: rng.range(0.4, 1.1) });
    }
    // nebulae in 2 parallax layers
    const nebulae = [];
    const palette = {
      empire: ['#1a2a5a', '#2a3a6a', '#1a3a4a'], lowsec: ['#3a2a1a', '#4a3a1a', '#2a2a3a'],
      nullsec: ['#3a1a3a', '#4a1a2a', '#2a1a4a'], sisters: ['#1a3a3a', '#2a4a4a', '#1a2a4a'],
    }[sys.region] ?? ['#1a2a5a'];
    for (let i = 0; i < qp.nebulae; i++) {
      nebulae.push({
        x: rng.range(0, tile), y: rng.range(0, tile), r: rng.range(220, 520),
        c: rng.pick(palette), layer: rng.chance(0.5) ? 0.07 : 0.16,
      });
    }
    const galaxies = [];
    for (let i = 0; i < qp.galaxies; i++) {
      galaxies.push({ x: rng.range(0, tile), y: rng.range(0, tile), v: rng.int(0, 3), s: rng.range(0.5, 1.1) });
    }
    this.bgCache = { layers, nebulae, galaxies, dust, tile };
    this.bgKey = sys.id + ':' + W + 'x' + H + ':' + getSettings().quality;
  }

  render(state, dt) {
    const { ctx, canvas } = this;
    const cam = state.camera;
    const sys = state.universe.systems[state.currentSystemId];
    const player = state.entities.find(e => e.kind === 'player');
    // camera focus: tracked object (EVE-style "look at") or the player ship
    let focus = null;
    if (cam.focusId) {
      focus = this.findDrawable(state, cam.focusId);
      if (!focus) cam.focusId = null; // target gone (destroyed / left behind)
    }
    focus = focus ?? player ?? { x: 0, y: 0 };
    cam.x += (focus.x - cam.x) * Math.min(1, dt * 5);
    cam.y += (focus.y - cam.y) * Math.min(1, dt * 5);

    const qp = qualityPreset();
    const key = sys.id + ':' + canvas.width + 'x' + canvas.height + ':' + getSettings().quality;
    if (this.bgKey !== key) this.buildBackground(sys);
    const bg = this.bgCache;

    ctx.fillStyle = '#04060e';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    // background layers rotate with the camera (deep-space parallax)
    ctx.save();
    ctx.translate(canvas.width / 2, canvas.height / 2);
    ctx.rotate(-cam.rot);
    ctx.translate(-canvas.width / 2, -canvas.height / 2);

    // nebulae
    for (const neb of bg.nebulae) {
      const sx = this.wrap(neb.x - cam.x * neb.layer, bg.tile) - 150;
      const sy = this.wrap(neb.y - cam.y * neb.layer, bg.tile) - 150;
      const g = ctx.createRadialGradient(sx, sy, 0, sx, sy, neb.r);
      g.addColorStop(0, neb.c + '66');
      g.addColorStop(0.55, neb.c + '2e');
      g.addColorStop(1, 'transparent');
      ctx.fillStyle = g;
      ctx.fillRect(sx - neb.r, sy - neb.r, neb.r * 2, neb.r * 2);
    }
    // distant galaxies
    for (const gx of bg.galaxies) {
      const sx = this.wrap(gx.x - cam.x * 0.1, bg.tile) - 150;
      const sy = this.wrap(gx.y - cam.y * 0.1, bg.tile) - 150;
      const spr = sprites.getGalaxySprite(gx.v);
      const s = 120 * gx.s;
      ctx.drawImage(spr, sx - s / 2, sy - s / 2, s, s);
    }
    // starfield
    for (const layer of bg.layers) {
      for (const s of layer.stars) {
        const sx = this.wrap(s.x - cam.x * layer.factor, bg.tile) - 150;
        const sy = this.wrap(s.y - cam.y * layer.factor, bg.tile) - 150;
        ctx.globalAlpha = 0.55 + 0.45 * Math.sin(state.time * 1.6 + s.tw);
        ctx.fillStyle = s.c;
        ctx.beginPath(); ctx.arc(sx, sy, s.r, 0, Math.PI * 2); ctx.fill();
      }
    }
    ctx.globalAlpha = 1;
    // near dust
    ctx.fillStyle = '#8a94a8';
    for (const d of bg.dust) {
      const sx = this.wrap(d.x - cam.x * 0.85, bg.tile) - 150;
      const sy = this.wrap(d.y - cam.y * 0.85, bg.tile) - 150;
      ctx.globalAlpha = 0.35;
      ctx.fillRect(sx, sy, d.r, d.r);
    }
    ctx.globalAlpha = 1;
    ctx.restore(); // end rotated background

    // star (sprite + slow corona shimmer)
    const [stx, sty] = this.w2s(cam, 0, 0);
    const starSpr = sprites.getStarSprite(sys.star.color);
    const starSize = sys.star.r * 7 * cam.zoom;
    ctx.drawImage(starSpr, stx - starSize / 2, sty - starSize / 2, starSize, starSize);
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    ctx.globalAlpha = 0.22 + 0.1 * Math.sin(state.time * 0.7);
    ctx.translate(stx, sty);
    ctx.rotate(state.time * 0.05 - cam.rot);
    ctx.drawImage(starSpr, -starSize / 2, -starSize / 2, starSize, starSize);
    ctx.restore();

    // route next-hop gate
    let nextGateId = null;
    if (state.player.destination && state.player.destination !== sys.id) {
      const route = routeBetween(state.universe, sys.id, state.player.destination);
      if (route && route.length > 1) {
        const g = sys.gates.find(g => g.to === route[1]);
        if (g) nextGateId = g.id;
      }
    }

    for (const p of sys.planets) {
      this.drawPlanet(state, p);
      for (const m of p.moons) this.drawMoon(state, m);
    }
    for (const a of state.asteroids) this.drawAsteroid(state, a);
    for (const st of sys.stations) this.drawStation(state, st);
    for (const g of sys.gates) this.drawGate(state, g, g.id === nextGateId);
    for (const b of state.beacons) this.drawBeacon(state, b);
    for (const e of state.entities) if (!e.dead) this.drawShip(state, e, dt);

    // projectiles
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (const pr of state.projectiles) {
      const [px, py] = this.w2s(cam, pr.x, pr.y);
      const g = ctx.createRadialGradient(px, py, 0, px, py, 5 * cam.zoom);
      g.addColorStop(0, '#ffeebb'); g.addColorStop(1, 'transparent');
      ctx.fillStyle = g;
      ctx.beginPath(); ctx.arc(px, py, 5 * cam.zoom, 0, Math.PI * 2); ctx.fill();
    }
    ctx.restore();

    // effects in world space (same mapping as w2s: translate -> scale -> rotate -> translate)
    ctx.save();
    ctx.translate(canvas.width / 2, canvas.height / 2);
    ctx.scale(cam.zoom, cam.zoom);
    ctx.rotate(-cam.rot);
    ctx.translate(-cam.x, -cam.y);
    state.fx?.draw(ctx);
    ctx.restore();

    this.drawNavAids(state, player);
    this.drawSelection(state, player);
    this.drawEdgeIndicators(state, player);

    // warp tunnel
    if (player?.warp) {
      const lines = qp.warpLines;
      ctx.save();
      ctx.translate(canvas.width / 2, canvas.height / 2);
      ctx.strokeStyle = 'rgba(150,200,255,0.15)';
      for (let i = 0; i < lines; i++) {
        const a = (i / lines) * Math.PI * 2 + state.time * 0.4;
        const r1 = 60 + ((i * 131 + state.time * 1100) % (canvas.width / 2));
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(Math.cos(a) * r1, Math.sin(a) * r1);
        ctx.lineTo(Math.cos(a) * (r1 + 70), Math.sin(a) * (r1 + 70));
        ctx.stroke();
      }
      // light streaks rushing past (textured)
      const trace = sprites.getFxSprite('trace_01');
      if (trace) {
        ctx.globalCompositeOperation = 'lighter';
        for (let i = 0; i < 14; i++) {
          const a = (i / 14) * Math.PI * 2 + state.time * 0.9;
          const r = 90 + ((i * 197 + state.time * 1400) % (canvas.width / 2));
          const len = 90 + (i % 3) * 40;
          ctx.save();
          ctx.translate(Math.cos(a) * r, Math.sin(a) * r);
          ctx.rotate(a);
          ctx.globalAlpha = 0.2;
          ctx.drawImage(trace, -len / 2, -6, len, 12);
          ctx.restore();
        }
        ctx.globalAlpha = 1;
      }
      ctx.restore();
    }

    // region color grading
    ctx.fillStyle = REGION_TINT[sys.region] ?? 'transparent';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    // screen flashes
    if (state.fx) {
      if (state.fx.flashT > 0) {
        ctx.fillStyle = `rgba(255,240,220,${state.fx.flashT * 0.8})`;
        ctx.fillRect(0, 0, canvas.width, canvas.height);
      }
      if (state.fx.alarmT > 0) {
        const a = Math.abs(Math.sin(state.time * 8)) * 0.22 * Math.min(1, state.fx.alarmT);
        ctx.fillStyle = `rgba(255,40,40,${a})`;
        ctx.fillRect(0, 0, canvas.width, canvas.height);
      }
    }
    // vignette
    if (qp.vignette) {
      const vg = ctx.createRadialGradient(canvas.width / 2, canvas.height / 2, canvas.height / 2.6,
        canvas.width / 2, canvas.height / 2, canvas.height);
      vg.addColorStop(0, 'transparent'); vg.addColorStop(1, 'rgba(0,0,0,0.5)');
      ctx.fillStyle = vg;
      ctx.fillRect(0, 0, canvas.width, canvas.height);
    }
  }

  wrap(v, tile) { return ((v % tile) + tile) % tile; }

  bracket(ctx, x, y, r) {
    const l = r * 0.45;
    ctx.beginPath();
    for (const [dx, dy] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) {
      ctx.moveTo(x + dx * r, y + dy * r - dy * l);
      ctx.lineTo(x + dx * r, y + dy * r);
      ctx.lineTo(x + dx * r - dx * l, y + dy * r);
    }
    ctx.stroke();
  }

  findDrawable(state, id) {
    if (!id) return null;
    const sys = state.universe.systems[state.currentSystemId];
    const e = state.entities.find(e => e.id === id && !e.dead);
    if (e) return { ...e, bracketR: 16 };
    const a = state.asteroids.find(a => a.id === id);
    if (a) return { ...a, name: 'Asteroid', bracketR: a.r };
    const b = state.beacons.find(b => b.id === id);
    if (b) return { ...b, bracketR: 16 };
    for (const p of sys.planets) {
      if (p.id === id) return { ...p, bracketR: p.r };
      const m = p.moons.find(m => m.id === id);
      if (m) return { ...m, bracketR: m.r };
    }
    const st = sys.stations.find(s => s.id === id);
    if (st) return { ...st, bracketR: 26 };
    const g = sys.gates.find(g => g.id === id);
    if (g) return { ...g, bracketR: 22 };
    return null;
  }

  drawSelection(state, player) {
    const { ctx } = this;
    const cam = state.camera;
    const sel = this.findDrawable(state, state.selectedId);
    if (sel) {
      const [sx, sy] = this.w2s(cam, sel.x, sel.y);
      const r = (sel.bracketR ?? 18) * cam.zoom + 6;
      ctx.strokeStyle = '#e8e8e8'; ctx.lineWidth = 1.2;
      this.bracket(ctx, sx, sy, r);
      ctx.fillStyle = '#cfd8e3'; ctx.font = '11px "Rajdhani", "Segoe UI"'; ctx.textAlign = 'center';
      ctx.fillText(sel.name, sx, sy + r + 14);
    }
    if (player?.targetId) {
      const t = state.entities.find(e => e.id === player.targetId && !e.dead)
        ?? state.asteroids.find(a => a.id === player.targetId);
      if (t) {
        const [tx2, ty2] = this.w2s(cam, t.x, t.y);
        ctx.strokeStyle = '#ff5544'; ctx.lineWidth = 1.6;
        const rr = 24 * cam.zoom + 4 + Math.sin(state.time * 6) * 2;
        this.bracket(ctx, tx2, ty2, rr);
        ctx.beginPath();
        ctx.moveTo(tx2 - rr - 8, ty2); ctx.lineTo(tx2 - rr + 4, ty2);
        ctx.moveTo(tx2 + rr + 8, ty2); ctx.lineTo(tx2 + rr - 4, ty2);
        ctx.stroke();
      }
    }
  }

  // Orbit ring / approach & warp destination lines (world-space, rotate with camera).
  drawNavAids(state, player) {
    if (!player || player.dead) return;
    const { ctx } = this;
    const cam = state.camera;
    const [px, py] = this.w2s(cam, player.x, player.y);
    ctx.save();
    ctx.lineWidth = 1;
    if (player.mode === 'orbit' && player.moveTarget) {
      const [tx, ty] = this.w2s(cam, player.moveTarget.x, player.moveTarget.y);
      ctx.strokeStyle = 'rgba(90,255,216,0.35)';
      ctx.setLineDash([6, 6]);
      ctx.beginPath(); ctx.arc(tx, ty, player.orbitDist * cam.zoom, 0, Math.PI * 2); ctx.stroke();
      ctx.globalAlpha = 0.5;
      ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(tx, ty); ctx.stroke();
    } else if (player.mode === 'approach' && player.moveTarget) {
      const [tx, ty] = this.w2s(cam, player.moveTarget.x, player.moveTarget.y);
      ctx.strokeStyle = 'rgba(90,255,216,0.4)';
      ctx.setLineDash([6, 6]);
      ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(tx, ty); ctx.stroke();
      // destination marker
      ctx.setLineDash([]);
      ctx.beginPath(); ctx.arc(tx, ty, 5, 0, Math.PI * 2); ctx.stroke();
    }
    if (player.warp) {
      const [tx, ty] = this.w2s(cam, player.warp.tx, player.warp.ty);
      ctx.setLineDash([10, 6]);
      ctx.strokeStyle = 'rgba(150,200,255,0.55)';
      ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(tx, ty); ctx.stroke();
    }
    ctx.restore();
  }

  // Edge arrows for off-screen objects: selection (white), locked target & hostiles (red).
  drawEdgeIndicators(state, player) {
    const { ctx, canvas } = this;
    const cam = state.camera;
    const m = 26;
    const cx = canvas.width / 2, cy = canvas.height / 2;
    const draw = (wx, wy, color, label) => {
      const [sx, sy] = this.w2s(cam, wx, wy);
      if (sx >= -m && sx <= canvas.width + m && sy >= -m && sy <= canvas.height + m) return;
      const ang = Math.atan2(sy - cy, sx - cx);
      const ex = clamp(sx, m, canvas.width - m), ey = clamp(sy, m, canvas.height - m);
      ctx.save();
      ctx.translate(ex, ey);
      ctx.rotate(ang);
      ctx.fillStyle = color;
      ctx.beginPath();
      ctx.moveTo(9, 0); ctx.lineTo(-5, 5.5); ctx.lineTo(-5, -5.5);
      ctx.closePath(); ctx.fill();
      ctx.restore();
      if (label) {
        ctx.fillStyle = color; ctx.font = '10px "Rajdhani", "Segoe UI"'; ctx.textAlign = 'center';
        ctx.fillText(label, ex, ey + 16);
      }
    };
    const sel = this.findDrawable(state, state.selectedId);
    if (sel && sel.id !== player?.id) {
      const d = player ? Math.hypot(sel.x - player.x, sel.y - player.y) : 0;
      draw(sel.x, sel.y, '#e8e8e8', `${sel.name} · ${formatDistance(d)}`);
    }
    let shown = 0;
    for (const e of state.entities) {
      if (e.dead || e.kind === 'player' || shown >= 8) continue;
      const isTarget = player?.targetId === e.id;
      if (!isTarget && disposition(state, e.faction) !== 'hostile') continue;
      draw(e.x, e.y, '#ff5544', isTarget ? e.name : null);
      shown++;
    }
  }

  drawPlanet(state, p) {
    const { ctx, canvas } = this;
    const [x, y] = this.w2s(state.camera, p.x, p.y);
    const variant = hashString(p.id) % 3;
    const spr = sprites.getPlanetSprite(p.ptype, variant);
    const size = p.r * (160 / 68) * 2 * state.camera.zoom;
    if (x < -size || x > canvas.width + size || y < -size || y > canvas.height + size) return;
    ctx.drawImage(spr, x - size / 2, y - size / 2, size, size);
    this.label(state, x, y + size / 2 + 10, p.name, '#8fa3bf');
  }
  drawMoon(state, m) {
    const { ctx } = this;
    const [x, y] = this.w2s(state.camera, m.x, m.y);
    const spr = sprites.getPlanetSprite('barren', hashString(m.id) % 3);
    const size = m.r * (160 / 68) * 2 * state.camera.zoom;
    ctx.drawImage(spr, x - size / 2, y - size / 2, size, size);
    this.label(state, x, y + size / 2 + 8, m.name, '#6b7a8f');
  }
  drawAsteroid(state, a) {
    const { ctx, canvas } = this;
    const [x, y] = this.w2s(state.camera, a.x, a.y);
    const size = a.r * 2.6 * state.camera.zoom;
    if (x < -40 || x > canvas.width + 40 || y < -40 || y > canvas.height + 40) return;
    const spr = sprites.getAsteroidSprite(hashString(a.id) % 8);
    ctx.save();
    ctx.translate(x, y);
    ctx.rotate((hashString(a.id) % 628) / 100 - state.camera.rot);
    if (a.amount <= 0) ctx.globalAlpha = 0.45;
    ctx.drawImage(spr, -size / 2, -size / 2, size, size);
    ctx.restore();
  }
  drawStation(state, st) {
    const { ctx } = this;
    const cam = state.camera;
    const [x, y] = this.w2s(cam, st.x, st.y);
    const ftype = FACTIONS[st.faction]?.type ?? 'empire';
    const spr = sprites.getStationSprite(ftype, st.faction);
    const size = 64 * cam.zoom;
    ctx.drawImage(spr, x - size / 2, y - size / 2, size, size);
    // blinking nav lights
    const blink = Math.sin(state.time * 2.5 + hashString(st.id)) > 0.4;
    if (blink) {
      ctx.save();
      ctx.globalCompositeOperation = 'lighter';
      ctx.fillStyle = '#ff4a4a';
      ctx.beginPath(); ctx.arc(x + size * 0.3, y - size * 0.3, 2 * cam.zoom, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#4aff4a';
      ctx.beginPath(); ctx.arc(x - size * 0.3, y + size * 0.3, 2 * cam.zoom, 0, Math.PI * 2); ctx.fill();
      ctx.restore();
    }
    const col = FACTIONS[st.faction]?.color ?? '#aaa';
    this.label(state, x, y + size / 2 + 12, st.name, col);
  }
  drawGate(state, g, isNext) {
    const { ctx } = this;
    const cam = state.camera;
    const [x, y] = this.w2s(cam, g.x, g.y);
    const spr = sprites.getGateSprite();
    const size = 56 * cam.zoom;
    ctx.drawImage(spr, x - size / 2, y - size / 2, size, size);
    // energy vortex
    const col = isNext ? '#3aff88' : '#7ab8d9';
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    const R = 13 * cam.zoom;
    const swirl = ctx.createRadialGradient(x, y, 0, x, y, R);
    swirl.addColorStop(0, `rgba(160,210,255,${0.55 + 0.25 * Math.sin(state.time * 3)})`);
    swirl.addColorStop(1, 'transparent');
    ctx.fillStyle = swirl;
    ctx.beginPath(); ctx.arc(x, y, R, 0, Math.PI * 2); ctx.fill();
    // rotating spiral arms
    ctx.strokeStyle = col;
    ctx.globalAlpha = 0.6;
    ctx.lineWidth = 1.2;
    for (let i = 0; i < 3; i++) {
      const a0 = state.time * 1.5 + (i / 3) * Math.PI * 2 - cam.rot;
      ctx.beginPath();
      ctx.arc(x, y, R * 0.7, a0, a0 + Math.PI * 0.7);
      ctx.stroke();
    }
    // swirling wormhole textures (counter-rotating, additive)
    const tw1 = sprites.getFxSprite('twirl_01');
    const tw2 = sprites.getFxSprite('twirl_02');
    if (tw1 || tw2) {
      const vs = size * 1.2;
      ctx.globalAlpha = 0.45 + 0.15 * Math.sin(state.time * 3);
      if (tw1) {
        ctx.save(); ctx.translate(x, y); ctx.rotate(state.time * 0.9 - cam.rot);
        ctx.drawImage(tw1, -vs / 2, -vs / 2, vs, vs); ctx.restore();
      }
      if (tw2) {
        ctx.save(); ctx.translate(x, y); ctx.rotate(-state.time * 0.55 - cam.rot);
        ctx.globalAlpha *= 0.7;
        ctx.drawImage(tw2, -vs / 2, -vs / 2, vs, vs); ctx.restore();
      }
    }
    // pulsing core
    const magic = sprites.getFxSprite('magic_01');
    if (magic) {
      const ms = R * (1.6 + 0.3 * Math.sin(state.time * 5));
      ctx.globalAlpha = 0.8;
      ctx.drawImage(magic, x - ms / 2, y - ms / 2, ms, ms);
    }
    ctx.restore();
    if (isNext) {
      ctx.strokeStyle = '#3aff88'; ctx.lineWidth = 1.5;
      this.bracket(ctx, x, y, size * 0.55 + 4 + Math.sin(state.time * 4) * 2);
    }
    this.label(state, x, y + size / 2 + 12, g.name + (isNext ? ' ▸' : ''), col);
  }
  drawBeacon(state, b) {
    const { ctx } = this;
    const [x, y] = this.w2s(state.camera, b.x, b.y);
    const r = 10 * state.camera.zoom;
    ctx.strokeStyle = '#ff9a3a'; ctx.lineWidth = 2;
    ctx.save(); ctx.translate(x, y); ctx.rotate(state.time * 0.8 - state.camera.rot);
    ctx.strokeRect(-r, -r, r * 2, r * 2);
    ctx.restore();
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    const g = ctx.createRadialGradient(x, y, 0, x, y, r * 2);
    g.addColorStop(0, 'rgba(255,154,58,0.4)'); g.addColorStop(1, 'transparent');
    ctx.fillStyle = g;
    ctx.beginPath(); ctx.arc(x, y, r * 2, 0, Math.PI * 2); ctx.fill();
    ctx.restore();
    this.label(state, x, y + r + 14, b.name, '#ff9a3a');
  }

  drawShip(state, e, dt) {
    const { ctx, canvas } = this;
    const cam = state.camera;
    const [x, y] = this.w2s(cam, e.x, e.y);
    if (x < -80 || x > canvas.width + 80 || y < -80 || y > canvas.height + 80) return;
    const def = SHIPS[e.shipId];
    const spr = sprites.getShipSprite(e.shipId, def.cls, e.faction);
    const worldLen = SHIP_WORLD_LEN[def.cls] ?? 26;
    const drawLen = worldLen * cam.zoom * (e.kind === 'player' ? 1.15 : 1);
    const drawH = drawLen * (spr.height / spr.width);

    // engine trail & warp streaks (throttled during warp to avoid additive over-exposure)
    if (e.speed > 1 && state.fx) {
      e.fxAcc = (e.fxAcc ?? 0) + dt;
      if (!e.warp || e.fxAcc >= 0.05) {
        e.fxAcc = 0;
        const bx = e.x - Math.cos(e.angle) * worldLen * 0.42;
        const by = e.y - Math.sin(e.angle) * worldLen * 0.42;
        state.fx.trail(bx, by, e.warp ? '#aaddff' : '#ff9a55', e.warp ? 2.6 : 2.2);
        if (e.warp) state.fx.warpStreak(e.x, e.y, e.angle);
      }
    }

    ctx.save();
    ctx.translate(x, y);
    ctx.rotate(e.angle - cam.rot);
    if (e.warp) ctx.scale(1.5, 0.72);
    ctx.drawImage(spr, -drawLen / 2, -drawH / 2, drawLen, drawH);
    // engine flame & glow (additive, pulsing)
    if (e.speed > 1) {
      ctx.globalCompositeOperation = 'lighter';
      const ex = -drawLen * 0.44;
      const pulse = 1 + Math.sin(state.time * 12 + e.x) * 0.25;
      const flame = sprites.getFxSprite(e.warp ? 'flame_05' : 'flame_01');
      if (flame) {
        const fl = (e.warp ? 2.4 : 1.7) * drawH * pulse;
        const fw = drawH * (e.warp ? 1.0 : 0.75);
        ctx.save();
        ctx.translate(ex, 0);
        ctx.rotate(-Math.PI / 2); // texture "up" -> ship rear (-x)
        ctx.globalAlpha = e.warp ? 0.65 : 0.9;
        ctx.drawImage(flame, -fw / 2, -fl, fw, fl);
        ctx.restore();
        const g = ctx.createRadialGradient(ex, 0, 0, ex, 0, drawH * (e.warp ? 0.34 : 0.42));
        g.addColorStop(0, e.warp ? '#cfe8ff' : '#ffd9a0');
        g.addColorStop(1, 'transparent');
        ctx.fillStyle = g;
        ctx.beginPath(); ctx.arc(ex, 0, drawH * 0.42, 0, Math.PI * 2); ctx.fill();
      } else {
        const gr = (e.warp ? 5 : 3.2) * cam.zoom * pulse;
        const g = ctx.createRadialGradient(ex, 0, 0, ex, 0, gr);
        g.addColorStop(0, e.warp ? '#cfe8ff' : '#ffb066');
        g.addColorStop(1, 'transparent');
        ctx.fillStyle = g;
        ctx.beginPath(); ctx.arc(ex, 0, gr, 0, Math.PI * 2); ctx.fill();
      }
    }
    ctx.restore();

    // player holo outline
    if (e.kind === 'player') {
      ctx.save();
      ctx.globalAlpha = 0.25 + 0.1 * Math.sin(state.time * 3);
      ctx.strokeStyle = '#5affd8'; ctx.lineWidth = 1;
      ctx.beginPath(); ctx.arc(x, y, drawLen * 0.62, 0, Math.PI * 2); ctx.stroke();
      ctx.restore();
    }

    // hp bars
    const damaged = e.hp.shield < e.maxHp.shield || e.hp.armor < e.maxHp.armor || e.hp.hull < e.maxHp.hull;
    const isTarget = state.entities.find(p => p.kind === 'player')?.targetId === e.id;
    if (e.kind !== 'player' && (damaged || isTarget)) {
      const w = 36;
      const bars = [
        ['#4a9edd', e.hp.shield / e.maxHp.shield],
        ['#e0c53a', e.hp.armor / e.maxHp.armor],
        ['#d13a3a', e.hp.hull / e.maxHp.hull],
      ];
      bars.forEach(([c, f], i) => {
        ctx.fillStyle = 'rgba(10,14,22,0.8)'; ctx.fillRect(x - w / 2, y - drawH / 2 - 14 - i * 4, w, 3);
        ctx.fillStyle = c; ctx.fillRect(x - w / 2, y - drawH / 2 - 14 - i * 4, w * clamp(f, 0, 1), 3);
      });
    }
    // label & hostility bracket
    if (e.kind !== 'player') {
      const disp = disposition(state, e.faction);
      const lc = disp === 'hostile' ? '#ff5544' : disp === 'friendly' ? '#3aff88' : '#b8c4d4';
      if (cam.zoom > 0.55 || disp === 'hostile' || isTarget) {
        this.label(state, x, y + drawH / 2 + 12, e.name, lc);
      }
      if (disp === 'hostile') {
        ctx.strokeStyle = '#ff5544'; ctx.lineWidth = 1;
        this.bracket(ctx, x, y, drawLen * 0.55 + 4);
      }
    }
  }

  label(state, x, y, text, color) {
    if (state.camera.zoom < 0.45) return;
    const { ctx } = this;
    ctx.fillStyle = color;
    ctx.font = '10px "Rajdhani", "Segoe UI"';
    ctx.textAlign = 'center';
    ctx.fillText(text, x, y);
  }
}
