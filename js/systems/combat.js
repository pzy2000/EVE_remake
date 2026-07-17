// Combat: entities, weapons, damage, movement, mining. Pure module (fx via callbacks).
import { SHIPS, MODULES, ITEMS } from '../data/ships.js';
import { dist, angleTo, clamp, angleLerp, uid } from '../core/utils.js';
import { modifyStanding, isCriminalAttack, applyCrime } from './standings.js';
import { addCargo, activeShipInst } from './economy.js';

export const BOUNTY = { frigate: 8000, destroyer: 25000, cruiser: 90000, battleship: 350000 };
export const KILL_STANDING = { frigate: -0.1, destroyer: -0.2, cruiser: -0.45, battleship: -0.9 };

// Build a combat entity from a ship instance (or raw shipId+fitting for NPCs).
export function createEntity(shipId, fitting, opts = {}) {
  const def = SHIPS[shipId];
  const e = {
    id: opts.id ?? uid('ship'), kind: opts.kind ?? 'npc',
    shipId, faction: opts.faction ?? def.faction,
    name: opts.name ?? def.name,
    x: opts.x ?? 0, y: opts.y ?? 0, angle: opts.angle ?? 0,
    speed: 0, vx: 0, vy: 0,
    fitting: fitting ?? { high: [], mid: [], low: [] },
    hp: { ...def.hp }, maxHp: { ...def.hp },
    dmgMult: 1, maxSpeed: def.speed, warpSpeed: def.warp,
    lockRange: def.lockRange,
    modules: [], mode: 'idle',
    moveTarget: null, orbitTargetId: null, orbitDist: 60,
    targetId: null, lockProgress: 0,
    warp: null, lastDamageAt: -999, lastAttackerId: null,
    ai: opts.ai ?? null, dead: false,
    afterburnerOn: false,
  };
  recomputeDerived(e);
  if (opts.hp) e.hp = { ...opts.hp };
  return e;
}

export function recomputeDerived(e) {
  const def = SHIPS[e.shipId];
  e.maxHp = { ...def.hp };
  e.dmgMult = 1; e.maxSpeed = def.speed;
  e.modules = [];
  const push = (moduleId, slotType) => {
    if (!moduleId) return;
    const m = MODULES[moduleId];
    const inst = { moduleId, slotType, def: m, cooldown: 0, active: false };
    e.modules.push(inst);
    if (m.type === 'passive') {
      if (m.shieldBonus) e.maxHp.shield += m.shieldBonus;
      if (m.armorBonus) e.maxHp.armor += m.armorBonus;
      if (m.dmgMult) e.dmgMult *= m.dmgMult;
    }
  };
  e.fitting.high.forEach(m => push(m, 'high'));
  e.fitting.mid.forEach(m => push(m, 'mid'));
  e.fitting.low.forEach(m => push(m, 'low'));
  if (e.afterburnerOn) e.maxSpeed *= (MODULES.afterburner.speedMult);
  e.hp.shield = Math.min(e.hp.shield, e.maxHp.shield);
  e.hp.armor = Math.min(e.hp.armor, e.maxHp.armor);
  e.hp.hull = Math.min(e.hp.hull, e.maxHp.hull);
}

export function findEntity(state, id) {
  if (!id) return null;
  return state.entities.find(e => e.id === id && !e.dead) ?? null;
}

// ---- Damage -------------------------------------------------------------------
export function applyDamage(state, target, amount, attacker) {
  if (target.dead) return false;
  let dmg = amount;
  target.lastDamageAt = state.time;
  if (attacker) target.lastAttackerId = attacker.id;
  if (target.hp.shield > 0) {
    const s = Math.min(target.hp.shield, dmg);
    target.hp.shield -= s; dmg -= s;
  }
  if (dmg > 0 && target.hp.armor > 0) {
    const s = Math.min(target.hp.armor, dmg);
    target.hp.armor -= s; dmg -= s;
  }
  if (dmg > 0) target.hp.hull -= dmg;
  if (target.hp.hull <= 0) { killEntity(state, target, attacker); return true; }
  return false;
}

export function killEntity(state, target, attacker) {
  if (target.dead) return;
  target.dead = true;
  state.fx?.explosion(target.x, target.y, SHIPS[target.shipId].cls, '#ffaa55');
  state.sfx?.('explosion');
  const p = state.player;
  if (target.kind === 'player') {
    state.playerDead = true;
    state.log?.(`Your ${SHIPS[target.shipId].name} was destroyed!`, 'bad');
    return;
  }
  if (attacker?.kind === 'player') {
    p.stats.kills++;
    const cls = SHIPS[target.shipId].cls;
    const fac = target.faction;
    const isPirate = target.ai?.behavior === 'pirate';
    if (isPirate) {
      const sec = state.universe.systems[state.currentSystemId].security;
      const bounty = Math.round((BOUNTY[cls] ?? 10000) * (1 + Math.max(0, 0.5 - sec)));
      p.credits += bounty;
      state.log?.(`Bounty: +${bounty.toLocaleString()} ISK for destroying ${target.name}.`, 'good');
    }
    modifyStanding(state, fac, KILL_STANDING[cls] ?? -0.1, `destroyed ${target.name}`);
    state.missionHooks?.onKill(target);
  }
}

