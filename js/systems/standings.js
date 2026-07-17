// Player standings & NPC disposition logic. Pure module.
import { FACTIONS, factionRelation } from '../data/factions.js';
import { clamp } from '../core/utils.js';

export function getDirectStanding(player, fid) {
  return player.standings[fid] ?? 0;
}

// Effective standing = direct + derived from friends/foes of the faction.
export function getEffectiveStanding(player, fid) {
  const direct = getDirectStanding(player, fid);
  let derived = 0;
  for (const [otherId, val] of Object.entries(player.standings)) {
    if (otherId === fid || !val) continue;
    derived += val * factionRelation(otherId, fid) * 0.04;
  }
  return clamp(direct + derived, -10, 10);
}

export function modifyStanding(state, fid, delta, reason) {
  if (!FACTIONS[fid] || !delta) return;
  const p = state.player;
  const before = getEffectiveStanding(p, fid);
  p.standings[fid] = clamp((p.standings[fid] ?? 0) + delta, -10, 10);
  const after = getEffectiveStanding(p, fid);
  if (state.log) {
    const sign = delta > 0 ? '+' : '';
    state.log(`Standing ${sign}${delta.toFixed(2)} with ${FACTIONS[fid].name}${reason ? ` (${reason})` : ''} → ${after.toFixed(2)}`,
      delta > 0 ? 'good' : 'bad');
  }
}

// How an NPC of `fid` feels about the player right now.
export function disposition(state, fid) {
  const p = state.player;
  const fac = FACTIONS[fid];
  if (!fac) return 'neutral';
  const eff = getEffectiveStanding(p, fid);
  if (fac.type === 'police') return p.criminalTimer > 0 ? 'hostile' : 'neutral';
  if (fac.type === 'pirate') {
    if (eff >= 5) return 'friendly';
    if (eff > 0) return 'neutral';
    return 'hostile';
  }
  // empires & sisters
  if (p.criminalTimer > 0 && fac.type === 'empire') return 'hostile';
  if (eff < -5) return 'hostile';
  if (eff >= 5) return 'friendly';
  return 'neutral';
}

// Is it a crime to attack this faction's ship in this system?
export function isCriminalAttack(state, targetFaction) {
  const fac = FACTIONS[targetFaction];
  if (!fac) return false;
  if (fac.type === 'pirate') return false; // always legal to shoot pirates
  const sys = state.universe.systems[state.currentSystemId];
  if (fac.type === 'police' || fac.type === 'sisters') return true;
  if (fac.type === 'empire') return sys.security >= 0.5;
  return false;
}

export function applyCrime(state, targetFaction) {
  const sys = state.universe.systems[state.currentSystemId];
  state.player.criminalTimer = Math.max(state.player.criminalTimer, 120);
  modifyStanding(state, targetFaction, -0.5, 'criminal aggression');
  modifyStanding(state, 'directorate', -0.2, 'criminal aggression');
  if (state.log) state.log(`CRIMINAL ACT in ${sys.name}! The Directorate has been alerted.`, 'bad');
}
