// Procedural universe generation: systems, gates, planets, moons, belts,
// stations, agents. Pure module (seeded, deterministic).
import { makeRng } from '../core/rng.js';
import { bfsPath } from '../core/utils.js';
import { FACTIONS, EMPIRES } from './factions.js';

const SYL_A = ['Al','Bel','Cor','Dur','El','Fen','Gal','Hel','Ith','Jor','Ka','Lyr','Mor','Nym','Ost','Per','Qua','Ryn','Sel','Tor','Ul','Vex','Wyn','Xan','Yor','Zel','Ash','Bra','Cyn','Dra'];
const SYL_B = ['ara','eth','ion','os','une','ax','ir','on','ea','ys','oth','ael','mir','is','ur'];
const FIRST = ['Aren','Bela','Corin','Dara','Elias','Freya','Goran','Hana','Ivan','Jora','Kell','Lena','Marek','Nadia','Orin','Petra','Quill','Rosa','Sten','Talia','Ulric','Vera','Wren','Xavier','Yara','Zane'];
const LAST = ['Voss','Kaine','Ardath','Belmore','Castellan','Draven','Erland','Falk','Greer','Haldane','Ivar','Jorund','Korr','Lindqvist','Moreau','Nyx','Okafor','Pryce','Quade','Reyes','Sorren','Thane','Umar','Valen','Ward','Yilmaz'];

const STAR_TYPES = [
  { type: 'O', color: '#9db4ff', r: 90 }, { type: 'B', color: '#aabfff', r: 80 },
  { type: 'A', color: '#cad8ff', r: 70 }, { type: 'F', color: '#f8f7ff', r: 62 },
  { type: 'G', color: '#fff4e0', r: 58 }, { type: 'K', color: '#ffd2a1', r: 52 },
  { type: 'M', color: '#ffab8a', r: 44 },
];
const PLANET_TYPES = [
  { type: 'temperate', color: '#4a90d9' }, { type: 'barren', color: '#a08a6a' },
  { type: 'gas giant', color: '#d9b06a' }, { type: 'ice', color: '#bfe8ff' },
  { type: 'lava', color: '#ff6a3a' }, { type: 'oceanic', color: '#3a6ad9' },
  { type: 'toxic', color: '#8ad93a' },
];

function makeName(rng, used) {
  for (let tries = 0; tries < 50; tries++) {
    let n = rng.pick(SYL_A) + rng.pick(SYL_B);
    if (rng.chance(0.35)) n += ' ' + rng.pick(['II','III','IV','V','VI','Prime','Major','Minor']);
    if (!used.has(n)) { used.add(n); return n; }
  }
  const n = rng.pick(SYL_A) + rng.pick(SYL_B) + '-' + rng.int(10, 99);
  used.add(n); return n;
}

function makeAgent(rng, stationId, division, level) {
  return {
    id: `agent_${stationId}_${division}`,
    name: rng.pick(FIRST) + ' ' + rng.pick(LAST),
    division, level, stationId,
  };
}

