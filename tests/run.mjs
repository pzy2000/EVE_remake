// Smoke tests for pure logic modules. Run: node tests/run.mjs
import { generateUniverse, routeBetween, findAgent, secClass } from '../js/data/universe.js';
import { FACTIONS, STANDING_MATRIX, EMPIRES, PIRATES, factionRelation } from '../js/data/factions.js';
import { SHIPS, MODULES, ITEMS } from '../js/data/ships.js';
import { newGame, serialize, deserialize, spawnPlayerEntity, playerEntity } from '../js/core/state.js';
import { getEffectiveStanding, modifyStanding, disposition, isCriminalAttack } from '../js/systems/standings.js';
import * as eco from '../js/systems/economy.js';
import * as combat from '../js/systems/combat.js';
import * as npcSys from '../js/systems/npc.js';
import * as missions from '../js/systems/missions.js';

let passed = 0, failed = 0;
function ok(cond, name) {
  if (cond) { passed++; }
  else { failed++; console.error('  FAIL:', name); }
}
function section(name) { console.log('\n== ' + name); }

// ---------- Universe ----------
section('Universe generation');
const uni = generateUniverse(12345);
const sysIds = Object.keys(uni.systems);
ok(sysIds.length === 48, `48 systems (got ${sysIds.length})`);
// connectivity
const reachable = new Set([sysIds[0]]);
const q = [sysIds[0]];
while (q.length) {
  const c = q.shift();
  for (const nb of uni.adj[c]) if (!reachable.has(nb)) { reachable.add(nb); q.push(nb); }
}
ok(reachable.size === sysIds.length, `all systems reachable (${reachable.size}/${sysIds.length})`);
// adjacency symmetric & gates bidirectional
let adjSym = true, gateSym = true, secOk = true, agentsOk = true;
for (const [a, nbs] of Object.entries(uni.adj)) {
  for (const b of nbs) if (!uni.adj[b]?.includes(a)) adjSym = false;
}
for (const sys of Object.values(uni.systems)) {
  if (sys.security < 0 || sys.security > 1) secOk = false;
  for (const g of sys.gates) {
    const to = uni.systems[g.to];
    if (!to || !to.gates.some(g2 => g2.to === sys.id)) gateSym = false;
  }
  for (const st of sys.stations) if (!st.agents.length) agentsOk = false;
}
ok(adjSym, 'adjacency symmetric');
ok(gateSym, 'gates bidirectional');
ok(secOk, 'security in [0,1]');
ok(agentsOk, 'every station has agents');
ok(EMPIRES.every(e => uni.startSystems[e] && uni.systems[uni.startSystems[e]].stations.length >= 3),
  'each empire has a capital with stations');
ok(Object.values(uni.systems).some(s => s.region === 'nullsec' && PIRATES.includes(s.faction)),
  'pirate null-sec exists');
ok(Object.values(uni.systems).some(s => s.faction === 'sisters'), 'sisters enclave exists');
ok(Object.values(uni.systems).every(s => s.belts.length >= 1), 'every system has a belt');
const route = routeBetween(uni, uni.startSystems.aurelian, uni.startSystems.varkhald);
ok(route && route.length >= 2, `route between capitals (${route?.length} hops)`);
// determinism
const uni2 = generateUniverse(12345);
ok(JSON.stringify(Object.keys(uni.systems).map(k => uni.systems[k].name)) ===
   JSON.stringify(Object.keys(uni2.systems).map(k => uni2.systems[k].name)), 'generation deterministic');

// ---------- Factions ----------
section('Factions');
let matrixOk = true;
for (const [a, row] of Object.entries(STANDING_MATRIX)) {
  for (const [b, v] of Object.entries(row)) {
    if (Math.abs((STANDING_MATRIX[b]?.[a] ?? 0) - v) > 0.01) matrixOk = false;
    if (!FACTIONS[b]) matrixOk = false;
  }
}
ok(matrixOk, 'standing matrix symmetric & complete');
ok(EMPIRES.length === 4 && PIRATES.length === 4, 'four empires & four pirate factions');
ok(factionRelation('directorate', 'blood_reavers') === -10, 'Directorate hates pirates');

