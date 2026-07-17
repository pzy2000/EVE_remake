// NPC ships: patrol spawning, pirate AI, Directorate response. Pure module.
import { SHIPS, MODULES } from '../data/ships.js';
import { FACTIONS } from '../data/factions.js';
import { makeRng, hashString } from '../core/rng.js';
import { dist, uid } from '../core/utils.js';
import { createEntity, fireWeapon, useUtilityModule, findEntity } from './combat.js';
import { disposition } from './standings.js';

const FACTION_WEAPON = {
  aurelian: 'pulse_laser', kaldari: 'missile_launcher', meridian: 'blaster',
  varkhald: 'autocannon', sisters: 'pulse_laser', directorate: 'railgun',
  blood_reavers: 'pulse_laser', nathari: 'missile_launcher',
  crimson_hand: 'blaster', ashfang: 'autocannon',
};
const PIRATE_HULLS = {
  blood_reavers: ['acolyte', 'templar', 'dawnbringer'],
  nathari: ['shrike', 'heron', 'rook'],
  crimson_hand: ['wasp', 'anvil', 'mantis'],
  ashfang: ['fang', 'maul', 'broadsword'],
};
const NAVY_HULLS = {
  aurelian: ['acolyte', 'templar', 'dawnbringer'],
  kaldari: ['shrike', 'heron', 'rook'],
  meridian: ['wasp', 'anvil', 'mantis'],
  varkhald: ['fang', 'maul', 'broadsword'],
  sisters: ['pilgrim', 'pilgrim', 'pilgrim'],
};
const NPC_NAMES = {
  pirate: ['Raider', 'Marauder', 'Cutthroat', 'Reaver', 'Outlaw'],
  navy: ['Navy Frigate', 'Navy Destroyer', 'Navy Cruiser'],
  police: ['Response Unit'],
  sisters: ['Sisterhood Vessel'],
};

function npcFitting(shipId, faction) {
  const def = SHIPS[shipId];
  const weapon = FACTION_WEAPON[faction] ?? 'pulse_laser';
  const fitting = { high: [], mid: [], low: [] };
  for (let i = 0; i < def.slots.high; i++) fitting.high.push(weapon);
  // frigates get no booster/amp (keeps fights winnable for new pilots);
  // bigger hulls tank up and hit harder
  const elite = def.cls !== 'frigate';
  for (let i = 0; i < def.slots.mid; i++) fitting.mid.push(i === 0 && elite ? 'shield_booster' : null);
  for (let i = 0; i < def.slots.low; i++) fitting.low.push(i === 0 && elite ? 'damage_amp' : null);
  return fitting;
}

export function makeNpc(state, shipId, faction, x, y, behavior, opts = {}) {
  const cls = SHIPS[shipId].cls;
  const names = NPC_NAMES[behavior === 'pirate' ? 'pirate' : behavior] ?? NPC_NAMES.navy;
  const e = createEntity(shipId, npcFitting(shipId, faction), {
    kind: 'npc', faction, x, y,
    name: `${FACTIONS[faction].short} ${names[Math.min(cls === 'frigate' ? 0 : cls === 'destroyer' ? 1 : 2, names.length - 1)]}`,
    ai: {
      behavior, aggroRange: opts.aggroRange ?? 350,
      waypoints: opts.waypoints ?? [], wpIndex: 0,
      fleeing: false, fleeT: 0, missionId: opts.missionId ?? null,
      elite: opts.elite ?? false,
    },
  });
  if (opts.elite) {
    e.maxHp.shield *= 1.8; e.maxHp.armor *= 1.8; e.maxHp.hull *= 1.8;
    e.hp = { ...e.maxHp }; e.dmgMult *= 1.4; e.name = 'Elite ' + e.name;
  }
  state.entities.push(e);
  return e;
}

