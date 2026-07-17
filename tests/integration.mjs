// Headless gameplay integration test: simulates the main loop without DOM.
import { newGame, spawnPlayerEntity, playerEntity } from '../js/core/state.js';
import * as combat from '../js/systems/combat.js';
import * as npcSys from '../js/systems/npc.js';
import * as missions from '../js/systems/missions.js';
import * as eco from '../js/systems/economy.js';
import { dist } from '../js/core/utils.js';

let passed = 0, failed = 0;
function ok(cond, name) {
  if (cond) passed++;
  else { failed++; console.error('  FAIL:', name); }
}

function tick(state, dt = 0.05) {
  state.time += dt;
  const pe = playerEntity(state);
  if (pe && !pe.dead) combat.updateEntity(state, pe, dt);
  npcSys.updateNpcs(state, dt);
  for (const e of state.entities) if (e.kind === 'npc') combat.updateEntity(state, e, dt);
  combat.updateProjectiles(state, dt);
}
function run(state, seconds, dt = 0.05) {
  for (let t = 0; t < seconds; t += dt) tick(state, dt);
}

console.log('== Integration: world simulation across region types');
{
  const state = newGame(42, 'Sim', 'varkhald');
  state.missionHooks = { onKill: (n) => missions.onKill(state, n) };
  const regions = ['empire', 'lowsec', 'nullsec', 'sisters'];
  let allOk = true;
  for (const region of regions) {
    const sys = Object.values(state.universe.systems).find(s => s.region === region);
    state.currentSystemId = sys.id;
    spawnPlayerEntity(state, 500, 500);
    npcSys.populateSystem(state);
    try { run(state, 10); } catch (e) { allOk = false; console.error(`   error in ${region}:`, e.message); }
  }
  ok(allOk, '10s simulation in all region types without errors');
}

console.log('== Integration: pirate aggro & combat to the death');
{
  const state = newGame(42, 'Sim', 'aurelian');
  state.missionHooks = { onKill: (n) => missions.onKill(state, n) };
  const nullSys = Object.values(state.universe.systems).find(s => s.region === 'nullsec');
  state.currentSystemId = nullSys.id;
  const pe = spawnPlayerEntity(state, 0, 0);
  npcSys.populateSystem(state);
  // teleport a frigate pirate next to the player
  const pirate = state.entities.find(e => e.ai?.behavior === 'pirate' && e.shipId && e.maxHp.hull < 400);
  ok(!!pirate, 'found a pirate in null-sec');
  pirate.x = pe.x + 100; pirate.y = pe.y;
  run(state, 3);
  ok(pirate.targetId === pe.id || pirate.dead, 'pirate aggroed the player');
  pirate.ai.missionId = 'test_no_flee'; // prevent warp-out so the kill can complete
  // player fights back, keeping in range
  pe.targetId = pirate.id;
  pe.modules.forEach(m => { if (m.def.type === 'weapon') m.active = true; });
  let shots = 0;
  const booster = pe.modules.find(m => m.def.type === 'shield_boost');
  for (let t = 0; t < 180 && !pirate.dead && !pe.dead; t += 0.05) {
    pe.mode = 'approach'; pe.moveTarget = pirate; pe.approachDist = 30;
    combat.activateWeaponsOn(state, pe, pirate);
    if (booster) combat.useUtilityModule(state, pe, booster);
    tick(state);
    shots++;
  }
  ok(pirate.dead, `player destroyed the pirate (${(shots * 0.05).toFixed(1)}s of fire)`);
  ok(state.player.stats.kills >= 1, 'kill recorded');
}

console.log('== Integration: warp travel');
{
  const state = newGame(42, 'Sim', 'kaldari');
  const sys = state.universe.systems[state.currentSystemId];
  const pe = spawnPlayerEntity(state, 0, 0);
  npcSys.populateSystem(state);
  const gate = sys.gates[0];
  combat.startWarp(state, pe, gate.x, gate.y);
  run(state, 30);
  ok(!pe.warp, 'warp completed');
  ok(dist(pe, gate) < 200, `arrived near gate (${dist(pe, gate).toFixed(0)}u)`);
}

