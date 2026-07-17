// Generic helpers. Pure module, no DOM.
let uidCounter = 1;
export function uid(prefix = 'e') {
  return `${prefix}_${uidCounter++}_${Math.floor(Math.random() * 1e6).toString(36)}`;
}
export function resetUidForLoad(maxSeen) { uidCounter = Math.max(uidCounter, maxSeen + 1); }

export const dist = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
export const dist2 = (a, b) => (a.x - b.x) ** 2 + (a.y - b.y) ** 2;
export const angleTo = (a, b) => Math.atan2(b.y - a.y, b.x - a.x);
export const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
export const lerp = (a, b, t) => a + (b - a) * t;

export function angleLerp(a, b, t) {
  let d = b - a;
  while (d > Math.PI) d -= Math.PI * 2;
  while (d < -Math.PI) d += Math.PI * 2;
  return a + d * t;
}

export function formatCredits(n) {
  if (n >= 1e9) return (n / 1e9).toFixed(2) + 'B';
  if (n >= 1e6) return (n / 1e6).toFixed(2) + 'M';
  if (n >= 1e3) return (n / 1e3).toFixed(1) + 'K';
  return Math.floor(n).toString();
}

export function formatDistance(u) {
  if (u >= 1000) return (u / 1000).toFixed(1) + 'k km';
  return Math.round(u) + ' km';
}

// Breadth-first search path over adjacency map {id: [id,...]}
export function bfsPath(adj, from, to) {
  if (from === to) return [from];
  const prev = { [from]: null };
  const q = [from];
  while (q.length) {
    const cur = q.shift();
    for (const nb of (adj[cur] || [])) {
      if (!(nb in prev)) {
        prev[nb] = cur;
        if (nb === to) {
          const path = [to];
          let p = cur;
          while (p !== null) { path.unshift(p); p = prev[p]; }
          return path;
        }
        q.push(nb);
      }
    }
  }
  return null;
}

export function secStatusColor(sec) {
  if (sec >= 0.9) return '#2ee6a8';
  if (sec >= 0.7) return '#48d16b';
  if (sec >= 0.5) return '#b8d148';
  if (sec >= 0.3) return '#e0a53a';
  if (sec > 0.0) return '#e06c3a';
  return '#d13a3a';
}