// ---- System population ---------------------------------------------------------
export function populateSystem(state) {
  const sys = state.universe.systems[state.currentSystemId];
  const rng = makeRng(hashString(sys.id + ':' + (state.visitCounter++)));
  // clear old npcs & asteroids & beacons
  state.entities = state.entities.filter(e => e.kind === 'player');
  state.asteroids = [];
  state.beacons = [];

  // asteroids around belts (positions deterministic per belt)
  for (const belt of sys.belts) {
    const brng = makeRng(hashString(sys.id + belt.id));
    for (let i = 0; i < belt.asteroids; i++) {
      const ang = brng.range(0, Math.PI * 2);
      const r = brng.range(30, 220);
      state.asteroids.push({
        id: `${belt.id}_a${i}`, beltId: belt.id, ore: belt.ore,
        x: belt.x + Math.cos(ang) * r, y: belt.y + Math.sin(ang) * r,
        r: brng.range(6, 16), amount: brng.int(60, 220),
      });
    }
  }

  const owner = sys.faction;
  const ownerFac = FACTIONS[owner];
  const gates = sys.gates, stations = sys.stations;
  const poi = [...gates, ...stations, ...sys.belts];
  const pickPoi = () => poi.length ? poi[rng.int(0, poi.length - 1)] : { x: 0, y: 0 };

  const spawnPatrol = (faction, hulls, count, behavior, near) => {
    const anchor = near ?? pickPoi();
    const wps = rng.shuffle(poi).slice(0, 3).map(p => ({ x: p.x, y: p.y }));
    for (let i = 0; i < count; i++) {
      const shipId = hulls[Math.min(rng.int(0, count > 2 ? 2 : 1), hulls.length - 1)];
      makeNpc(state, shipId, faction,
        anchor.x + rng.range(-80, 80), anchor.y + rng.range(-80, 80),
        behavior, { waypoints: wps });
    }
  };

  if (sys.region === 'empire') {
    spawnPatrol(owner, NAVY_HULLS[owner], rng.int(2, 3), 'navy', gates[0]);
    spawnPatrol(owner, NAVY_HULLS[owner], rng.int(2, 3), 'navy', stations[0] ?? gates[1]);
    if (sys.capital) spawnPatrol(owner, NAVY_HULLS[owner], 3, 'navy', pickPoi());
    if (rng.chance(0.3) && sys.belts.length) {
      const pf = ownerFac.homePirate;
      spawnPatrol(pf, PIRATE_HULLS[pf], 1, 'pirate', sys.belts[0]);
    }
  } else if (sys.region === 'lowsec') {
    spawnPatrol(owner, NAVY_HULLS[owner] ?? NAVY_HULLS.aurelian, 2, 'navy', stations[0] ?? gates[0]);
    const pf = ownerFac?.homePirate ?? 'blood_reavers';
    for (const belt of sys.belts) spawnPatrol(pf, PIRATE_HULLS[pf], rng.int(1, 3), 'pirate', belt);
  } else if (sys.region === 'nullsec') {
    const pf = owner;
    for (const belt of sys.belts) spawnPatrol(pf, PIRATE_HULLS[pf], rng.int(2, 4), 'pirate', belt);
    if (gates.length) spawnPatrol(pf, PIRATE_HULLS[pf], rng.int(2, 3), 'pirate', gates[rng.int(0, gates.length - 1)]);
  } else if (sys.region === 'sisters') {
    spawnPatrol('sisters', NAVY_HULLS.sisters, 2, 'sisters', stations[0]);
  }

  // mission deadspace beacons & NPCs
  for (const m of state.player.missions) {
    if (m.state !== 'active') continue;
    if ((m.type === 'security' || m.subtype === 'story_kill') && m.targetSystemId === sys.id) {
      const remaining = m.killsRequired - m.kills;
      if (remaining > 0) {
        const brng = makeRng(hashString(m.id));
        const ang = brng.range(0, Math.PI * 2), rad = brng.range(1200, 2400);
        const bx = Math.cos(ang) * rad, by = Math.sin(ang) * rad;
        state.beacons.push({ id: `beacon_${m.id}`, name: `Deadspace: ${m.title}`, type: 'beacon', x: bx, y: by, missionId: m.id });
        const tf = m.targetFaction ?? m.pirateFaction ?? 'blood_reavers';
        const hulls = m.targetIsNavy ? (NAVY_HULLS[tf] ?? NAVY_HULLS.aurelian) : (PIRATE_HULLS[tf] ?? PIRATE_HULLS.blood_reavers);
        for (let i = 0; i < remaining; i++) {
          const elite = m.subtype === 'story_kill' && i === 0;
          const shipId = hulls[Math.min(m.level - 1 + (elite ? 1 : 0), hulls.length - 1)];
          makeNpc(state, shipId, tf, bx + brng.range(-120, 120), by + brng.range(-120, 120),
            m.targetIsNavy ? 'navy' : 'pirate', { missionId: m.id, elite, aggroRange: 600 });
        }
      }
    }
    // storyline haul ambush
    if (m.subtype === 'story_haul' && m.destSystemId === sys.id && !m.ambushSpawned) {
      m.ambushSpawned = true;
      const player = state.entities.find(e => e.kind === 'player');
      const tf = m.targetFaction ?? 'blood_reavers';
      const hulls = PIRATE_HULLS[tf] ?? PIRATE_HULLS.blood_reavers;
      for (let i = 0; i < 3; i++) {
        makeNpc(state, hulls[0], tf,
          (player?.x ?? 0) + rng.range(-200, 200), (player?.y ?? 0) + rng.range(-200, 200),
          'pirate', { missionId: m.id, aggroRange: 9999 });
      }
      state.log?.('Ambush! Hostile ships have detected your cargo!', 'bad');
    }
  }
}

