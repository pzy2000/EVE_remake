// Station screen: Agents / Market / Fitting / Ships / LP Store. Browser module.
import { FACTIONS } from '../data/factions.js';
import { SHIPS, MODULES, ITEMS } from '../data/ships.js';
import { formatCredits } from '../core/utils.js';
import * as eco from '../systems/economy.js';
import { getEffectiveStanding } from '../systems/standings.js';

const $ = (id) => document.getElementById(id);
let currentTab = 'agents';

export function initStation(game) {
  document.querySelectorAll('#station-tabs button').forEach(b => {
    b.onclick = () => { currentTab = b.dataset.tab; renderStation(game); };
  });
  $('btn-undock').onclick = () => game.actions.undock();
  $('btn-repair').onclick = () => {
    const r = eco.repairShip(game.state);
    game.log(r.msg, r.ok ? 'good' : 'bad');
    renderStation(game);
  };
}

export function openStation(game) {
  const state = game.state;
  const st = game.dockedStation();
  $('station-name').textContent = st.name;
  const fac = FACTIONS[st.faction];
  $('station-faction').textContent = fac.name;
  $('station-faction').style.color = fac.color;
  $('station-screen').classList.add('visible');
  renderStation(game);
}

export function closeStation() {
  $('station-screen').classList.remove('visible');
}

export function renderStation(game) {
  document.querySelectorAll('#station-tabs button').forEach(b =>
    b.classList.toggle('active', b.dataset.tab === currentTab));
  const c = $('station-content');
  if (currentTab === 'agents') c.innerHTML = agentsHtml(game);
  else if (currentTab === 'market') c.innerHTML = marketHtml(game);
  else if (currentTab === 'fitting') c.innerHTML = fittingHtml(game);
  else if (currentTab === 'ships') c.innerHTML = shipsHtml(game);
  else if (currentTab === 'lp') c.innerHTML = lpHtml(game);
  bindStationEvents(game, c);
}

// ---- Agents -------------------------------------------------------------------
function agentsHtml(game) {
  const st = game.dockedStation();
  const state = game.state;
  let html = `<h3>Agents — ${st.name}</h3>`;
  if (!st.agents.length) return html + '<p class="dim">No agents at this station.</p>';
  for (const ag of st.agents) {
    const standing = getEffectiveStanding(state.player, st.faction);
    const required = (ag.level - 1) * 1.5;
    const ok = standing >= required;
    html += `<div class="card">
      <div><b>${ag.name}</b> <span class="dim">— ${ag.division} division, Level ${ag.level}</span></div>
      <div class="dim">Requires effective standing ≥ ${required.toFixed(1)} (yours: ${standing.toFixed(2)})</div>
      <button data-agent="${ag.id}" ${ok ? '' : 'disabled'}>Request Mission</button>
    </div>`;
  }
  return html;
}

// ---- Market ---------------------------------------------------------------------
function marketHtml(game) {
  const state = game.state;
  const st = game.dockedStation();
  const sys = state.universe.systems[state.currentSystemId];
  let html = `<h3>Market — ${formatCredits(state.player.credits)} ISK available</h3><div class="cols"><div>`;
  html += '<h4>Buy Modules</h4>';
  for (const m of Object.values(MODULES)) {
    const price = eco.stationPrice(st, sys, m.id);
    html += `<div class="row"><span>${m.name} <span class="dim">(${m.slot})</span></span>
      <span>${formatCredits(price)} <button data-buy-mod="${m.id}">Buy</button></span></div>`;
  }
  html += '<h4>Buy Ships</h4>';
  const sellable = Object.values(SHIPS).filter(s => !s.npcOnly &&
    (s.faction === st.faction || (st.faction === 'sisters' && s.id === 'pilgrim') || s.cls === 'frigate'));
  for (const s of sellable) {
    const price = eco.stationPrice(st, sys, s.id);
    html += `<div class="row"><span>${s.name} <span class="dim">(${s.cls})</span></span>
      <span>${formatCredits(price)} <button data-buy-ship="${s.id}">Buy</button></span></div>`;
  }
  html += '</div><div>';
  html += '<h4>Sell Cargo</h4>';
  const cargo = Object.entries(state.player.cargo);
  if (!cargo.length) html += '<p class="dim">Cargo hold empty.</p>';
  for (const [id, q] of cargo) {
    if (ITEMS[id]?.noMarket) { html += `<div class="row"><span>${ITEMS[id].name} x${q}</span><span class="dim">mission item</span></div>`; continue; }
    const price = eco.stationSellPrice(st, sys, id);
    html += `<div class="row"><span>${ITEMS[id]?.name ?? id} x${q}</span>
      <span>${formatCredits(price)} ea <button data-sell-item="${id}">Sell All</button></span></div>`;
  }
  html += '<h4>Sell Modules (Hangar)</h4>';
  const hangar = Object.entries(state.player.hangar);
  if (!hangar.length) html += '<p class="dim">Hangar empty.</p>';
  for (const [id, q] of hangar) {
    const price = eco.stationSellPrice(st, sys, id);
    html += `<div class="row"><span>${MODULES[id]?.name ?? id} x${q}</span>
      <span>${formatCredits(price)} <button data-sell-mod="${id}">Sell</button></span></div>`;
  }
  html += '</div></div>';
  return html;
}

