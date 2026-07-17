// Settings panel: quality, volume, mute, FPS display. Browser module.
import { getSettings, setSetting } from '../core/settings.js';
import { applyVolume, sfx } from '../core/audio.js';

const $ = (id) => document.getElementById(id);

export function initSettings(game) {
  $('btn-settings').onclick = () => toggleSettings();
  $('btn-close-settings').onclick = () => toggleSettings(false);

  const q = $('set-quality');
  q.value = getSettings().quality;
  q.onchange = () => { setSetting('quality', q.value); sfx('ui'); };

  const vol = $('set-volume');
  const volVal = $('set-volume-val');
  vol.value = Math.round(getSettings().volume * 100);
  volVal.textContent = vol.value + '%';
  vol.oninput = () => {
    volVal.textContent = vol.value + '%';
    setSetting('volume', vol.value / 100);
    applyVolume();
  };
  vol.onchange = () => sfx('ui');

  const mute = $('set-mute');
  mute.checked = getSettings().muted;
  mute.onchange = () => { setSetting('muted', mute.checked); applyVolume(); sfx('ui'); };

  const fps = $('set-fps');
  fps.checked = getSettings().showFps;
  fps.onchange = () => { setSetting('showFps', fps.checked); sfx('ui'); };
}

export function toggleSettings(force) {
  const el = $('settings-panel');
  const show = force ?? !el.classList.contains('visible');
  el.classList.toggle('visible', show);
  if (show) sfx('ui');
}