// ---- AI update -------------------------------------------------------------------
function maxWeaponRange(e) {
  let r = 0;
  for (const m of e.modules) if (m.def.type === 'weapon') r = Math.max(r, m.def.range);
  return r || 60;
}

function acquireTarget(state, npc) {
  const player = state.entities.find(e => e.kind === 'player' && !e.dead);
  // retaliate
  const last = findEntity(state, npc.lastAttackerId);
  if (last && dist(npc, last) < 700) return last;
  const disp = disposition(state, npc.faction);
  if (player && (disp === 'hostile' || npc.ai.missionId) && dist(npc, player) < npc.ai.aggroRange) return player;
  // navy vs pirates
  if (npc.ai.behavior === 'navy' || npc.ai.behavior === 'police' || npc.ai.behavior === 'sisters') {
    let best = null, bd = 400;
    for (const e of state.entities) {
      if (e.kind !== 'npc' || e.dead || e.ai?.behavior !== 'pirate') continue;
      const d = dist(npc, e);
      if (d < bd) { bd = d; best = e; }
    }
    if (best) return best;
  }
  if (npc.ai.behavior === 'pirate') {
    let best = null, bd = 260;
    for (const e of state.entities) {
      if (e.kind !== 'npc' || e.dead || e.ai?.behavior === 'pirate') continue;
      const d = dist(npc, e);
      if (d < bd) { bd = d; best = e; }
    }
    if (best) return best;
  }
  return null;
}

export function updateNpcs(state, dt) {
  const player = state.entities.find(e => e.kind === 'player');
  for (const npc of state.entities) {
    if (npc.kind !== 'npc' || npc.dead) continue;
    const ai = npc.ai;
    // fleeing
    if (ai.fleeing) {
      ai.fleeT += dt;
      npc.mode = 'flee';
      npc.moveTarget = findEntity(state, npc.lastAttackerId) ?? { x: 0, y: 0 };
      if (ai.fleeT > 4) { npc.dead = true; state.fx?.warpOut(npc.x, npc.y); }
      continue;
    }
    let target = findEntity(state, npc.targetId);
    if (!target) {
      target = acquireTarget(state, npc);
      npc.targetId = target?.id ?? null;
      if (target && target.kind === 'player') {
        state.log?.(`${npc.name} has engaged you!`, 'bad');
        state.fx?.alarm?.();
        state.sfx?.('alarm');
      }
    }
    if (target) {
      const d = dist(npc, target);
      const wRange = maxWeaponRange(npc);
      if (d > wRange * 0.85) {
        npc.mode = 'approach'; npc.moveTarget = target; npc.approachDist = wRange * 0.6;
      } else {
        npc.mode = 'orbit'; npc.moveTarget = target; npc.orbitDist = wRange * 0.55;
      }
      for (const modInst of npc.modules) {
        if (modInst.def.type === 'weapon' && d <= modInst.def.range) {
          fireWeapon(state, npc, modInst, target);
        } else if (modInst.def.type === 'shield_boost' && npc.hp.shield < npc.maxHp.shield * 0.4) {
          useUtilityModule(state, npc, modInst);
        }
      }
      if (ai.behavior === 'pirate' && npc.hp.hull < npc.maxHp.hull * 0.3 && !ai.missionId) {
        ai.fleeing = true; ai.fleeT = 0;
      }
    } else if (ai.waypoints.length) {
      npc.mode = 'patrol';
      const wp = ai.waypoints[ai.wpIndex % ai.waypoints.length];
      npc.moveTarget = wp;
      if (npc.patrolArrived) { npc.patrolArrived = false; ai.wpIndex++; }
    } else {
      npc.mode = 'idle';
    }
  }
  // cleanup dead npcs (after fx)
  state.entities = state.entities.filter(e => e.kind === 'player' || !e.dead || (e.deathT = (e.deathT ?? 0) + dt) < 0.1);
}

export function spawnDirectorates(state) {
  const player = state.entities.find(e => e.kind === 'player');
  if (!player) return;
  const existing = state.entities.filter(e => e.ai?.behavior === 'police' && !e.dead).length;
  if (existing >= 2) return;
  for (let i = 0; i < 2; i++) {
    const e = makeNpc(state, 'enforcer', 'directorate',
      player.x + (Math.random() - 0.5) * 400, player.y + (Math.random() - 0.5) * 400,
      'police', { aggroRange: 99999 });
    e.targetId = player.id;
  }
  state.log?.('Directorate response units have warped in!', 'bad');
}
