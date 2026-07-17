// Agent missions: security / distribution / mining + storyline every 5. Pure module.
import { FACTIONS, EMPIRES, PIRATES } from '../data/factions.js';
import { ITEMS } from '../data/ships.js';
import { uid } from '../core/utils.js';
import { routeBetween } from '../data/universe.js';
import { addCargo, removeCargo } from './economy.js';
import { modifyStanding } from './standings.js';

const PIRATE_OF_EMPIRE = { aurelian: 'blood_reavers', kaldari: 'nathari', meridian: 'crimson_hand', varkhald: 'ashfang' };
const EMPIRE_OF_PIRATE = { blood_reavers: 'aurelian', nathari: 'kaldari', crimson_hand: 'meridian', ashfang: 'varkhald' };

function secRewardMult(sec) { return 1 + Math.max(0, 0.5 - sec) * 2.2; }

function enemyOf(faction, rng) {
  const t = FACTIONS[faction]?.type;
  if (t === 'empire') return { f: PIRATE_OF_EMPIRE[faction], navy: false };
  if (t === 'pirate') return { f: EMPIRE_OF_PIRATE[faction], navy: true };
  return { f: PIRATES[Math.floor(rng() * PIRATES.length)], navy: false };
}

const SEC_TITLES = ['Pirate Menace', 'Clear the Spacelanes', 'Retribution', 'Deadspace Incursion', 'Cull the Swarm'];
const DIST_TITLES = ['Special Delivery', 'Urgent Shipment', 'Supply Run', 'Fragile Freight', 'Priority Courier'];
const MINE_TITLES = ['Ore Quota', 'Industrial Demand', 'Raw Materials', 'The Extraction Contract'];

export function generateOffer(state, agent, station, system) {
  const rng = Math.random;
  const level = agent.level;
  const faction = station.faction;
  const base = {
    id: uid('mis'), faction, agentId: agent.id, agentName: agent.name,
    stationId: station.id, level, state: 'offered', kills: 0,
  };
  if (agent.division === 'security') {
    const { f: targetFaction, navy } = enemyOf(faction, rng);
    const neighbors = [system.id, ...(state.universe.adj[system.id] ?? [])];
    const targetSystemId = neighbors[Math.floor(rng() * neighbors.length)];
    const killsRequired = 2 + level * 2 + Math.floor(rng() * 2);
    const tSys = state.universe.systems[targetSystemId];
    const credits = Math.round(11000 * level * killsRequired * secRewardMult(tSys.security) / 1000) * 1000;
    return {
      ...base, type: 'security',
      title: SEC_TITLES[Math.floor(rng() * SEC_TITLES.length)],
      desc: `Hostile ${FACTIONS[targetFaction].name} forces are gathering in ${tSys.name}. ` +
        `Destroy ${killsRequired} of their ships at the deadspace site.`,
      targetSystemId, targetFaction, targetIsNavy: navy, killsRequired,
      reward: { credits, lp: 40 * level * killsRequired, standing: 0.15 * level },
    };
  }
  if (agent.division === 'distribution') {
    // find a station 1-3 jumps away
    const dest = pickDestinationStation(state, system.id, 1 + Math.floor(rng() * 3));
    if (!dest) return null;
    const jumps = (routeBetween(state.universe, system.id, dest.system.id)?.length ?? 2) - 1;
    const cargoQty = 20 * level + Math.floor(rng() * 20);
    const credits = Math.round((14000 * level + 9000 * jumps) * secRewardMult(dest.system.security) / 1000) * 1000;
    return {
      ...base, type: 'distribution',
      title: DIST_TITLES[Math.floor(rng() * DIST_TITLES.length)],
      desc: `Deliver ${cargoQty} m3 of sealed cargo to ${dest.station.name} in ${dest.system.name} (${jumps} jump${jumps > 1 ? 's' : ''}).`,
      destStationId: dest.station.id, destSystemId: dest.system.id, cargoQty,
      reward: { credits, lp: 60 * level * jumps, standing: 0.12 * level },
    };
  }
  // mining
  const oreId = system.belts[0]?.ore ?? 'ferrite';
  const oreQty = 60 * level + Math.floor(rng() * 40);
  const credits = Math.round(oreQty * (ITEMS[oreId].basePrice * 2.4) * secRewardMult(system.security) / 1000) * 1000;
  return {
    ...base, type: 'mining',
    title: MINE_TITLES[Math.floor(rng() * MINE_TITLES.length)],
    desc: `Mine and deliver ${oreQty} units of ${ITEMS[oreId].name}. Turn in at this station.`,
    oreId, oreQty,
    reward: { credits, lp: 50 * level, standing: 0.1 * level },
  };
}

function pickDestinationStation(state, fromSystemId, maxJumps) {
  const candidates = [];
  const seen = { [fromSystemId]: 0 };
  const q = [fromSystemId];
  while (q.length) {
    const cur = q.shift();
    const d = seen[cur];
    if (d >= maxJumps) continue;
    for (const nb of state.universe.adj[cur] ?? []) {
      if (!(nb in seen)) { seen[nb] = d + 1; q.push(nb); }
    }
  }
  for (const [sysId, d] of Object.entries(seen)) {
    if (d === 0) continue;
    const sys = state.universe.systems[sysId];
    for (const st of sys.stations) candidates.push({ station: st, system: sys });
  }
  if (!candidates.length) return null;
  return candidates[Math.floor(Math.random() * candidates.length)];
}

