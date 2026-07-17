// Browser storage: localStorage slots + JSON export/import.
import { serialize, deserialize } from './state.js';

const PREFIX = 'starfall_';

export function saveToSlot(state, slot) {
  localStorage.setItem(PREFIX + slot, serialize(state));
  return true;
}

export function loadFromSlot(slot) {
  const json = localStorage.getItem(PREFIX + slot);
  if (!json) return null;
  try { return deserialize(json); } catch (e) { console.error('Load failed', e); return null; }
}

export function slotInfo(slot) {
  const json = localStorage.getItem(PREFIX + slot);
  if (!json) return null;
  try {
    const d = JSON.parse(json);
    return {
      name: d.player.name, credits: d.player.credits,
      system: d.currentSystemId, time: d.time,
      missions: d.player.stats?.missionsDone ?? 0,
    };
  } catch { return null; }
}

export function hasAnySave() {
  return ['auto', 'slot1', 'slot2', 'slot3'].some(s => localStorage.getItem(PREFIX + s));
}

export function exportSave(state) {
  const blob = new Blob([serialize(state)], { type: 'application/json' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = `starfall_save_${Date.now()}.json`;
  a.click();
  URL.revokeObjectURL(a.href);
}

export function importSave(file, cb) {
  const reader = new FileReader();
  reader.onload = () => {
    try { cb(deserialize(reader.result)); } catch (e) { alert('Invalid save file.'); }
  };
  reader.readAsText(file);
}
