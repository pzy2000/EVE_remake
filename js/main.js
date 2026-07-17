// STARFALL ODYSSEY — main game controller.
import { newGame, spawnPlayerEntity, playerEntity, syncPlayerHp } from './core/state.js';
import * as storage from './core/storage.js';
import { Effects } from './render/effects.js';
import { Renderer } from './render/renderer.js';
import { prewarm } from './render/sprites.js';
import { loadAssets } from './render/assets.js';
import * as panels from './ui/panels.js';
import * as stationUI from './ui/station.js';
import * as mapUI from './ui/map.js';
import * as dialogs from './ui/dialogs.js';
import * as settingsUI from './ui/settings.js';
import { loadSettings, getSettings, onSettingsChange } from './core/settings.js';
import { sfx, unlockAudio } from './core/audio.js';
import * as combat from './systems/combat.js';
import * as npcSys from './systems/npc.js';
import * as missions from './systems/missions.js';
import { isCriminalAttack, applyCrime } from './systems/standings.js';
import { FACTIONS, EMPIRES } from './data/factions.js';
import { SHIPS } from './data/ships.js';
import { dist, clamp } from './core/utils.js';
import { findAgent } from './data/universe.js';

const $ = (id) => document.getElementById(id);

class Game {
  constructor() {
    this.state = null;
    this.renderer = new Renderer($('game-canvas'));
    this.lastT = 0;
    this.fpsFrames = 0; this.fpsTime = 0;
    loadSettings();
    window.addEventListener('resize', () => this.renderer.resize());
    this.bindInput();
    this.actions = this.makeActions();
    panels.initPanels(this);
    stationUI.initStation(this);
    mapUI.initMap(this);
    dialogs.initDialogs(this);
    settingsUI.initSettings(this);
    const fpsEl = $('fps-meter');
    fpsEl.style.display = getSettings().showFps ? 'block' : 'none';
    onSettingsChange((s) => { fpsEl.style.display = s.showFps ? 'block' : 'none'; });
    requestAnimationFrame((t) => this.loop(t));
    this.boot();
  }

  async boot() {
    await loadAssets(); // bitmap art; missing files fall back to procedural sprites
    prewarm(); // generate all sprite textures up-front
    this.showTitle();
  }

  log(msg, cls) { if (this.state) panels.logMessage(this.state, msg, cls); }

  // ---- title / new game -------------------------------------------------------
  showTitle() {
    const cards = $('empire-cards');
    cards.innerHTML = '';
    let chosen = 'aurelian';
    for (const emp of EMPIRES) {
      const f = FACTIONS[emp];
      const card = document.createElement('div');
      card.className = 'empire-card' + (emp === chosen ? ' chosen' : '');
      card.innerHTML = `<h3 style="color:${f.color}">${f.name}</h3><p>${f.desc}</p>`;
      card.onclick = () => {
        chosen = emp;
        cards.querySelectorAll('.empire-card').forEach(c => c.classList.remove('chosen'));
        card.classList.add('chosen');
      };
      cards.appendChild(card);
    }
    $('btn-launch').onclick = () => {
      const name = $('pilot-name').value.trim() || 'Pilot';
      const seed = (Math.random() * 1e9) | 0;
      $('newgame-screen').classList.remove('visible');
      this.startNew(seed, name, chosen);
    };
    const btnContinue = $('btn-continue');
    if (storage.slotInfo('auto')) {
      btnContinue.style.display = 'inline-block';
      btnContinue.onclick = () => {
        $('newgame-screen').classList.remove('visible');
        this.actions.loadGame('auto');
      };
    } else btnContinue.style.display = 'none';
    $('newgame-screen').classList.add('visible');
  }

  startNew(seed, name, empire) {
    this.state = newGame(seed, name, empire);
    this.setupRuntime();
    this.log(`Welcome to the stars, ${name}. You are docked at your home station.`, 'good');
    this.log('Talk to an agent to run your first mission. Press H for help.', 'info');
    this.enterStation();
  }

  setupRuntime() {
    const s = this.state;
    s.fx = new Effects();
    s.sfx = sfx;
    s.log = (m, c) => panels.logMessage(s, m, c);
    s.missionHooks = { onKill: (npc) => missions.onKill(s, npc) };
    s.docked = !!s.player.location.dockedAt;
  }