console.log('== Integration: mining');
{
  const state = newGame(42, 'Sim', 'meridian');
  const pe = spawnPlayerEntity(state, 0, 0);
  npcSys.populateSystem(state);
  const inst = eco.activeShipInst(state.player);
  inst.fitting.high[1] = 'mining_laser';
  combat.recomputeDerived(pe);
  pe.fitting = inst.fitting; combat.recomputeDerived(pe);
  const ast = state.asteroids.find(a => a.amount > 0);
  ast.x = pe.x + 30; ast.y = pe.y;
  const ml = pe.modules.find(m => m.def.type === 'mining');
  ok(!!ml, 'mining laser fitted');
  let mined = 0;
  for (let i = 0; i < 10; i++) {
    ml.cooldown = 0;
    if (combat.mineCycle(state, pe, ml, ast)) mined++;
  }
  ok(mined > 0 && Object.keys(state.player.cargo).length > 0, `mined ore into cargo (${mined} cycles)`);
  ok(ast.amount < 220, 'asteroid depleted by mining');
}

console.log('== Integration: missiles & projectiles');
{
  const state = newGame(42, 'Sim', 'kaldari'); // shrike has missile_launcher fitted
  const pe = spawnPlayerEntity(state, 0, 0);
  npcSys.populateSystem(state);
  const target = combat.createEntity('fang', { high: [], mid: [], low: [] },
    { kind: 'npc', faction: 'ashfang', x: 100, y: 0, ai: { behavior: 'pirate', aggroRange: 0, waypoints: [], wpIndex: 0 } });
  state.entities.push(target);
  const launcher = pe.modules.find(m => m.def.projectile);
  ok(!!launcher, 'missile launcher fitted on Shrike');
  combat.fireWeapon(state, pe, launcher, target);
  ok(state.projectiles.length === 1, 'missile in flight');
  run(state, 5);
  ok(state.projectiles.length === 0, 'missile resolved');
  ok(target.hp.shield < target.maxHp.shield || target.dead, 'missile dealt damage');
}

console.log('== Integration: player death & respawn');
{
  const state = newGame(42, 'Sim', 'aurelian');
  const pe = spawnPlayerEntity(state, 0, 0);
  npcSys.populateSystem(state);
  combat.applyDamage(state, pe, 999999, null);
  ok(state.playerDead, 'player death flagged');
  combat.respawnPlayer(state);
  ok(!state.playerDead, 'respawn clears death flag');
  ok(state.player.location.dockedAt === state.player.homeStationId, 'respawned docked at home');
  ok(state.player.ships.length >= 1, 'rookie ship issued');
}

console.log('== Integration: full mission loop (accept → travel → kill → complete)');
{
  const state = newGame(99, 'Sim', 'meridian');
  state.missionHooks = { onKill: (n) => missions.onKill(state, n) };
  const sys = state.universe.systems[state.currentSystemId];
  const station = sys.stations[0];
  const agent = station.agents[0];
  agent.division = 'security';
  const offer = missions.generateOffer(state, agent, station, sys);
  missions.acceptMission(state, offer);
  state.currentSystemId = offer.targetSystemId;
  const pe = spawnPlayerEntity(state, 0, 0);
  npcSys.populateSystem(state);
  const foes = state.entities.filter(e => e.ai?.missionId === offer.id);
  ok(foes.length === offer.killsRequired, 'deadspace NPCs present');
  // simulate 60s of combat with the player god-mode
  pe.maxHp = { shield: 1e6, armor: 1e6, hull: 1e6 }; pe.hp = { ...pe.maxHp };
  pe.dmgMult = 50;
  for (const f of foes) { f.x = pe.x + 50; f.y = pe.y; }
  let target = foes[0];
  pe.targetId = target.id;
  pe.modules.forEach(m => { if (m.def.type === 'weapon') m.active = true; });
  for (let t = 0; t < 90 && offer.state === 'active'; t += 0.05) {
    if (!target || target.dead) {
      target = state.entities.find(e => e.ai?.missionId === offer.id && !e.dead);
      pe.targetId = target?.id ?? null;
      if (!target) break;
    }
    combat.activateWeaponsOn(state, pe, target);
    tick(state);
  }
  ok(offer.state === 'objectives_met', 'mission objectives completed via simulated combat');
  const before = state.player.credits;
  missions.completeMission(state, offer);
  ok(state.player.credits === before + offer.reward.credits, 'exact reward paid');
}

console.log(`\n========================================`);
console.log(`PASSED: ${passed}  FAILED: ${failed}`);
process.exit(failed ? 1 : 0);