// ---- Weapons ------------------------------------------------------------------
export function fireWeapon(state, shooter, modInst, target) {
  const m = modInst.def;
  const d = dist(shooter, target);
  if (d > m.range) return false;
  if (modInst.cooldown > 0) return false;
  modInst.cooldown = m.rate;
  const dmg = m.damage * shooter.dmgMult * (0.85 + Math.random() * 0.3);
  if (m.projectile) {
    state.projectiles.push({
      id: uid('msl'), x: shooter.x, y: shooter.y, targetId: target.id,
      speed: 320, dmg, faction: shooter.faction, fromId: shooter.id, life: 6,
    });
    if (shooter.kind === 'player') state.sfx?.('missile');
  } else {
    state.fx?.beam(shooter.x, shooter.y, target.x, target.y, m.beam ?? '#ffffff');
    applyDamage(state, target, dmg, shooter);
    if (shooter.kind === 'player') {
      state.fx?.text(target.x, target.y - 14, Math.round(dmg).toString(), '#ffdd88');
      state.sfx?.('laser');
    }
  }
  return true;
}

// Player/NPC activates a weapon on the currently locked target.
export function activateWeaponsOn(state, e, target) {
  let fired = false;
  for (const modInst of e.modules) {
    if (modInst.def.type === 'weapon' && modInst.active) {
      if (fireWeapon(state, e, modInst, target)) fired = true;
    }
  }
  return fired;
}

export function useUtilityModule(state, e, modInst) {
  const m = modInst.def;
  if (modInst.cooldown > 0) return false;
  if (m.type === 'shield_boost' && e.hp.shield < e.maxHp.shield) {
    e.hp.shield = Math.min(e.maxHp.shield, e.hp.shield + m.amount);
    modInst.cooldown = m.rate;
    state.fx?.text(e.x, e.y - 16, `+${m.amount} shield`, '#6af');
    return true;
  }
  if (m.type === 'armor_rep' && e.hp.armor < e.maxHp.armor) {
    e.hp.armor = Math.min(e.maxHp.armor, e.hp.armor + m.amount);
    modInst.cooldown = m.rate;
    state.fx?.text(e.x, e.y - 16, `+${m.amount} armor`, '#fa6');
    return true;
  }
  return false;
}

// ---- Mining ---------------------------------------------------------------------
export function mineCycle(state, e, modInst, asteroid) {
  const m = modInst.def;
  if (modInst.cooldown > 0) return false;
  if (dist(e, asteroid) > m.range) return false;
  modInst.cooldown = m.rate;
  const qty = Math.min(m.yield, asteroid.amount);
  if (qty <= 0) return false;
  if (e.kind === 'player') {
    if (!addCargo(state, asteroid.ore, qty)) {
      state.log?.('Cargo hold full!', 'bad');
      return false;
    }
    state.player.stats.oreMined += qty;
    state.fx?.text(asteroid.x, asteroid.y - 12, `+${qty} ${ITEMS[asteroid.ore].name}`, '#8aff8a');
    state.missionHooks?.onMine?.(asteroid.ore, qty);
  }
  asteroid.amount -= qty;
  state.fx?.beam(e.x, e.y, asteroid.x, asteroid.y, m.beam);
  return true;
}

// ---- Per-frame update ------------------------------------------------------------
export function updateEntity(state, e, dt) {
  if (e.dead) return;
  for (const modInst of e.modules) if (modInst.cooldown > 0) modInst.cooldown -= dt;
  // shield regen after 6s without damage
  if (state.time - e.lastDamageAt > 6 && e.hp.shield < e.maxHp.shield) {
    e.hp.shield = Math.min(e.maxHp.shield, e.hp.shield + e.maxHp.shield * 0.02 * dt);
  }
  updateMovement(state, e, dt);
}