  // ---- docking / jumping ---------------------------------------------------------
  dockedStation() {
    const sys = this.state.universe.systems[this.state.currentSystemId];
    return sys.stations.find(st => st.id === this.state.player.location.dockedAt);
  }

  enterStation() {
    const s = this.state;
    s.docked = true;
    syncPlayerHp(s);
    s.entities = s.entities.filter(e => e.kind !== 'player');
    stationUI.openStation(this);
    storage.saveToSlot(s, 'auto');
  }

  selectedObject() {
    const s = this.state;
    const id = s?.selectedId;
    if (!id) return null;
    const sys = s.universe.systems[s.currentSystemId];
    const e = s.entities.find(e => e.id === id && !e.dead);
    if (e) return { ...e, kind: 'ship' };
    const a = s.asteroids.find(a => a.id === id);
    if (a) return { ...a, kind: 'asteroid', name: 'Asteroid' };
    const b = s.beacons.find(b => b.id === id);
    if (b) return { ...b, kind: 'beacon' };
    const st = sys.stations.find(x => x.id === id);
    if (st) return { ...st, kind: 'station' };
    const g = sys.gates.find(x => x.id === id);
    if (g) return { ...g, kind: 'gate' };
    const belt = sys.belts.find(x => x.id === id);
    if (belt) return { ...belt, kind: 'belt' };
    for (const p of sys.planets) {
      if (p.id === id) return { ...p, kind: 'planet' };
      const m = p.moons.find(m => m.id === id);
      if (m) return { ...m, kind: 'moon' };
    }
    return null;
  }

  makeActions() {
    const g = this;
    return {
      select(id) { g.state.selectedId = id; },
      warpToSelected() {
        const s = g.state, pe = playerEntity(s), o = g.selectedObject();
        if (!pe || !o || s.docked) return;
        combat.startWarp(s, pe, o.x, o.y);
      },
      approachSelected() {
        const s = g.state, pe = playerEntity(s), o = g.selectedObject();
        if (!pe || !o || s.docked) return;
        pe.warp = null; pe.mode = 'approach'; pe.moveTarget = o; pe.approachDist = 5;
      },
      orbitSelected() {
        const s = g.state, pe = playerEntity(s), o = g.selectedObject();
        if (!pe || !o || s.docked) return;
        pe.warp = null; pe.mode = 'orbit'; pe.moveTarget = o; pe.orbitDist = 50;
      },
      lockSelected() {
        const s = g.state, pe = playerEntity(s), o = g.selectedObject();
        if (!pe || !o || s.docked) return;
        if (o.kind !== 'ship' && o.kind !== 'asteroid') { g.log('Cannot lock that object.', 'bad'); return; }
        if (dist(pe, o) > pe.lockRange) { g.log('Target out of lock range.', 'bad'); return; }
        pe.targetId = o.id;
        g.log(`Target locked: ${o.name}`, 'info');
      },
      dockOrJumpSelected() {
        const s = g.state, pe = playerEntity(s), o = g.selectedObject();
        if (!pe || !o || s.docked) return;
        if (o.kind === 'station' && dist(pe, o) <= 40) {
          s.player.location.dockedAt = o.id;
          s.player.location.x = o.x; s.player.location.y = o.y;
          sfx('dock');
          g.log(`Docked at ${o.name}.`, 'good');
          missions.onDock(s, o.id);
          missions.pruneMissions(s.player);
          g.enterStation();
        } else if (o.kind === 'gate' && dist(pe, o) <= 35) {
          g.jumpGate(o);
        }
      },
      toggleModule(i) {
        const s = g.state, pe = playerEntity(s);
        if (!pe || s.docked) return;
        const m = pe.modules[i];
        if (!m) return;
        if (m.def.type === 'weapon' || m.def.type === 'mining') {
          if (!m.active && !pe.targetId) { g.log('Lock a target first (select + L).', 'bad'); return; }
          m.active = !m.active;
        } else if (m.def.type === 'shield_boost' || m.def.type === 'armor_rep') {
          combat.useUtilityModule(s, pe, m);
        } else if (m.def.type === 'propulsion') {
          pe.afterburnerOn = !pe.afterburnerOn;
          combat.recomputeDerived(pe);
        }
      },
      undock() {
        const s = g.state;
        const st = g.dockedStation();
        s.docked = false;
        s.player.location.dockedAt = null;
        stationUI.closeStation();
        spawnPlayerEntity(s, st.x + 45, st.y + 10);
        npcSys.populateSystem(s);
        sfx('dock');
        g.log(`Undocked from ${st.name}.`, 'info');
        storage.saveToSlot(s, 'auto');
      },
      talkToAgent(agentId) {
        const s = g.state;
        const found = findAgent(s.universe, agentId);
        if (!found) return;
        const offer = missions.generateOffer(s, found.agent, found.station, found.system);
        if (!offer) { g.log('This agent has no mission for you right now.', 'bad'); return; }
        dialogs.showMissionOffer(g, offer);
      },
      respawn() {
        const s = g.state;
        combat.respawnPlayer(s);
        s.docked = true;
        g.log('You were cloned at your home station.', 'info');
        g.enterStation();
      },
      saveGame(slot) { storage.saveToSlot(g.state, slot); g.log(`Game saved to ${slot}.`, 'good'); },
      loadGame(slot) {
        const s = storage.loadFromSlot(slot);
        if (!s) { g.log('Load failed.', 'bad'); return; }
        stationUI.closeStation();
        g.state = s;
        g.setupRuntime();
        if (s.player.location.dockedAt) {
          g.enterStation();
        } else {
          spawnPlayerEntity(s);
          npcSys.populateSystem(s);
        }
        g.log('Game loaded.', 'good');
      },
      exportSave() { storage.exportSave(g.state); },
      importSave(file) {
        storage.importSave(file, (s) => {
          stationUI.closeStation();
          g.state = s;
          g.setupRuntime();
          if (s.player.location.dockedAt) g.enterStation();
          else { spawnPlayerEntity(s); npcSys.populateSystem(s); }
          g.log('Save imported.', 'good');
        });
      },
    };
  }