export function generateUniverse(seed) {
  const rng = makeRng(seed);
  const usedNames = new Set();
  const systems = {};
  let sysCounter = 0;

  function newSystem(name, mx, my, security, faction, region) {
    const id = `sys_${sysCounter++}`;
    systems[id] = {
      id, name, x: mx, y: my, security, faction, region,
      star: rng.pick(STAR_TYPES),
      planets: [], belts: [], stations: [], gates: [],
    };
    return systems[id];
  }

  // --- Cluster layout on the starmap (1000x1000) ---
  const empireCenters = {
    aurelian: [230, 230], kaldari: [770, 230],
    meridian: [230, 770], varkhald: [770, 770],
  };
  const clusters = []; // {systems:[ids], cx, cy, kind, faction}
  const startSystems = {};

  for (const emp of EMPIRES) {
    const [cx, cy] = empireCenters[emp];
    const ids = [];
    for (let i = 0; i < 7; i++) {
      let mx, my, ok = false;
      for (let t = 0; t < 40 && !ok; t++) {
        mx = cx + rng.range(-130, 130); my = cy + rng.range(-130, 130);
        ok = ids.every(id2 => Math.hypot(systems[id2].x - mx, systems[id2].y - my) > 55);
      }
      const isCapital = i === 0;
      const sec = isCapital ? 1.0 : rng.range(0.5, 0.9);
      const s = newSystem(makeName(rng, usedNames), mx, my, Math.round(sec * 10) / 10, emp, 'empire');
      if (isCapital) { s.capital = true; startSystems[emp] = s.id; }
      ids.push(s.id);
    }
    clusters.push({ systems: ids, cx, cy, kind: 'empire', faction: emp });
  }

  // Low-sec ring around center
  const lowIds = [];
  for (let i = 0; i < 10; i++) {
    const ang = (i / 10) * Math.PI * 2 + rng.range(-0.2, 0.2);
    const r = rng.range(130, 195);
    const mx = 500 + Math.cos(ang) * r, my = 500 + Math.sin(ang) * r;
    const owner = rng.pick(EMPIRES);
    const s = newSystem(makeName(rng, usedNames), mx, my, Math.round(rng.range(0.1, 0.4) * 10) / 10, owner, 'lowsec');
    lowIds.push(s.id);
  }
  clusters.push({ systems: lowIds, cx: 500, cy: 500, kind: 'lowsec', faction: null });

  // Null-sec pirate clusters
  const nullClusters = [
    { faction: 'blood_reavers', c: [90, 500] }, { faction: 'nathari', c: [500, 90] },
    { faction: 'crimson_hand', c: [500, 910] }, { faction: 'ashfang', c: [910, 500] },
  ];
  for (const nc of nullClusters) {
    const ids = [];
    for (let i = 0; i < 2; i++) {
      const mx = nc.c[0] + rng.range(-70, 70), my = nc.c[1] + rng.range(-70, 70);
      const s = newSystem(makeName(rng, usedNames), mx, my, 0.0, nc.faction, 'nullsec');
      ids.push(s.id);
    }
    clusters.push({ systems: ids, cx: nc.c[0], cy: nc.c[1], kind: 'nullsec', faction: nc.faction });
  }

  // Sisters enclave near center
  const sisIds = [];
  for (let i = 0; i < 2; i++) {
    const mx = 500 + rng.range(-70, 70), my = 500 + rng.range(-70, 70);
    const s = newSystem(makeName(rng, usedNames), mx, my, 0.5, 'sisters', 'sisters');
    sisIds.push(s.id);
  }
  clusters.push({ systems: sisIds, cx: 500, cy: 500, kind: 'sisters', faction: 'sisters' });

  // --- Gate connections ---
  const adj = {};
  Object.keys(systems).forEach(id => { adj[id] = new Set(); });
  const link = (a, b) => { if (a !== b) { adj[a].add(b); adj[b].add(a); } };
  const nearest = (id, pool, n) => pool.filter(p => p !== id)
    .sort((a, b) => Math.hypot(systems[a].x - systems[id].x, systems[a].y - systems[id].y)
      - Math.hypot(systems[b].x - systems[id].x, systems[b].y - systems[id].y)).slice(0, n);

  for (const cl of clusters) {
    for (const id of cl.systems) for (const nb of nearest(id, cl.systems, 2)) link(id, nb);
  }
  // lowsec ring chain
  const ring = lowIds.slice().sort((a, b) =>
    Math.atan2(systems[a].y - 500, systems[a].x - 500) - Math.atan2(systems[b].y - 500, systems[b].x - 500));
  for (let i = 0; i < ring.length; i++) link(ring[i], ring[(i + 1) % ring.length]);
  // empire <-> lowsec
  for (const cl of clusters.filter(c => c.kind === 'empire')) {
    const border = cl.systems.slice().sort((a, b) =>
      Math.hypot(systems[a].x - 500, systems[a].y - 500) - Math.hypot(systems[b].x - 500, systems[b].y - 500)).slice(0, 3);
    for (const b of border) for (const ls of nearest(b, lowIds, 1)) link(b, ls);
  }
  // null & sisters <-> lowsec
  for (const cl of clusters.filter(c => c.kind === 'nullsec' || c.kind === 'sisters')) {
    for (const id of cl.systems) for (const ls of nearest(id, lowIds, 2)) link(id, ls);
  }
  // ensure full connectivity via union-find
  const parent = {}; Object.keys(systems).forEach(id => { parent[id] = id; });
  const find = (x) => parent[x] === x ? x : (parent[x] = find(parent[x]));
  Object.entries(adj).forEach(([a, set]) => set.forEach(b => { parent[find(a)] = find(b); }));
  let roots = new Set(Object.keys(systems).map(find));
  while (roots.size > 1) {
    const arr = [...roots];
    let best = null, bestD = Infinity;
    for (let i = 0; i < arr.length; i++) for (let j = i + 1; j < arr.length; j++) {
      const A = Object.keys(systems).filter(id => find(id) === arr[i]);
      const B = Object.keys(systems).filter(id => find(id) === arr[j]);
      for (const a of A) for (const b of B) {
        const d = Math.hypot(systems[a].x - systems[b].x, systems[a].y - systems[b].y);
        if (d < bestD) { bestD = d; best = [a, b]; }
      }
    }
    link(best[0], best[1]);
    parent[find(best[0])] = find(best[1]);
    roots = new Set(Object.keys(systems).map(find));
  }

  // --- In-system content ---
  for (const sys of Object.values(systems)) {
    const srng = makeRng(seed ^ (sys.id.length * 7919 + sysCounter + sys.name.length * 131 + sys.id.charCodeAt(4) * 31337));
    // planets & moons
    const nPlanets = srng.int(2, 7);
    for (let i = 0; i < nPlanets; i++) {
      const ang = srng.range(0, Math.PI * 2);
      const rad = 700 + i * srng.range(320, 420) + srng.range(0, 150);
      const pt = srng.pick(PLANET_TYPES);
      const planet = {
        id: `${sys.id}_p${i}`, name: `${sys.name} ${['I','II','III','IV','V','VI','VII','VIII'][i]}`,
        type: 'planet', ptype: pt.type, color: pt.color,
        x: Math.cos(ang) * rad, y: Math.sin(ang) * rad,
        r: srng.range(26, pt.type === 'gas giant' ? 60 : 42), moons: [],
      };
      const nMoons = srng.int(0, 2);
      for (let m = 0; m < nMoons; m++) {
        const mang = srng.range(0, Math.PI * 2);
        planet.moons.push({
          id: `${planet.id}_m${m}`, name: `${planet.name} - Moon ${m + 1}`, type: 'moon',
          x: planet.x + Math.cos(mang) * srng.range(70, 110),
          y: planet.y + Math.sin(mang) * srng.range(70, 110), r: srng.range(8, 14),
        });
      }
      sys.planets.push(planet);
    }
    // asteroid belts
    const nBelts = srng.int(1, 4);
    for (let i = 0; i < nBelts; i++) {
      const ang = srng.range(0, Math.PI * 2);
      const rad = srng.range(900, 3200);
      let ore = 'ferrite';
      if (sys.security <= 0.0) ore = srng.chance(0.6) ? 'crystalline' : 'novacite';
      else if (sys.security < 0.5) ore = srng.chance(0.7) ? 'novacite' : 'ferrite';
      else ore = srng.chance(0.8) ? 'ferrite' : 'novacite';
      sys.belts.push({
        id: `${sys.id}_b${i}`, name: `${sys.name} Belt ${['Alpha','Beta','Gamma','Delta'][i]}`,
        type: 'belt', x: Math.cos(ang) * rad, y: Math.sin(ang) * rad,
        ore, asteroids: srng.int(10, 20),
      });
    }
    // stations
    const mkStation = (i, faction, label) => {
      const ang = srng.range(0, Math.PI * 2);
      const rad = srng.range(800, 2600);
      const st = {
        id: `${sys.id}_st${sys.stations.length}`, name: `${sys.name} ${label}`,
        type: 'station', faction, x: Math.cos(ang) * rad, y: Math.sin(ang) * rad,
        agents: [],
      };
      const divs = srng.shuffle(['security', 'distribution', 'mining']).slice(0, srng.int(1, 3));
      const lvl = sys.security >= 0.5 ? srng.int(1, 2) : sys.security > 0 ? srng.int(2, 3) : 3;
      divs.forEach(d => st.agents.push(makeAgent(srng, st.id, d, lvl)));
      // capital stations always have a level-1 agent so new pilots can start
      if (sys.capital && st.agents.length) st.agents[0].level = 1;
      sys.stations.push(st);
      return st;
    };
    if (sys.region === 'empire') {
      const n = sys.capital ? 3 : srng.int(1, 2);
      for (let i = 0; i < n; i++) mkStation(i, sys.faction, `${FACTIONS[sys.faction].short} Station ${i + 1}`);
      if (sys.capital) mkStation(99, 'directorate', 'Directorate Bureau');
    } else if (sys.region === 'lowsec') {
      if (srng.chance(0.7)) mkStation(0, sys.faction, 'Outpost');
    } else if (sys.region === 'nullsec') {
      mkStation(0, sys.faction, 'Pirate Haven');
    } else if (sys.region === 'sisters') {
      mkStation(0, 'sisters', 'Sanctuary');
      if (srng.chance(0.5)) mkStation(1, 'sisters', 'Refuge');
    }
    // gates (positioned toward connected systems on the map)
    [...adj[sys.id]].forEach((toId, i) => {
      const to = systems[toId];
      const ang = Math.atan2(to.y - sys.y, to.x - sys.x) + srng.range(-0.15, 0.15);
      const rad = srng.range(2400, 3400);
      sys.gates.push({
        id: `${sys.id}_g${i}`, name: `Stargate to ${to.name}`, type: 'gate',
        to: toId, x: Math.cos(ang) * rad, y: Math.sin(ang) * rad,
      });
    });
  }

  const adjList = {};
  Object.entries(adj).forEach(([k, set]) => { adjList[k] = [...set]; });
  return { seed, systems, adj: adjList, startSystems };
}

// ---- Query helpers ----------------------------------------------------------
export function routeBetween(universe, from, to) {
  return bfsPath(universe.adj, from, to);
}

export function allStations(sys) { return sys.stations; }

export function findStation(universe, stationId) {
  for (const sys of Object.values(universe.systems)) {
    const st = sys.stations.find(s => s.id === stationId);
    if (st) return { station: st, system: sys };
  }
  return null;
}

export function findAgent(universe, agentId) {
  for (const sys of Object.values(universe.systems)) {
    for (const st of sys.stations) {
      const ag = st.agents.find(a => a.id === agentId);
      if (ag) return { agent: ag, station: st, system: sys };
    }
  }
  return null;
}

export function secClass(sec) {
  return sec >= 0.5 ? 'high' : sec > 0 ? 'low' : 'null';
}
