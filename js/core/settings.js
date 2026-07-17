// User settings: quality, volume, FPS display. Persisted to localStorage.
// Node-safe (no DOM/localStorage at import time) so logic tests can import it.

const KEY = 'starfall_settings';

export const DEFAULTS = {
  quality: 'high',   // 'low' | 'medium' | 'high'
  volume: 0.7,       // 0..1 master volume
  muted: false,
  showFps: true,
};

// Quality scaling factors used by renderer & effects.
export const QUALITY = {
  low:    { starMult: 0.35, nebulae: 0, galaxies: 0, dust: 0,  warpLines: 20, vignette: false, particleMult: 0.35, particleCap: 250 },
  medium: { starMult: 0.6,  nebulae: 3, galaxies: 2, dust: 30, warpLines: 32, vignette: true,  particleMult: 0.6,  particleCap: 500 },
  high:   { starMult: 1,    nebulae: 4, galaxies: 3, dust: 60, warpLines: 48, vignette: true,  particleMult: 1,    particleCap: 900 },
};

let current = { ...DEFAULTS };
const listeners = [];

function persist() {
  try { localStorage.setItem(KEY, JSON.stringify(current)); } catch { /* no storage */ }
}

export function loadSettings() {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) current = { ...DEFAULTS, ...JSON.parse(raw) };
  } catch { /* no storage */ }
  return current;
}

export function getSettings() { return current; }

export function qualityPreset() { return QUALITY[current.quality] ?? QUALITY.high; }

export function setSetting(key, value) {
  if (!(key in DEFAULTS)) return;
  current = { ...current, [key]: value };
  persist();
  for (const fn of listeners) fn(current);
}

export function onSettingsChange(fn) { listeners.push(fn); }