  jumpGate(gate) {
    const s = this.state;
    const oldId = s.currentSystemId;
    const newSys = s.universe.systems[gate.to];
    s.fx.jumpFlash();
    sfx('jump');
    s.currentSystemId = gate.to;
    s.player.location.systemId = gate.to;
    s.player.stats.jumps++;
    const back = newSys.gates.find(g2 => g2.to === oldId) ?? newSys.gates[0];
    spawnPlayerEntity(s, back.x + 35, back.y + 15);
    npcSys.populateSystem(s);
    this.log(`Jumped through stargate to ${newSys.name} (${newSys.security.toFixed(1)} — ${FACTIONS[newSys.faction].name}).`, 'info');
    storage.saveToSlot(s, 'auto');
  }

  // ---- input ---------------------------------------------------------------------
  bindInput() {
    // AudioContext requires a user gesture to start.
    window.addEventListener('pointerdown', unlockAudio, { once: true });
    window.addEventListener('keydown', unlockAudio, { once: true });
    window.addEventListener('keydown', (ev) => {
      if (!this.state) return;
      if (ev.target.tagName === 'INPUT') return;
      const k = ev.key.toLowerCase();
      if (k === 'escape') {
        dialogs.closeModal();
        $('journal-panel').classList.remove('visible');
        $('character-panel').classList.remove('visible');
        settingsUI.toggleSettings(false);
        mapUI.toggleMap(this, false);
        return;
      }
      if (this.state.docked) return;
      if (k === 'm') this.toggleMap();
      else if (k === 'j') this.toggleJournal();
      else if (k === 'c') this.toggleCharacter();
      else if (k === 'h') this.showHelp();
      else if (k === 'w') this.actions.warpToSelected();
      else if (k === 'l') this.actions.lockSelected();
      else if (k === 'd') this.actions.dockOrJumpSelected();
      else if (k >= '1' && k <= '9') this.actions.toggleModule(+k - 1);
    });
    window.addEventListener('wheel', (ev) => {
      if (!this.state) return;
      const cam = this.state.camera;
      cam.zoom = clamp(cam.zoom * (ev.deltaY > 0 ? 0.9 : 1.1), 0.35, 2.5);
    }, { passive: true });
    $('game-canvas').addEventListener('click', (ev) => {
      const s = this.state;
      if (!s || s.docked) return;
      const cam = s.camera;
      const wx = (ev.clientX - window.innerWidth / 2) / cam.zoom + cam.x;
      const wy = (ev.clientY - window.innerHeight / 2) / cam.zoom + cam.y;
      let best = null, bd = 30 / cam.zoom;
      const consider = (o) => {
        const d = Math.hypot(o.x - wx, o.y - wy);
        if (d < bd) { bd = d; best = o; }
      };
      s.entities.forEach(e => !e.dead && consider(e));
      s.asteroids.forEach(consider);
      s.beacons.forEach(consider);
      const sys = s.universe.systems[s.currentSystemId];
      sys.stations.forEach(consider);
      sys.gates.forEach(consider);
      if (best) s.selectedId = best.id;
    });
  }