// ---------- State / save ----------
section('State & save/load');
let state = newGame(777, 'Tester', 'aurelian');
ok(state.player.credits === 50000, 'starting credits');
ok(state.player.standings.aurelian === 1.0, 'starting empire standing');
ok(state.player.ships.length === 1 && state.player.ships[0].shipId === 'acolyte', 'starting frigate');
const json = serialize(state);
const loaded = deserialize(json);
ok(loaded.player.name === 'Tester' && loaded.player.credits === 50000, 'save round-trip player');
ok(Object.keys(loaded.universe.systems).length === 48, 'save round-trip universe regen');
ok(loaded.player.location.dockedAt === state.player.location.dockedAt, 'save round-trip location');

// ---------- Standings ----------
section('Standings & disposition');
state = newGame(777, 'Tester', 'aurelian');
modifyStanding(state, 'kaldari', 5, 'test');
ok(getEffectiveStanding(state.player, 'kaldari') >= 5, 'direct standing applied');
// derived: aurelian effective boosted by kaldari friendship (relation +5)
ok(getEffectiveStanding(state.player, 'aurelian') > 1.0, 'derived standing from allied faction');
ok(disposition(state, 'blood_reavers') === 'hostile', 'pirates hostile by default');
state.player.standings.blood_reavers = 3;
ok(disposition(state, 'blood_reavers') === 'neutral', 'pirates neutral at positive standing');
state.player.standings.blood_reavers = 8;
ok(disposition(state, 'blood_reavers') === 'friendly', 'pirates friendly at high standing');
state.player.standings.meridian = -6;
ok(disposition(state, 'meridian') === 'hostile', 'empire navy hostile below -5');
state.player.criminalTimer = 30;
ok(disposition(state, 'directorate') === 'hostile', 'Directorate hostile when criminal');
state.player.criminalTimer = 0;
ok(disposition(state, 'directorate') === 'neutral', 'Directorate neutral otherwise');
ok(isCriminalAttack(state, 'aurelian') === true, 'attacking empire in high-sec is crime');
ok(isCriminalAttack(state, 'blood_reavers') === false, 'attacking pirates is legal');

// ---------- Economy ----------
section('Economy');
state = newGame(777, 'Tester', 'aurelian');
const sys0 = state.universe.systems[state.currentSystemId];
const st0 = sys0.stations[0];
const p1 = eco.stationPrice(st0, sys0, 'pulse_laser');
const p2 = eco.stationPrice(st0, sys0, 'pulse_laser');
ok(p1 === p2 && p1 > 0, 'station price deterministic');
const c0 = state.player.credits;
const br = eco.buyModule(state, st0, sys0, 'pulse_laser');
ok(br.ok && state.player.credits === c0 - p1 && state.player.hangar.pulse_laser === 1, 'buy module');
const sr = eco.sellModule(state, st0, sys0, 'pulse_laser');
ok(sr.ok && !state.player.hangar.pulse_laser, 'sell module');
// cargo capacity
ok(eco.addCargo(state, 'ferrite', 50), 'add cargo within capacity');
const cap = eco.cargoCapacity(state.player);
ok(!eco.addCargo(state, 'crystalline', Math.ceil(cap) + 1000), 'cargo capacity enforced');
// fitting
const inst = eco.activeShipInst(state.player);
state.player.hangar.shield_extender = 1;
const fr = eco.fitModule(state, inst, 'low', 0, 'shield_extender');
ok(fr.ok && inst.fitting.low[0] === 'shield_extender', 'fit module');
const ur = eco.unfitModule(state, inst, 'low', 0);
ok(ur.ok && state.player.hangar.shield_extender === 1, 'unfit module');
// buy ship
state.player.credits = 1000000;
const bs = eco.buyShip(state, st0, sys0, 'acolyte');
ok(bs.ok && state.player.ships.length === 2, 'buy ship');