// ---- Fitting ---------------------------------------------------------------------
function fittingHtml(game) {
  const state = game.state;
  const inst = eco.activeShipInst(state.player);
  const def = SHIPS[inst.shipId];
  let html = `<h3>Fitting — ${inst.name} <span class="dim">(${def.cls})</span></h3>`;
  html += `<div class="cols"><div>`;
  for (const slotType of ['high', 'mid', 'low']) {
    html += `<h4>${slotType.toUpperCase()} slots</h4>`;
    inst.fitting[slotType].forEach((modId, i) => {
      html += `<div class="row"><span>${modId ? MODULES[modId].name : '<span class="dim">[empty]</span>'}</span>
        ${modId ? `<button data-unfit="${slotType}:${i}">Unfit</button>` : ''}</div>`;
    });
  }
  html += `</div><div><h4>Hangar</h4>`;
  const hangar = Object.entries(state.player.hangar);
  if (!hangar.length) html += '<p class="dim">No modules in hangar. Buy some on the Market.</p>';
  for (const [id, q] of hangar) {
    html += `<div class="row"><span>${MODULES[id].name} x${q} <span class="dim">(${MODULES[id].slot})</span></span>
      <button data-fit="${id}">Fit</button></div>`;
  }
  html += `<h4>Stats</h4><div class="dim">
    Speed ${def.speed} · Warp ${def.warp} · Lock ${def.lockRange}<br>
    Shield ${def.hp.shield} · Armor ${def.hp.armor} · Hull ${def.hp.hull}<br>
    Cargo ${eco.cargoCapacity(state.player)} m³</div>`;
  html += '</div></div>';
  return html;
}

// ---- Ships ------------------------------------------------------------------------
function shipsHtml(game) {
  const state = game.state;
  let html = '<h3>Ship Hangar</h3>';
  for (const inst of state.player.ships) {
    const def = SHIPS[inst.shipId];
    const active = inst.instId === state.player.activeShip;
    html += `<div class="card"><b>${inst.name}</b> <span class="dim">(${def.cls})</span>
      ${active ? '<span class="good">ACTIVE</span>' : `<button data-activate="${inst.instId}">Activate</button>`}
      <div class="dim">HP: S${Math.ceil(inst.hp.shield)} A${Math.ceil(inst.hp.armor)} H${Math.ceil(inst.hp.hull)}</div></div>`;
  }
  return html;
}

