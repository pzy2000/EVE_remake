// HUD, topbar, overview, target bar, log. Browser module.
import { FACTIONS } from '../data/factions.js';
import { SHIPS, MODULES, ITEMS } from '../data/ships.js';
import { dist, formatDistance, formatCredits, secStatusColor, clamp } from '../core/utils.js';
import { disposition } from '../systems/standings.js';
import { cargoUsed, cargoCapacity, activeShipInst } from '../systems/economy.js';
import { playerEntity } from '../core/state.js';

const $ = (id) => document.getElementById(id);
let overviewTimer = 0;

export function initPanels(game) {
  $('btn-map').onclick = () => game.toggleMap();
  $('btn-journal').onclick = () => game.toggleJournal();
  $('btn-character').onclick = () => game.toggleCharacter();
  $('btn-save').onclick = () => game.showSaveMenu();
  $('btn-help').onclick = () => game.showHelp();
  // overview actions
  $('act-warp').onclick = () => game.actions.warpToSelected();
  $('act-approach').onclick = () => game.actions.approachSelected();
  $('act-orbit').onclick = () => game.actions.orbitSelected();
  $('act-lock').onclick = () => game.actions.lockSelected();
  $('act-dock').onclick = () => game.actions.dockOrJumpSelected();
  $('overview-list').addEventListener('click', (ev) => {
    const row = ev.target.closest('[data-id]');
    if (row) game.actions.select(row.dataset.id);
  });
}

export function logMessage(state, msg, cls = 'info') {
  const el = $('log');
  const div = document.createElement('div');
  div.className = 'log-' + cls;
  div.textContent = `[${new Date().toLocaleTimeString('en-US', { hour12: false })}] ${msg}`;
  el.appendChild(div);
  while (el.children.length > 60) el.removeChild(el.firstChild);
  el.scrollTop = el.scrollHeight;
}

export function updatePanels(game, dt) {
  const state = game.state;
  if (!state) return;
  const p = state.player;
  const sys = state.universe.systems[state.currentSystemId];
  const pe = playerEntity(state);

  // topbar
  $('sys-name').textContent = sys.name;
  const secEl = $('sys-sec');
  secEl.textContent = sys.security.toFixed(1);
  secEl.style.color = secStatusColor(sys.security);
  $('sys-faction').textContent = FACTIONS[sys.faction]?.name ?? 'Unknown';
  $('sys-faction').style.color = FACTIONS[sys.faction]?.color ?? '#fff';
  $('credits').textContent = formatCredits(p.credits) + ' ISK';
  const dest = p.destination ? state.universe.systems[p.destination] : null;
  $('destination').textContent = dest ? `▸ ${dest.name}` : '';

  // criminal timer
  const crim = $('criminal');
  if (p.criminalTimer > 0) {
    crim.style.display = 'inline';
    crim.textContent = `CRIMINAL ${Math.ceil(p.criminalTimer)}s`;
  } else crim.style.display = 'none';

  // HUD
  if (pe) {
    const inst = activeShipInst(p);
    $('hud-ship').textContent = `${inst.name} — ${SHIPS[inst.shipId].cls}`;
    setBar('bar-shield', pe.hp.shield, pe.maxHp.shield);
    setBar('bar-armor', pe.hp.armor, pe.maxHp.armor);
    setBar('bar-hull', pe.hp.hull, pe.maxHp.hull);
    $('hud-speed').textContent = pe.warp ? `WARP ${(pe.speed).toFixed(0)}` : `${pe.speed.toFixed(0)} m/s`;
    const cu = cargoUsed(p), cc = cargoCapacity(p);
    $('hud-cargo').textContent = `Cargo ${cu.toFixed(0)}/${cc.toFixed(0)} m³`;
    $('hud-cargo').style.color = cu >= cc ? '#ff5544' : '#9ab';
    updateModules(game, pe);
  }

  // target bar
  const tb = $('targetbar');
  const tgt = pe?.targetId ? (state.entities.find(e => e.id === pe.targetId && !e.dead)
    ?? state.asteroids.find(a => a.id === pe.targetId)) : null;
  if (tgt) {
    tb.style.display = 'block';
    $('target-name').textContent = tgt.name ?? 'Asteroid';
    if (tgt.hp) {
      $('target-hp').style.display = 'block';
      setBar('tbar-shield', tgt.hp.shield, tgt.maxHp.shield);
      setBar('tbar-armor', tgt.hp.armor, tgt.maxHp.armor);
      setBar('tbar-hull', tgt.hp.hull, tgt.maxHp.hull);
    } else {
      $('target-hp').style.display = 'none';
    }
  } else tb.style.display = 'none';

  // overview (throttled)
  overviewTimer -= dt;
  if (overviewTimer <= 0) {
    overviewTimer = 0.3;
    rebuildOverview(game);
  }
}

function setBar(id, val, max) {
  const el = $(id);
  el.style.width = (clamp(val / Math.max(1, max), 0, 1) * 100).toFixed(1) + '%';
  el.nextElementSibling.textContent = `${Math.ceil(val)}/${Math.ceil(max)}`;
}

