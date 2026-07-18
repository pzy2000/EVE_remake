// Modal dialogs, journal, character sheet, save menu, help, death screen. Browser module.
import { FACTIONS } from '../data/factions.js';
import { formatCredits, clamp } from '../core/utils.js';
import { getEffectiveStanding } from '../systems/standings.js';
import * as missions from '../systems/missions.js';
import { slotInfo } from '../core/storage.js';

const $ = (id) => document.getElementById(id);

export function initDialogs(game) {
  $('modal-close').onclick = () => closeModal();
  $('btn-close-journal').onclick = () => $('journal-panel').classList.remove('visible');
  $('btn-close-character').onclick = () => $('character-panel').classList.remove('visible');
  $('btn-respawn').onclick = () => {
    $('death-screen').classList.remove('visible');
    game.actions.respawn();
  };
}

export function showModal(html) {
  $('modal-body').innerHTML = html;
  $('modal').classList.add('visible');
}
export function closeModal() { $('modal').classList.remove('visible'); }

// ---- Mission offer ----------------------------------------------------------------
export function showMissionOffer(game, mission) {
  const fac = FACTIONS[mission.faction];
  showModal(`
    <h3 style="color:${fac.color}">${mission.title}</h3>
    <p class="dim">${fac.name} — ${mission.agentName}${mission.level ? ` (Level ${mission.level})` : ''}</p>
    <p>${mission.desc}</p>
    <p><b>Rewards:</b> ${formatCredits(mission.reward.credits)} ISK · ${mission.reward.lp} LP · +${mission.reward.standing.toFixed(2)} standing</p>
    <div class="btn-row">
      <button id="offer-accept">Accept</button>
      <button id="offer-decline" class="secondary">Decline</button>
    </div>`);
  $('offer-accept').onclick = () => {
    const r = missions.acceptMission(game.state, mission);
    if (!r.ok) game.log(r.msg, 'bad');
    closeModal();
    renderJournal(game);
  };
  $('offer-decline').onclick = () => {
    missions.declineMission(game.state, mission);
    closeModal();
  };
}

// ---- Journal ------------------------------------------------------------------------
export function renderJournal(game) {
  const state = game.state;
  const list = $('journal-list');
  const ms = state.player.missions.filter(m => m.state !== 'done');
  if (!ms.length) {
    list.innerHTML = '<p class="dim">No missions. Talk to an agent at a station.</p>';
    return;
  }
  let html = '';
  for (const m of ms) {
    const fac = FACTIONS[m.faction];
    const sysId = m.targetSystemId ?? m.destSystemId ?? null;
    const sysName = sysId ? state.universe.systems[sysId]?.name : null;
    html += `<div class="card ${m.type === 'storyline' ? 'storyline' : ''}">
      <b style="color:${fac.color}">${m.title}</b>
      <span class="dim"> — ${fac.name}${m.type === 'storyline' ? ' · STORYLINE' : ''}</span>
      <div>${m.desc}</div>
      <div class="dim">${missions.progressText(m)}${sysName ? ` · Location: ${sysName}` : ''}</div>
      <div class="dim">Reward: ${formatCredits(m.reward.credits)} ISK · ${m.reward.lp} LP · +${m.reward.standing.toFixed(2)} standing</div>
      <div class="btn-row">`;
    if (m.state === 'offered') {
      html += `<button data-acc="${m.id}">Accept</button><button class="secondary" data-dec="${m.id}">Decline</button>`;
    } else {
      if (missions.canComplete(m)) html += `<button data-comp="${m.id}">Complete Mission</button>`;
      if (sysId) html += `<button class="secondary" data-dest="${sysId}">Set Destination</button>`;
      html += `<button class="secondary" data-aban="${m.id}">Abandon</button>`;
    }
    html += `</div></div>`;
  }
  list.innerHTML = html;
  list.querySelectorAll('[data-acc]').forEach(b => b.onclick = () => {
    const m = state.player.missions.find(x => x.id === b.dataset.acc);
    const r = missions.acceptMission(state, m);
    if (!r.ok) game.log(r.msg, 'bad');
    renderJournal(game);
  });
  list.querySelectorAll('[data-dec]').forEach(b => b.onclick = () => {
    const m = state.player.missions.find(x => x.id === b.dataset.dec);
    missions.declineMission(state, m);
    state.player.missions = state.player.missions.filter(x => x.id !== m.id);
    renderJournal(game);
  });
  list.querySelectorAll('[data-comp]').forEach(b => b.onclick = () => {
    const m = state.player.missions.find(x => x.id === b.dataset.comp);
    missions.completeMission(state, m);
    missions.pruneMissions(state.player);
    renderJournal(game);
  });
  list.querySelectorAll('[data-aban]').forEach(b => b.onclick = () => {
    const m = state.player.missions.find(x => x.id === b.dataset.aban);
    missions.abandonMission(state, m);
    missions.pruneMissions(state.player);
    renderJournal(game);
  });
  list.querySelectorAll('[data-dest]').forEach(b => b.onclick = () => {
    state.player.destination = b.dataset.dest;
    game.log(`Destination set: ${state.universe.systems[b.dataset.dest].name}`, 'info');
  });
}