// ---------- Combat ----------
section('Combat');
state = newGame(777, 'Tester', 'aurelian');
const pe = spawnPlayerEntity(state, 0, 0);
const npc = combat.createEntity('acolyte', { high: ['pulse_laser'], mid: [], low: [] },
  { kind: 'npc', faction: 'blood_reavers', x: 30, y: 0, ai: { behavior: 'pirate', aggroRange: 350, waypoints: [], wpIndex: 0 } });
state.entities.push(npc);
const sh0 = npc.hp.shield;
combat.applyDamage(state, npc, 50, pe);
ok(npc.hp.shield === sh0 - 50 && npc.hp.armor === npc.maxHp.armor, 'damage hits shield first');
combat.applyDamage(state, npc, npc.maxHp.shield + 50, pe);
ok(npc.hp.shield === 0 && npc.hp.armor < npc.maxHp.armor, 'damage bleeds to armor');
const credBefore = state.player.credits;
combat.applyDamage(state, npc, 99999, pe);
ok(npc.dead, 'ship destroyed at 0 hull');
ok(state.player.credits > credBefore, 'pirate bounty paid');
ok((state.player.standings.blood_reavers ?? 0) < 0, 'standing loss with victim faction');
// weapon cooldown
const npc2 = combat.createEntity('wasp', { high: ['blaster'], mid: [], low: [] },
  { kind: 'npc', faction: 'crimson_hand', x: 20, y: 0, ai: { behavior: 'pirate', aggroRange: 1, waypoints: [], wpIndex: 0 } });
state.entities.push(npc2);
const modInst = pe.modules.find(m => m.def.type === 'weapon');
ok(combat.fireWeapon(state, pe, modInst, npc2) === true, 'weapon fires in range');
ok(modInst.cooldown > 0, 'weapon cooldown set');
ok(combat.fireWeapon(state, pe, modInst, npc2) === false, 'weapon blocked by cooldown');

// ---------- NPC population ----------
section('NPC population');
state = newGame(777, 'Tester', 'aurelian');
spawnPlayerEntity(state, 100, 100);
npcSys.populateSystem(state); // high-sec capital
const navy = state.entities.filter(e => e.kind === 'npc' && e.ai.behavior === 'navy');
ok(navy.length >= 3, `navy patrols in high-sec (${navy.length})`);
ok(state.asteroids.length > 0, `asteroids spawned (${state.asteroids.length})`);
// null-sec
const nullSys = Object.values(state.universe.systems).find(s => s.region === 'nullsec');
state.currentSystemId = nullSys.id;
npcSys.populateSystem(state);
const pirates = state.entities.filter(e => e.kind === 'npc' && e.ai.behavior === 'pirate');
ok(pirates.length >= 4, `pirates in null-sec (${pirates.length})`);
ok(pirates.every(e2 => e2.faction === nullSys.faction), 'null-sec pirates match local faction');
// low-sec
const lowSys = Object.values(state.universe.systems).find(s => s.region === 'lowsec');
state.currentSystemId = lowSys.id;
npcSys.populateSystem(state);
const lowPirates = state.entities.filter(e => e.kind === 'npc' && e.ai.behavior === 'pirate');
ok(lowPirates.length >= 1, `pirates in low-sec belts (${lowPirates.length})`);