function updateModules(game, pe) {
  const bar = $('module-bar');
  if (bar.childElementCount !== pe.modules.length) {
    bar.innerHTML = '';
    pe.modules.forEach((m, i) => {
      const b = document.createElement('button');
      b.className = 'mod-btn';
      b.dataset.idx = i;
      b.onclick = () => game.actions.toggleModule(i);
      bar.appendChild(b);
    });
  }
  pe.modules.forEach((m, i) => {
    const b = bar.children[i];
    const cd = m.cooldown > 0 ? ` ${m.cooldown.toFixed(0)}` : '';
    b.innerHTML = `<span class="mod-key">${i + 1}</span>${m.def.name}${cd}`;
    b.classList.toggle('active', !!m.active || (m.moduleId === 'afterburner' && pe.afterburnerOn));
    b.classList.toggle('cooldown', m.cooldown > 0);
  });
}

function rebuildOverview(game) {
  const state = game.state;
  const pe = playerEntity(state);
  if (!pe) return;
  const sys = state.universe.systems[state.currentSystemId];
  const list = $('overview-list');
  const groups = [];

  const hostiles = [], npcs = [];
  for (const e of state.entities) {
    if (e.kind === 'player' || e.dead) continue;
    const disp = disposition(state, e.faction);
    (disp === 'hostile' ? hostiles : npcs).push({
      id: e.id, name: e.name, type: SHIPS[e.shipId].cls, d: dist(pe, e),
      color: disp === 'hostile' ? '#ff5544' : disp === 'friendly' ? '#3aff88' : '#b8c4d4',
    });
  }
  hostiles.sort((a, b) => a.d - b.d); npcs.sort((a, b) => a.d - b.d);
  if (hostiles.length) groups.push(['⚠ Hostiles', hostiles]);
  if (npcs.length) groups.push(['Ships', npcs]);

  const beacons = state.beacons.map(b => ({ id: b.id, name: b.name, type: 'beacon', d: dist(pe, b), color: '#ff9a3a' }));
  if (beacons.length) groups.push(['Mission', beacons]);

  const stations = sys.stations.map(s => ({
    id: s.id, name: s.name, type: 'station', d: dist(pe, s),
    color: FACTIONS[s.faction]?.color ?? '#fff',
  })).sort((a, b) => a.d - b.d);
  if (stations.length) groups.push(['Stations', stations]);

  const gates = sys.gates.map(g => ({ id: g.id, name: g.name, type: 'gate', d: dist(pe, g), color: '#7ab8d9' }))
    .sort((a, b) => a.d - b.d);
  if (gates.length) groups.push(['Stargates', gates]);

  const belts = sys.belts.map(b => ({ id: b.id, name: `${b.name} (${b.ore})`, type: 'belt', d: dist(pe, b), color: '#9a8f7a' }));
  if (belts.length) groups.push(['Asteroid Belts', belts]);

  const asts = state.asteroids.filter(a => a.amount > 0)
    .map(a => ({ id: a.id, name: ITEMS[a.ore].name, type: 'asteroid', d: dist(pe, a), color: '#8a8073' }))
    .sort((a, b) => a.d - b.d).slice(0, 8);
  if (asts.length) groups.push(['Asteroids', asts]);

  const celestials = [];
  for (const pl of sys.planets) {
    celestials.push({ id: pl.id, name: pl.name, type: pl.ptype, d: dist(pe, pl), color: pl.color });
    for (const m of pl.moons) celestials.push({ id: m.id, name: m.name, type: 'moon', d: dist(pe, m), color: '#9a9a8f' });
  }
  celestials.sort((a, b) => a.d - b.d);
  groups.push(['Celestials', celestials]);

  let html = '';
  for (const [title, rows] of groups) {
    html += `<div class="ov-group">${title}</div>`;
    for (const r of rows) {
      const sel = state.selectedId === r.id ? ' selected' : '';
      html += `<div class="ov-row${sel}" data-id="${r.id}">
        <span class="ov-name" style="color:${r.color}">${r.name}</span>
        <span class="ov-type">${r.type}</span>
        <span class="ov-dist">${formatDistance(r.d)}</span></div>`;
    }
  }
  list.innerHTML = html;

  // action button states
  const sel = game.selectedObject();
  const has = !!sel;
  $('act-warp').disabled = !has;
  $('act-approach').disabled = !has;
  $('act-orbit').disabled = !has;
  const lockable = has && (sel.kind === 'ship' || sel.kind === 'asteroid');
  $('act-lock').disabled = !lockable;
  const dockEl = $('act-dock');
  if (has && sel.kind === 'station') {
    dockEl.textContent = 'Dock'; dockEl.disabled = dist(pe, sel) > 40;
  } else if (has && sel.kind === 'gate') {
    dockEl.textContent = 'Jump'; dockEl.disabled = dist(pe, sel) > 35;
  } else {
    dockEl.textContent = 'Dock/Jump'; dockEl.disabled = true;
  }
}