export function updateMovement(state, e, dt) {
  const def = SHIPS[e.shipId];
  // Warp takes precedence
  if (e.warp) {
    const w = e.warp;
    const d = Math.hypot(w.tx - e.x, w.ty - e.y);
    const targetAngle = Math.atan2(w.ty - e.y, w.tx - e.x);
    if (w.phase === 'align') {
      e.angle = angleLerp(e.angle, targetAngle, dt * 4);
      e.speed = Math.min(e.speed + def.speed * 2 * dt, def.speed * 0.75);
      w.t += dt;
      if (w.t > 1.2) w.phase = 'cruise';
    } else if (w.phase === 'cruise') {
      e.angle = targetAngle;
      e.speed = e.warpSpeed;
      if (d < 350) w.phase = 'decel';
    } else {
      e.speed = Math.max(def.speed, e.speed - e.warpSpeed * 1.5 * dt);
      if (d < 30 || e.speed <= def.speed + 1) {
        e.warp = null; e.speed = 0; e.mode = 'idle';
        if (e.kind === 'player') state.log?.('Warp drive deactivated.', 'info');
        return;
      }
    }
    e.x += Math.cos(e.angle) * e.speed * dt;
    e.y += Math.sin(e.angle) * e.speed * dt;
    return;
  }
  let desired = 0;
  if (e.mode === 'approach' && e.moveTarget) {
    const d = dist(e, e.moveTarget);
    e.angle = angleLerp(e.angle, angleTo(e, e.moveTarget), dt * 3);
    desired = d > (e.approachDist ?? 5) ? e.maxSpeed : 0;
    if (d <= (e.approachDist ?? 5)) { e.mode = 'idle'; desired = 0; }
  } else if (e.mode === 'orbit' && e.moveTarget) {
    const d = dist(e, e.moveTarget);
    const tang = angleTo(e, e.moveTarget) + Math.PI / 2;
    const inward = d > e.orbitDist * 1.15 ? angleTo(e, e.moveTarget) : tang;
    e.angle = angleLerp(e.angle, inward, dt * 3);
    desired = e.maxSpeed * 0.85;
  } else if (e.mode === 'flee' && e.moveTarget) {
    e.angle = angleLerp(e.angle, angleTo(e.moveTarget, e), dt * 3); // directly away
    desired = e.maxSpeed;
  } else if (e.mode === 'patrol' && e.moveTarget) {
    const d = dist(e, e.moveTarget);
    e.angle = angleLerp(e.angle, angleTo(e, e.moveTarget), dt * 2);
    desired = e.maxSpeed * 0.4;
    if (d < 40) e.patrolArrived = true;
  }
  e.speed = desired;
  e.x += Math.cos(e.angle) * e.speed * dt;
  e.y += Math.sin(e.angle) * e.speed * dt;
}

export function startWarp(state, e, tx, ty) {
  e.warp = { tx, ty, phase: 'align', t: 0 };
  e.mode = 'warp';
  if (e.kind === 'player') state.log?.('Warp drive active.', 'info');
}

// ---- Projectiles ------------------------------------------------------------------
export function updateProjectiles(state, dt) {
  for (const pr of state.projectiles) {
    pr.life -= dt;
    const t = findEntity(state, pr.targetId);
    if (!t || pr.life <= 0) { pr.dead = true; continue; }
    const d = dist(pr, t);
    if (d < 12) {
      const shooter = findEntity(state, pr.fromId);
      applyDamage(state, t, pr.dmg, shooter ?? null);
      state.fx?.smallExplosion(t.x, t.y);
      pr.dead = true;
      continue;
    }
    const ang = angleTo(pr, t);
    pr.x += Math.cos(ang) * pr.speed * dt;
    pr.y += Math.sin(ang) * pr.speed * dt;
    state.fx?.trail(pr.x, pr.y, '#ffcc66');
  }
  state.projectiles = state.projectiles.filter(p => !p.dead);
}

// ---- Player death / respawn ---------------------------------------------------------
export function respawnPlayer(state) {
  const p = state.player;
  const inst = activeShipInst(p);
  if (inst) p.ships = p.ships.filter(s => s.instId !== inst.instId);
  if (p.ships.length === 0) {
    const rookie = { aurelian: 'acolyte', kaldari: 'shrike', meridian: 'wasp', varkhald: 'fang' }[p.empire] ?? 'acolyte';
    const { makeShipInstance } = state.economyApi;
    const ni = makeShipInstance(rookie, `ship_${Date.now()}`);
    ni.fitting.high[0] = { aurelian: 'pulse_laser', kaldari: 'missile_launcher', meridian: 'blaster', varkhald: 'autocannon' }[SHIPS[rookie].faction] ?? 'pulse_laser';
    p.ships.push(ni);
    state.log?.('The Directorate issued you a rookie frigate.', 'info');
  }
  p.activeShip = p.ships[0].instId;
  p.criminalTimer = 0;
  p.location.systemId = p.homeSystemId;
  p.location.dockedAt = p.homeStationId;
  state.currentSystemId = p.homeSystemId;
  state.playerDead = false;
}
