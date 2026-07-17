// Bitmap asset loader: downloads nothing at runtime, just loads local PNGs
// from assets/ (fetched once by tools/fetch_assets.sh). Missing files resolve
// to null so sprites.js can fall back to procedural generation. Browser module.

const images = new Map(); // name -> HTMLImageElement | null

const MANIFEST = {
  ships: [
    'orangeship', 'orangeship2', 'orangeship3', 'smallorange',
    'blueship1', 'blueship2', 'blueship3', 'blueship4',
    'greenship1', 'greenship2', 'greenship3', 'greenship4',
    'alien1', 'alien2', 'alien3', 'alien4',
    'f5s1', 'f5s2', 'f5s3', 'f5s4',
    'rd1', 'rd2', 'rd3', 'redship4',
    'spshipspr1', 'medfighter', 'medfrighter', 'bgbattleship', 'spshipsprite',
    'aliensprite', 'aliensprite2', 'alienspaceship', 'att2',
  ],
  stations: ['spacestation', 'mainbase'],
  fx: [
    'flame_01', 'flame_05', 'fire_01', 'flare_01',
    'light_01', 'light_02', 'magic_01', 'magic_04',
    'smoke_01', 'smoke_05', 'smoke_08',
    'spark_01', 'spark_04', 'star_02', 'star_06',
    'trace_01', 'trace_04', 'twirl_01', 'twirl_02',
  ],
};

// Resolve against the module URL so pages in subdirectories (tools/) also work.
const BASE = new URL('../../assets/', import.meta.url);

export function loadAssets() {
  const jobs = [];
  for (const [dir, names] of Object.entries(MANIFEST)) {
    for (const n of names) {
      jobs.push(new Promise((resolve) => {
        const img = new Image();
        img.onload = () => { images.set(n, img); resolve(); };
        img.onerror = () => { images.set(n, null); resolve(); };
        img.src = new URL(`${dir}/${n}.png`, BASE).href;
      }));
    }
  }
  return Promise.all(jobs);
}

export function getImage(name) { return images.get(name) ?? null; }

export function loadedCount() {
  let n = 0;
  for (const v of images.values()) if (v) n++;
  return n;
}