// ---- LP Store -----------------------------------------------------------------------
const LP_OFFERS = [
  { id: 'damage_amp', type: 'module', lp: 800, credits: 20000 },
  { id: 'shield_extender', type: 'module', lp: 600, credits: 15000 },
  { id: 'afterburner', type: 'module', lp: 600, credits: 15000 },
];
function lpHtml(game) {
  const state = game.state;
  const st = game.dockedStation();
  const fid = st.faction;
  const lp = state.player.lp[fid] ?? 0;
  let html = `<h3>Loyalty Point Store — ${FACTIONS[fid].name}</h3>
    <p>You have <b class="good">${lp} LP</b> with this faction.</p>`;
  const offers = [...LP_OFFERS];
  const factionShips = Object.values(SHIPS).filter(s => s.faction === fid && !s.npcOnly && s.cls !== 'frigate');
  for (const s of factionShips) offers.push({ id: s.id, type: 'ship', lp: s.cls === 'destroyer' ? 1500 : s.cls === 'cruiser' ? 6000 : 20000, credits: Math.round(s.price * 0.5) });
  for (const o of offers) {
    const name = o.type === 'module' ? MODULES[o.id].name : SHIPS[o.id].name;
    const can = lp >= o.lp && state.player.credits >= o.credits;
    html += `<div class="row"><span>${name} <span class="dim">(${o.type})</span></span>
      <span>${o.lp} LP + ${formatCredits(o.credits)} <button data-lp="${o.id}:${o.type}:${o.lp}:${o.credits}" ${can ? '' : 'disabled'}>Exchange</button></span></div>`;
  }
  return html;
}

// ---- Events --------------------------------------------------------------------------
function bindStationEvents(game, c) {
  const state = game.state;
  const st = game.dockedStation();
  const sys = state.universe.systems[state.currentSystemId];
  c.querySelectorAll('[data-agent]').forEach(b => b.onclick = () => game.actions.talkToAgent(b.dataset.agent));
  c.querySelectorAll('[data-buy-mod]').forEach(b => b.onclick = () => {
    const r = eco.buyModule(state, st, sys, b.dataset.buyMod); game.log(r.msg, r.ok ? 'good' : 'bad'); renderStation(game);
  });
  c.querySelectorAll('[data-buy-ship]').forEach(b => b.onclick = () => {
    const r = eco.buyShip(state, st, sys, b.dataset.buyShip); game.log(r.msg, r.ok ? 'good' : 'bad'); renderStation(game);
  });
  c.querySelectorAll('[data-sell-item]').forEach(b => b.onclick = () => {
    const r = eco.sellItem(state, st, sys, b.dataset.sellItem, 1e9); game.log(r.msg, r.ok ? 'good' : 'bad'); renderStation(game);
  });
  c.querySelectorAll('[data-sell-mod]').forEach(b => b.onclick = () => {
    const r = eco.sellModule(state, st, sys, b.dataset.sellMod); game.log(r.msg, r.ok ? 'good' : 'bad'); renderStation(game);
  });
  c.querySelectorAll('[data-unfit]').forEach(b => b.onclick = () => {
    const [slotType, i] = b.dataset.unfit.split(':');
    const r = eco.unfitModule(state, eco.activeShipInst(state.player), slotType, +i);
    game.log(r.msg, r.ok ? 'good' : 'bad'); renderStation(game);
  });
  c.querySelectorAll('[data-fit]').forEach(b => b.onclick = () => {
    const inst = eco.activeShipInst(state.player);
    const mod = MODULES[b.dataset.fit];
    const idx = inst.fitting[mod.slot].findIndex(x => !x);
    const r = idx < 0 ? { ok: false, msg: `No free ${mod.slot} slot.` } : eco.fitModule(state, inst, mod.slot, idx, mod.id);
    game.log(r.msg, r.ok ? 'good' : 'bad'); renderStation(game);
  });
  c.querySelectorAll('[data-activate]').forEach(b => b.onclick = () => {
    const r = eco.setActiveShip(state, b.dataset.activate); game.log(r.msg, r.ok ? 'good' : 'bad'); renderStation(game);
  });
  c.querySelectorAll('[data-lp]').forEach(b => b.onclick = () => {
    const [id, type, lp, credits] = b.dataset.lp.split(':');
    const fid = st.faction;
    state.player.lp[fid] -= +lp;
    state.player.credits -= +credits;
    if (type === 'module') state.player.hangar[id] = (state.player.hangar[id] ?? 0) + 1;
    else {
      const inst = eco.makeShipInstance(id, `ship_${Date.now()}`);
      state.player.ships.push(inst);
    }
    game.log(`LP exchange: ${type === 'module' ? MODULES[id].name : SHIPS[id].name}`, 'good');
    renderStation(game);
  });
}