  toggleMap() { mapUI.toggleMap(this); }
  toggleJournal() {
    const el = $('journal-panel');
    el.classList.toggle('visible');
    if (el.classList.contains('visible')) dialogs.renderJournal(this);
  }
  toggleCharacter() {
    const el = $('character-panel');
    el.classList.toggle('visible');
    if (el.classList.contains('visible')) dialogs.renderCharacter(this);
  }
  showSaveMenu() { dialogs.showSaveMenu(this); }
  showHelp() { dialogs.showHelp(); }

  // ---- main loop --------------------------------------------------------------------
  loop(t) {
    const rawDt = (t - this.lastT) / 1000 || 0.016;
    const dt = Math.min(0.05, rawDt);
    this.lastT = t;
    // real-time FPS meter (updated twice per second)
    this.fpsFrames++; this.fpsTime += rawDt;
    if (this.fpsTime >= 0.5) {
      const fps = Math.round(this.fpsFrames / this.fpsTime);
      const el = $('fps-meter');
      el.textContent = `${fps} FPS`;
      el.style.color = fps >= 50 ? 'var(--accent)' : fps >= 30 ? '#ffd76a' : 'var(--bad)';
      this.fpsFrames = 0; this.fpsTime = 0;
    }
    const s = this.state;
    if (s) {
      s.time += dt;
      if (!s.docked && !s.playerDead) this.updateWorld(dt);
      s.fx?.update(dt);
      this.renderer.render(s, dt);
      panels.updatePanels(this, dt);
      if (s.playerDead && !$('death-screen').classList.contains('visible')) {
        dialogs.showDeath(this);
      }
    }
    requestAnimationFrame((t2) => this.loop(t2));
  }

  updateWorld(dt) {
    const s = this.state;
    const p = s.player;
    if (p.criminalTimer > 0) p.criminalTimer = Math.max(0, p.criminalTimer - dt);
    const pe = playerEntity(s);
    if (pe && !pe.dead) {
      // validate lock
      if (pe.targetId) {
        const t = s.entities.find(e => e.id === pe.targetId && !e.dead)
          ?? s.asteroids.find(a => a.id === pe.targetId && a.amount > 0);
        if (!t) {
          pe.targetId = null;
          pe.modules.forEach(m => { m.active = false; });
          this.log('Target lost.', 'info');
        }
      }
      // criminal check before firing
      const tgt = combat.findEntity(s, pe.targetId);
      if (tgt && tgt.kind === 'npc' && pe.modules.some(m => m.active && m.def.type === 'weapon')) {
        if (isCriminalAttack(s, tgt.faction) && p.criminalTimer <= 0) {
          applyCrime(s, tgt.faction);
          npcSys.spawnDirectorates(s);
        }
      }
      if (tgt) combat.activateWeaponsOn(s, pe, tgt);
      const ast = s.asteroids.find(a => a.id === pe.targetId && a.amount > 0);
      if (ast) for (const m of pe.modules) {
        if (m.active && m.def.type === 'mining') combat.mineCycle(s, pe, m, ast);
      }
      combat.updateEntity(s, pe, dt);
    }
    npcSys.updateNpcs(s, dt);
    for (const e of s.entities) if (e.kind === 'npc') combat.updateEntity(s, e, dt);
    combat.updateProjectiles(s, dt);
  }
}

new Game();
