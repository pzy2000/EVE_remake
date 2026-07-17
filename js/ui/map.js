// Starmap overlay. Browser module.
import { FACTIONS } from '../data/factions.js';
import { secStatusColor } from '../core/utils.js';
import { routeBetween } from '../data/universe.js';

const $ = (id) => document.getElementById(id);
let open = false;

export function initMap(game) {
  $('btn-close-map').onclick = () => toggleMap(game, false);
  $('map-canvas').addEventListener('click', (ev) => {
    const state = game.state;
    const rect = ev.target.getBoundingClientRect();
    const { scale, ox, oy } = mapTransform(ev.target);
    const mx = (ev.clientX - rect.left - ox) / scale;
    const my = (ev.clientY - rect.top - oy) / scale;
    let best = null, bd = 20 / scale;
    for (const sys of Object.values(state.universe.systems)) {
      const d = Math.hypot(sys.x - mx, sys.y - my);
      if (d < bd) { bd = d; best = sys; }
    }
    if (best) {
      state.player.destination = best.id;
      game.log(`Destination set: ${best.name}`, 'info');
      renderMap(game);
    }
  });
}

export function toggleMap(game, force) {
  open = force !== undefined ? force : !open;
  $('map-overlay').classList.toggle('visible', open);
  if (open) renderMap(game);
}
export function isMapOpen() { return open; }

function mapTransform(canvas) {
  const margin = 60;
  const scale = Math.min((canvas.width - margin * 2) / 1000, (canvas.height - margin * 2) / 1000);
  return { scale, ox: (canvas.width - 1000 * scale) / 2, oy: (canvas.height - 1000 * scale) / 2 };
}

export function renderMap(game) {
  const state = game.state;
  const canvas = $('map-canvas');
  canvas.width = canvas.clientWidth; canvas.height = canvas.clientHeight;
  const ctx = canvas.getContext('2d');
  const { scale, ox, oy } = mapTransform(canvas);
  const P = (x, y) => [ox + x * scale, oy + y * scale];
  ctx.fillStyle = '#05070f';
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  const route = state.player.destination
    ? routeBetween(state.universe, state.currentSystemId, state.player.destination) : null;

  // edges
  ctx.lineWidth = 1;
  for (const [a, nbs] of Object.entries(state.universe.adj)) {
    const A = state.universe.systems[a];
    for (const b of nbs) {
      if (b < a) continue;
      const B = state.universe.systems[b];
      const onRoute = route && route.includes(a) && route.includes(b) &&
        Math.abs(route.indexOf(a) - route.indexOf(b)) === 1;
      ctx.strokeStyle = onRoute ? '#3aff88' : '#26314a';
      ctx.lineWidth = onRoute ? 2 : 1;
      ctx.beginPath();
      ctx.moveTo(...P(A.x, A.y)); ctx.lineTo(...P(B.x, B.y)); ctx.stroke();
    }
  }
  // systems
  for (const sys of Object.values(state.universe.systems)) {
    const [x, y] = P(sys.x, sys.y);
    const fac = FACTIONS[sys.faction];
    // faction territory ring
    ctx.strokeStyle = fac?.color ?? '#555';
    ctx.globalAlpha = 0.5; ctx.lineWidth = 2;
    ctx.beginPath(); ctx.arc(x, y, 8, 0, Math.PI * 2); ctx.stroke();
    ctx.globalAlpha = 1;
    ctx.fillStyle = secStatusColor(sys.security);
    ctx.beginPath(); ctx.arc(x, y, sys.capital ? 6 : 4, 0, Math.PI * 2); ctx.fill();
    if (sys.id === state.currentSystemId) {
      ctx.strokeStyle = '#fff'; ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.arc(x, y, 12, 0, Math.PI * 2); ctx.stroke();
    }
    if (sys.id === state.player.destination) {
      ctx.strokeStyle = '#3aff88'; ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.arc(x, y, 12, 0, Math.PI * 2); ctx.stroke();
    }
    ctx.fillStyle = '#9ab'; ctx.font = '10px "Segoe UI"'; ctx.textAlign = 'center';
    ctx.fillText(sys.name, x, y + 20);
  }
  // legend
  const cur = state.universe.systems[state.currentSystemId];
  const dest = state.player.destination ? state.universe.systems[state.player.destination] : null;
  $('map-info').innerHTML =
    `Current: <b>${cur.name}</b> (${cur.security.toFixed(1)}, ${FACTIONS[cur.faction].name})` +
    (dest ? ` — Destination: <b style="color:#3aff88">${dest.name}</b> (${(route?.length ?? 1) - 1} jumps)` : ' — Click a system to set destination');
}