// ---------- Missions ----------
section('Missions');
state = newGame(777, 'Tester', 'meridian');
state.missionHooks = { onKill: (npc) => missions.onKill(state, npc) };
const homeSys = state.universe.systems[state.currentSystemId];
const station = homeSys.stations[0];
const secAgent = station.agents.find(a => a.division === 'security') ?? station.agents[0];
// force divisions for test coverage
secAgent.division = 'security';
const offer = missions.generateOffer(state, secAgent, station, homeSys);
ok(offer && offer.type === 'security' && offer.killsRequired > 0, 'security offer generated');
ok(offer.reward.credits > 0 && offer.reward.lp > 0, 'offer has rewards');
const ar = missions.acceptMission(state, offer);
ok(ar.ok && offer.state === 'active', 'accept security mission');
// travel to target system & spawn deadspace
state.currentSystemId = offer.targetSystemId;
state.player.location.systemId = offer.targetSystemId;
spawnPlayerEntity(state, 0, 0);
npcSys.populateSystem(state);
const mNpcs = state.entities.filter(e => e.ai?.missionId === offer.id);
ok(mNpcs.length === offer.killsRequired, `mission NPCs spawned (${mNpcs.length}/${offer.killsRequired})`);
ok(state.beacons.some(b => b.missionId === offer.id), 'deadspace beacon spawned');
const credM = state.player.credits;
for (const n of mNpcs.slice()) combat.killEntity(state, n, playerEntity(state));
ok(offer.state === 'objectives_met', 'objectives met after kills');
missions.completeMission(state, offer);
ok(state.player.credits > credM, 'mission reward paid');
ok((state.player.missionCounts[offer.faction] ?? 0) === 1, 'faction mission counter incremented');

// distribution mission
const distAgent = station.agents.find(a => a.division === 'distribution') ?? station.agents[0];
distAgent.division = 'distribution';
const dOffer = missions.generateOffer(state, distAgent, station, homeSys);
ok(dOffer && dOffer.type === 'distribution' && dOffer.destStationId, 'distribution offer generated');
const ar2 = missions.acceptMission(state, dOffer);
ok(ar2.ok && (state.player.cargo.sealed_cargo ?? 0) >= dOffer.cargoQty, 'mission cargo loaded');
const credD = state.player.credits;
missions.onDock(state, dOffer.destStationId);
ok(dOffer.state === 'done' && !(state.player.cargo.sealed_cargo > 0), 'distribution completed on dock');
ok(state.player.credits > credD, 'distribution reward paid');

// mining mission
const mineAgent = station.agents.find(a => a.division === 'mining') ?? station.agents[0];
mineAgent.division = 'mining';
const mOffer = missions.generateOffer(state, mineAgent, station, homeSys);
ok(mOffer && mOffer.type === 'mining' && mOffer.oreQty > 0, 'mining offer generated');
mOffer.oreQty = 50; // keep within frigate cargo for the flow test
missions.acceptMission(state, mOffer);
state.player.cargo = {}; // clear
ok(eco.addCargo(state, mOffer.oreId, mOffer.oreQty), 'mined ore into cargo');
missions.onDock(state, mOffer.stationId);
ok(mOffer.state === 'done', 'mining completed on dock with ore');

// storyline trigger: complete 5 normal missions for a faction
section('Storyline trigger');
state = newGame(777, 'Tester', 'kaldari');
for (let i = 0; i < 5; i++) {
  const dummy = {
    id: 'dummy' + i, type: 'security', faction: 'kaldari', state: 'objectives_met',
    title: 'Dummy ' + i, reward: { credits: 100, lp: 10, standing: 0.1 },
  };
  state.player.missions.push(dummy);
  missions.completeMission(state, dummy);
}
const story = state.player.missions.find(m => m.type === 'storyline');
ok(!!story, 'storyline mission offered after 5 completions');
ok(story && story.state === 'offered' && story.reward.standing >= 1, 'storyline has big rewards');
// accept & run storyline kill variant flow
if (story.subtype === 'story_kill') {
  missions.acceptMission(state, story);
  state.currentSystemId = story.targetSystemId;
  spawnPlayerEntity(state, 0, 0);
  npcSys.populateSystem(state);
  const sNpcs = state.entities.filter(e => e.ai?.missionId === story.id);
  ok(sNpcs.length === story.killsRequired, 'storyline NPCs spawned');
  ok(sNpcs.some(e => e.ai.elite), 'storyline has elite commander');
} else {
  missions.acceptMission(state, story);
  ok((state.player.cargo.sealed_cargo ?? 0) >= story.cargoQty, 'storyline cargo loaded');
}

// ---------- Summary ----------
console.log(`\n========================================`);
console.log(`PASSED: ${passed}  FAILED: ${failed}`);
process.exit(failed ? 1 : 0);