// ---- Character sheet -------------------------------------------------------------------
export function renderCharacter(game) {
  const state = game.state;
  const p = state.player;
  let html = `<h3>${p.name}</h3><p class="dim">${FACTIONS[p.empire].name} citizen</p>
    <h4>Standings (effective)</h4>`;
  for (const fac of Object.values(FACTIONS)) {
    const eff = getEffectiveStanding(p, fac.id);
    const pct = ((eff + 10) / 20) * 100;
    const col = eff > 0.5 ? '#3aff88' : eff < -0.5 ? '#ff5544' : '#b8c4d4';
    html += `<div class="standing-row">
      <span style="color:${fac.color}">${fac.name}</span>
      <div class="standing-bar"><div style="width:${pct}%;background:${col}"></div></div>
      <span>${eff.toFixed(2)}</span>
      <span class="dim">${(p.lp[fac.id] ?? 0)} LP</span></div>`;
  }
  html += `<h4>Record</h4><div class="dim">
    Ships destroyed: ${p.stats.kills}<br>
    Missions completed: ${p.stats.missionsDone}<br>
    Ore mined: ${p.stats.oreMined} units<br>
    Stargate jumps: ${p.stats.jumps}</div>`;
  $('character-body').innerHTML = html;
}

// ---- Save menu ---------------------------------------------------------------------------
export function showSaveMenu(game) {
  let html = '<h3>Save / Load</h3>';
  for (const slot of ['slot1', 'slot2', 'slot3']) {
    const info = slotInfo(slot);
    html += `<div class="row"><span>${slot.toUpperCase()}: ${info ? `${info.name} — ${formatCredits(info.credits)} ISK` : '<span class="dim">empty</span>'}</span>
      <span><button data-save="${slot}">Save</button>
      ${info ? `<button data-load="${slot}" class="secondary">Load</button>` : ''}</span></div>`;
  }
  html += `<div class="btn-row">
    <button id="save-export" class="secondary">Export JSON</button>
    <button id="save-import" class="secondary">Import JSON</button>
    <input type="file" id="save-file" accept=".json" style="display:none">
  </div>`;
  showModal(html);
  document.querySelectorAll('[data-save]').forEach(b => b.onclick = () => {
    game.actions.saveGame(b.dataset.save); closeModal();
  });
  document.querySelectorAll('[data-load]').forEach(b => b.onclick = () => {
    closeModal(); game.actions.loadGame(b.dataset.load);
  });
  $('save-export').onclick = () => game.actions.exportSave();
  $('save-import').onclick = () => $('save-file').click();
  $('save-file').onchange = (ev) => {
    if (ev.target.files[0]) { closeModal(); game.actions.importSave(ev.target.files[0]); }
  };
}

// ---- Help ----------------------------------------------------------------------------------
export function showHelp() {
  showModal(`<h3>How to Play</h3>
    <p>You are a capsuleer in a connected universe of ${''}star systems. Run missions for agents, mine asteroids, fight pirates, and raise your standing with the four empires — or with the pirates themselves.</p>
    <h4>Controls</h4>
    <ul>
      <li><b>Click</b> an object in space or in the Overview (right) to select it</li>
      <li><b>Right-drag</b> rotate camera · <b>Right-click</b> object context menu</li>
      <li><b>Double-click</b> space to fly there · <b>Mouse wheel</b> zoom</li>
      <li><b>V</b> camera: track selected object (press again or <b>X</b> to return to your ship)</li>
      <li><b>W</b> warp to selected · <b>L</b> lock target · <b>D</b> dock / jump when in range</li>
      <li><b>1-9</b> toggle ship modules (weapons fire on locked target)</li>
      <li><b>M</b> starmap · <b>J</b> journal · <b>C</b> character · <b>Esc</b> close panels</li>
    </ul>
    <h4>Tips</h4>
    <ul>
      <li>Every <b>5 missions</b> for a faction unlocks a <b>storyline mission</b>.</li>
      <li>Ore sells for much more in low/null-security space — if you survive the trip.</li>
      <li>Attacking empire ships in high-sec makes you a criminal. The Directorate responds fast.</li>
      <li>Killing pirates hurts their standing but pleases their enemies.</li>
    </ul>
    <h4>Credits</h4>
    <p class="dim" style="font-size:11px">Ship &amp; station art by <b>MillionthVector</b> (Alan Guyant),
    <a href="https://millionthvector.blogspot.com/p/free-sprites.html" target="_blank" style="color:var(--accent)">millionthvector.blogspot.com</a>, CC-BY 4.0.
    Particle textures by <b>Kenney</b> (kenney.nl), CC0.</p>`);
}

export function showDeath(game) {
  $('death-screen').classList.add('visible');
}
