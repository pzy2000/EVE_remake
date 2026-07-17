// Economy: prices, cargo, market transactions, fitting. Pure module.
import { SHIPS, MODULES, ITEMS } from '../data/ships.js';
import { hashString, makeRng } from '../core/rng.js';

// Deterministic per-station price factor.
function priceFactor(stationId, itemId, security) {
  const rng = makeRng(hashString(stationId + ':' + itemId));
  let f = 0.85 + rng() * 0.45;
  // scarce supply outside high-sec -> higher prices; ore buys higher in low/null
  f *= 1 + (0.5 - Math.min(security, 0.5)) * 0.5;
  return f;
}

export function stationPrice(station, system, itemId) {
  let base = 0;
  if (MODULES[itemId]) base = MODULES[itemId].price;
  else if (SHIPS[itemId]) base = SHIPS[itemId].price;
  else if (ITEMS[itemId]) base = ITEMS[itemId].basePrice;
  else return 0;
  return Math.max(1, Math.round(base * priceFactor(station.id, itemId, system.security)));
}

export function stationSellPrice(station, system, itemId) {
  return Math.max(1, Math.round(stationPrice(station, system, itemId) * 0.85));
}

// ---- Cargo ------------------------------------------------------------------
export function cargoUsed(player) {
  let v = 0;
  for (const [id, q] of Object.entries(player.cargo)) v += (ITEMS[id]?.volume ?? 1) * q;
  return v;
}

export function cargoCapacity(player) {
  const inst = activeShipInst(player);
  if (!inst) return 0;
  let cap = SHIPS[inst.shipId].cargo;
  for (const m of inst.fitting.low) if (m === 'cargo_expander') cap += MODULES.cargo_expander.cargoBonus;
  return cap;
}

export function addCargo(state, itemId, qty) {
  const p = state.player;
  const vol = (ITEMS[itemId]?.volume ?? 1) * qty;
  if (cargoUsed(p) + vol > cargoCapacity(p) + 1e-9) return false;
  p.cargo[itemId] = (p.cargo[itemId] ?? 0) + qty;
  return true;
}

export function removeCargo(state, itemId, qty) {
  const p = state.player;
  if ((p.cargo[itemId] ?? 0) < qty) return false;
  p.cargo[itemId] -= qty;
  if (p.cargo[itemId] <= 0) delete p.cargo[itemId];
  return true;
}

// ---- Ships & hangar -----------------------------------------------------------
export function activeShipInst(player) {
  return player.ships.find(s => s.instId === player.activeShip) ?? null;
}

export function makeShipInstance(shipId, instId) {
  const def = SHIPS[shipId];
  return {
    instId, shipId, name: def.name,
    fitting: {
      high: Array(def.slots.high).fill(null),
      mid: Array(def.slots.mid).fill(null),
      low: Array(def.slots.low).fill(null),
    },
    hp: { ...def.hp },
  };
}

export function buyShip(state, station, system, shipId) {
  const p = state.player;
  const def = SHIPS[shipId];
  if (!def || def.npcOnly) return { ok: false, msg: 'Not available.' };
  const price = stationPrice(station, system, shipId);
  if (p.credits < price) return { ok: false, msg: 'Insufficient credits.' };
  p.credits -= price;
  const inst = makeShipInstance(shipId, `ship_${Date.now()}_${Math.floor(Math.random() * 1e5)}`);
  p.ships.push(inst);
  return { ok: true, msg: `Purchased ${def.name} for ${price.toLocaleString()} ISK.`, inst };
}

export function buyModule(state, station, system, moduleId) {
  const p = state.player;
  const price = stationPrice(station, system, moduleId);
  if (p.credits < price) return { ok: false, msg: 'Insufficient credits.' };
  p.credits -= price;
  p.hangar[moduleId] = (p.hangar[moduleId] ?? 0) + 1;
  return { ok: true, msg: `Purchased ${MODULES[moduleId].name}.` };
}

export function sellModule(state, station, system, moduleId) {
  const p = state.player;
  if ((p.hangar[moduleId] ?? 0) <= 0) return { ok: false, msg: 'None in hangar.' };
  p.hangar[moduleId]--;
  if (p.hangar[moduleId] <= 0) delete p.hangar[moduleId];
  const price = stationSellPrice(station, system, moduleId);
  p.credits += price;
  return { ok: true, msg: `Sold ${MODULES[moduleId].name} for ${price.toLocaleString()} ISK.` };
}

export function sellItem(state, station, system, itemId, qty) {
  const p = state.player;
  qty = Math.min(qty, p.cargo[itemId] ?? 0);
  if (qty <= 0) return { ok: false, msg: 'Nothing to sell.' };
  removeCargo(state, itemId, qty);
  const total = stationSellPrice(station, system, itemId) * qty;
  p.credits += total;
  return { ok: true, msg: `Sold ${qty}x ${ITEMS[itemId].name} for ${total.toLocaleString()} ISK.` };
}

// ---- Fitting ------------------------------------------------------------------
export function fitModule(state, inst, slotType, slotIndex, moduleId) {
  const p = state.player;
  const mod = MODULES[moduleId];
  if (!mod || mod.slot !== slotType) return { ok: false, msg: 'Wrong slot type.' };
  if ((p.hangar[moduleId] ?? 0) <= 0) return { ok: false, msg: 'Module not in hangar.' };
  if (inst.fitting[slotType][slotIndex]) return { ok: false, msg: 'Slot occupied.' };
  p.hangar[moduleId]--;
  if (p.hangar[moduleId] <= 0) delete p.hangar[moduleId];
  inst.fitting[slotType][slotIndex] = moduleId;
  return { ok: true, msg: `Fitted ${mod.name}.` };
}

export function unfitModule(state, inst, slotType, slotIndex) {
  const p = state.player;
  const moduleId = inst.fitting[slotType][slotIndex];
  if (!moduleId) return { ok: false, msg: 'Slot empty.' };
  inst.fitting[slotType][slotIndex] = null;
  p.hangar[moduleId] = (p.hangar[moduleId] ?? 0) + 1;
  return { ok: true, msg: `Unfitted ${MODULES[moduleId].name}.` };
}

export function setActiveShip(state, instId) {
  const p = state.player;
  if (!p.ships.find(s => s.instId === instId)) return { ok: false, msg: 'Ship not found.' };
  p.activeShip = instId;
  return { ok: true, msg: 'Ship activated.' };
}

export function repairShip(state) {
  const p = state.player;
  const inst = activeShipInst(p);
  if (!inst) return { ok: false, msg: 'No ship.' };
  const def = SHIPS[inst.shipId];
  const missing = (def.hp.shield - inst.hp.shield) + (def.hp.armor - inst.hp.armor) + (def.hp.hull - inst.hp.hull);
  const cost = Math.round(missing * 2);
  if (cost <= 0) return { ok: false, msg: 'Ship is fully repaired.' };
  if (p.credits < cost) return { ok: false, msg: `Repairs cost ${cost.toLocaleString()} ISK.` };
  p.credits -= cost;
  inst.hp = { ...def.hp };
  return { ok: true, msg: `Repaired for ${cost.toLocaleString()} ISK.` };
}
