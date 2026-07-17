// Game state: new game, serialization (pure; no DOM, no localStorage).
import { generateUniverse } from '../data/universe.js';
import { makeShipInstance, activeShipInst } from '../systems/economy.js';
import { createEntity } from '../systems/combat.js';

const EMPIRE_FRIGATE = { aurelian: 'acolyte', kaldari: 'shrike', meridian: 'wasp', varkhald: 'fang' };
const EMPIRE_WEAPON = { aurelian: 'pulse_laser', kaldari: 'missile_launcher', meridian: 'blaster', varkhald: 'autocannon' };

export function newGame(seed, name, empireId) {
  const universe = generateUniverse(seed);
  const homeSystemId = universe.startSystems[empireId];
  const homeSys = universe.systems[homeSystemId];
  const homeStation = homeSys.stations[0];
  const inst = makeShipInstance(EMPIRE_FRIGATE[empireId], 'ship_start');
  inst.fitting.high[0] = EMPIRE_WEAPON[empireId];
  inst.fitting.high[1] = EMPIRE_WEAPON[empireId];
  inst.fitting.mid[0] = 'shield_booster';
  const player = {
    name, empire: empireId,
    credits: 50000, lp: {}, standings: { [empireId]: 1.0 },
    ships: [inst], activeShip: inst.instId,
    cargo: {}, hangar: { mining_laser: 1 },
    missions: [], missionCounts: {},
    location: { systemId: homeSystemId, dockedAt: homeStation.id, x: homeStation.x, y: homeStation.y },
    homeSystemId, homeStationId: homeStation.id,
    criminalTimer: 0, destination: null,
    stats: { kills: 0, missionsDone: 0, oreMined: 0, jumps: 0 },
  };
  return makeRuntimeState(universe, player);
}

export function makeRuntimeState(universe, player) {
  return {
    version: 1, universe, player,
    currentSystemId: player.location.systemId,
    entities: [], asteroids: [], beacons: [], projectiles: [],
    time: 0, visitCounter: 0,
    selectedId: null, camera: { x: 0, y: 0, zoom: 1 },
    fx: null, log: null, missionHooks: null, economyApi: { makeShipInstance },
    playerDead: false, docked: !!player.location.dockedAt,
  };
}

// Create/refresh the player's ship entity from the active ship instance.
export function spawnPlayerEntity(state, x, y) {
  const p = state.player;
  const inst = activeShipInst(p);
  state.entities = state.entities.filter(e => e.kind !== 'player');
  const e = createEntity(inst.shipId, inst.fitting, {
    kind: 'player', id: 'player', faction: p.empire,
    name: `${p.name} (${inst.name})`,
    x: x ?? p.location.x, y: y ?? p.location.y, hp: inst.hp,
  });
  state.entities.push(e);
  return e;
}

export function playerEntity(state) {
  return state.entities.find(e => e.kind === 'player') ?? null;
}

// Persist ship hp back to the instance (call before dock/jump/save).
export function syncPlayerHp(state) {
  const e = playerEntity(state);
  const inst = activeShipInst(state.player);
  if (e && inst) inst.hp = { ...e.hp };
}

// ---- Serialization -------------------------------------------------------------
export function serialize(state) {
  syncPlayerHp(state);
  return JSON.stringify({
    version: 1,
    seed: state.universe.seed,
    time: state.time,
    currentSystemId: state.currentSystemId,
    player: state.player,
  });
}

export function deserialize(json) {
  const data = typeof json === 'string' ? JSON.parse(json) : json;
  const universe = generateUniverse(data.seed);
  const state = makeRuntimeState(universe, data.player);
  state.time = data.time ?? 0;
  state.currentSystemId = data.player.location.systemId;
  return state;
}
