// Procedural sound effects via Web Audio API. No audio assets.
// Volume/mute are driven by the settings module. Browser-only; no-ops elsewhere.
import { getSettings } from './settings.js';

let ctx = null;
let master = null;

function ensureCtx() {
  if (typeof window === 'undefined') return null;
  const AC = window.AudioContext || window.webkitAudioContext;
  if (!AC) return null;
  if (!ctx) {
    ctx = new AC();
    master = ctx.createGain();
    master.connect(ctx.destination);
    applyVolume();
  }
  if (ctx.state === 'suspended') ctx.resume().catch(() => {});
  return ctx;
}

export function applyVolume() {
  if (!master) return;
  const s = getSettings();
  master.gain.value = s.muted ? 0 : s.volume * 0.4;
}

// Browsers require a user gesture before audio can start.
export function unlockAudio() { ensureCtx(); }

function tone(freq, dur, type = 'square', vol = 1, slideTo = null, delay = 0) {
  const t0 = ctx.currentTime + delay;
  const osc = ctx.createOscillator();
  const g = ctx.createGain();
  osc.type = type;
  osc.frequency.setValueAtTime(freq, t0);
  if (slideTo != null) osc.frequency.exponentialRampToValueAtTime(Math.max(1, slideTo), t0 + dur);
  g.gain.setValueAtTime(vol, t0);
  g.gain.exponentialRampToValueAtTime(0.001, t0 + dur);
  osc.connect(g); g.connect(master);
  osc.start(t0); osc.stop(t0 + dur + 0.02);
}

function noise(dur, vol = 1, cutoff = 800, delay = 0) {
  const t0 = ctx.currentTime + delay;
  const len = Math.max(1, Math.floor(ctx.sampleRate * dur));
  const buf = ctx.createBuffer(1, len, ctx.sampleRate);
  const data = buf.getChannelData(0);
  for (let i = 0; i < len; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / len);
  const src = ctx.createBufferSource();
  src.buffer = buf;
  const filt = ctx.createBiquadFilter();
  filt.type = 'lowpass'; filt.frequency.value = cutoff;
  const g = ctx.createGain();
  g.gain.setValueAtTime(vol, t0);
  g.gain.exponentialRampToValueAtTime(0.001, t0 + dur);
  src.connect(filt); filt.connect(g); g.connect(master);
  src.start(t0);
}

export function sfx(name) {
  const s = getSettings();
  if (s.muted || s.volume <= 0) return;
  if (!ensureCtx()) return;
  switch (name) {
    case 'laser':     tone(920, 0.14, 'sawtooth', 0.35, 240); break;
    case 'missile':   noise(0.3, 0.3, 1200); tone(180, 0.3, 'triangle', 0.25, 60); break;
    case 'explosion': noise(0.6, 0.8, 500); tone(120, 0.5, 'sine', 0.5, 30); break;
    case 'hit':       noise(0.12, 0.3, 2000); break;
    case 'dock':      tone(440, 0.12, 'sine', 0.4); tone(660, 0.16, 'sine', 0.4, null, 0.12); break;
    case 'jump':      tone(220, 0.5, 'sine', 0.4, 880); noise(0.4, 0.2, 900, 0.1); break;
    case 'alarm':     tone(760, 0.18, 'square', 0.3); tone(760, 0.18, 'square', 0.3, null, 0.24); break;
    case 'ui':        tone(1200, 0.05, 'sine', 0.25); break;
  }
}