// ---- Storyline (every 5 completed missions for a faction) -----------------------
export function offerStoryline(state, faction) {
  const rng = Math.random;
  const p = state.player;
  const kind = rng() < 0.5 ? 'story_kill' : 'story_haul';
  const { f: targetFaction, navy } = enemyOf(faction, rng);
  const base = {
    id: uid('mis'), type: 'storyline', subtype: kind, faction,
    agentId: null, agentName: `${FACTIONS[faction].name} Command`,
    stationId: null, level: 3, state: 'offered', kills: 0,
  };
  let m;
  if (kind === 'story_kill') {
    // target a low/null system near the faction's territory
    const systems = Object.values(state.universe.systems).filter(s => s.security < 0.5);
    const tSys = systems[Math.floor(rng() * systems.length)] ?? Object.values(state.universe.systems)[0];
    const killsRequired = 6;
    m = {
      ...base, title: 'Storyline: Breaking the Blockade',
      desc: `${FACTIONS[faction].name} has a critical operation in ${tSys.name}. An elite ${FACTIONS[targetFaction].name} squad blocks the way. Eliminate all ${killsRequired} hostiles including their commander.`,
      targetSystemId: tSys.id, targetFaction, targetIsNavy: navy, killsRequired,
      reward: { credits: 1500000, lp: 2500, standing: 1.5 },
    };
  } else {
    // deliver VIP cargo to the faction's home station
    const home = Object.values(state.universe.systems).find(s => s.faction === faction && s.stations.length)
      ?? Object.values(state.universe.systems).find(s => s.stations.length);
    const st = home.stations[0];
    m = {
      ...base, title: 'Storyline: The Ambassador',
      desc: `A ${FACTIONS[faction].name} dignitary must reach ${st.name} in ${home.name} in absolute secrecy. Expect interception. Deliver the VIP cargo.`,
      destStationId: st.id, destSystemId: home.id, cargoQty: 40,
      targetFaction, ambushSpawned: false,
      reward: { credits: 1200000, lp: 2000, standing: 1.5 },
    };
  }
  p.missions.push(m);
  state.log?.(`STORYLINE MISSION available: "${m.title}" from ${FACTIONS[faction].name}. Check your journal.`, 'good');
  return m;
}

// ---- Accept / complete -------------------------------------------------------------
export function acceptMission(state, mission) {
  mission.state = 'active';
  if (mission.type === 'distribution' || mission.subtype === 'story_haul') {
    if (!addCargo(state, 'sealed_cargo', mission.cargoQty)) {
      return { ok: false, msg: `Need ${mission.cargoQty} m3 free cargo space.` };
    }
  }
  if (!state.player.missions.includes(mission)) state.player.missions.push(mission);
  state.log?.(`Mission accepted: ${mission.title}`, 'info');
  return { ok: true };
}

export function declineMission(state, mission) {
  mission.state = 'done';
  state.log?.(`Mission declined: ${mission.title}`, 'info');
}

export function abandonMission(state, mission) {
  mission.state = 'done';
  if (mission.type === 'distribution' || mission.subtype === 'story_haul') {
    removeCargo(state, 'sealed_cargo', mission.cargoQty);
  }
  modifyStanding(state, mission.faction, -0.2, 'abandoned mission');
  state.log?.(`Mission abandoned: ${mission.title}`, 'bad');
}

export function canComplete(mission) {
  return mission.state === 'objectives_met';
}

export function completeMission(state, mission) {
  const p = state.player;
  mission.state = 'done';
  p.credits += mission.reward.credits;
  p.lp[mission.faction] = (p.lp[mission.faction] ?? 0) + mission.reward.lp;
  modifyStanding(state, mission.faction, mission.reward.standing, mission.title);
  p.stats.missionsDone++;
  state.log?.(`Mission complete: ${mission.title}. +${mission.reward.credits.toLocaleString()} ISK, +${mission.reward.lp} LP.`, 'good');
  // storyline trigger: every 5 normal missions for this faction
  if (mission.type !== 'storyline') {
    p.missionCounts[mission.faction] = (p.missionCounts[mission.faction] ?? 0) + 1;
    if (p.missionCounts[mission.faction] % 5 === 0) {
      offerStoryline(state, mission.faction);
    }
  }
  return true;
}

// ---- Event hooks ---------------------------------------------------------------------
export function onKill(state, npc) {
  const mid = npc.ai?.missionId;
  if (!mid) return;
  const m = state.player.missions.find(x => x.id === mid && x.state === 'active');
  if (!m) return;
  m.kills++;
  state.log?.(`Mission progress: ${m.title} — ${m.kills}/${m.killsRequired} hostiles destroyed.`, 'info');
  if (m.kills >= m.killsRequired) {
    m.state = 'objectives_met';
    state.log?.(`Objectives complete: ${m.title}. You may complete it from the journal.`, 'good');
  }
}

export function onDock(state, stationId) {
  const p = state.player;
  for (const m of p.missions) {
    if (m.state !== 'active') continue;
    if (m.type === 'distribution' && m.destStationId === stationId) {
      if (removeCargo(state, 'sealed_cargo', m.cargoQty)) completeMission(state, m);
    } else if (m.type === 'mining' && m.stationId === stationId) {
      if ((p.cargo[m.oreId] ?? 0) >= m.oreQty) {
        removeCargo(state, m.oreId, m.oreQty);
        completeMission(state, m);
      }
    } else if (m.subtype === 'story_haul' && m.destStationId === stationId) {
      if (removeCargo(state, 'sealed_cargo', m.cargoQty)) completeMission(state, m);
    }
  }
}

export function progressText(m) {
  if (m.type === 'security' || m.subtype === 'story_kill') return `Hostiles destroyed: ${m.kills}/${m.killsRequired}`;
  if (m.type === 'distribution' || m.subtype === 'story_haul') return 'Deliver the cargo to the destination station';
  if (m.type === 'mining') return `Deliver ore to the agent's station`;
  return '';
}

export function pruneMissions(player) {
  player.missions = player.missions.filter(m => m.state !== 'done');
}
