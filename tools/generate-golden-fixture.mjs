#!/usr/bin/env node

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { FACTIONS } from '../js/data/factions.js';
import { ITEMS, MODULES, SHIPS } from '../js/data/ships.js';
import { generateUniverse } from '../js/data/universe.js';

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const DEFAULT_FIXTURE_PATH = resolve(REPO_ROOT, 'tests/fixtures/universe-seed-12345.json');

// 107 is the asteroid-belt count. The generated graph has 83 undirected gate
// edges (166 directed adjacency entries); keeping both fields prevents the two
// topology metrics from being conflated in migration work.
export const EXPECTED_COUNTS = Object.freeze({
  systems: 48,
  stations: 68,
  agents: 142,
  planets: 216,
  moons: 221,
  belts: 107,
  undirectedGates: 83,
  directedGateEndpoints: 166,
});

function countUniverse(universe) {
  const systems = Object.values(universe.systems);
  const directedGateEndpoints = systems.reduce((total, system) => total + system.gates.length, 0);
  return {
    systems: systems.length,
    stations: systems.reduce((total, system) => total + system.stations.length, 0),
    agents: systems.reduce(
      (total, system) => total + system.stations.reduce((sum, station) => sum + station.agents.length, 0),
      0,
    ),
    planets: systems.reduce((total, system) => total + system.planets.length, 0),
    moons: systems.reduce(
      (total, system) => total + system.planets.reduce((sum, planet) => sum + planet.moons.length, 0),
      0,
    ),
    belts: systems.reduce((total, system) => total + system.belts.length, 0),
    undirectedGates: directedGateEndpoints / 2,
    directedGateEndpoints,
  };
}

function catalogSnapshot(entries) {
  return {
    count: Object.keys(entries).length,
    ids: Object.keys(entries),
  };
}

function undirectedGateEdges(universe) {
  const edges = new Set();
  for (const [from, neighbors] of Object.entries(universe.adj)) {
    for (const to of neighbors) edges.add([from, to].sort().join('|'));
  }
  return [...edges].sort().map((edge) => edge.split('|'));
}

function systemSnapshot(system) {
  const moons = system.planets.flatMap((planet) => planet.moons);
  const agents = system.stations.flatMap((station) => station.agents);
  return {
    id: system.id,
    name: system.name,
    mapPosition: { x: system.x, y: system.y },
    security: system.security,
    faction: system.faction,
    region: system.region,
    capital: system.capital ?? false,
    starType: system.star.type,
    counts: {
      planets: system.planets.length,
      moons: moons.length,
      belts: system.belts.length,
      stations: system.stations.length,
      agents: agents.length,
      gates: system.gates.length,
    },
    planetIds: system.planets.map((planet) => planet.id),
    moonIds: moons.map((moon) => moon.id),
    beltIds: system.belts.map((belt) => belt.id),
    stationIds: system.stations.map((station) => station.id),
    agentIds: agents.map((agent) => agent.id),
    gateTargets: system.gates.map((gate) => gate.to),
  };
}

export function buildGoldenFixture() {
  const universe = generateUniverse(12345);
  const counts = countUniverse(universe);

  for (const [name, expected] of Object.entries(EXPECTED_COUNTS)) {
    if (counts[name] !== expected) {
      throw new Error(`Seed 12345 ${name} drifted: expected ${expected}, got ${counts[name]}.`);
    }
  }

  return {
    schemaVersion: 1,
    universeGeneratorVersion: 1,
    seed: 12345,
    counts,
    catalogs: {
      factions: catalogSnapshot(FACTIONS),
      ships: catalogSnapshot(SHIPS),
      modules: catalogSnapshot(MODULES),
      items: catalogSnapshot(ITEMS),
    },
    startSystems: universe.startSystems,
    undirectedGateEdges: undirectedGateEdges(universe),
    systems: Object.values(universe.systems).map(systemSnapshot),
  };
}

export function serializeGoldenFixture() {
  return `${JSON.stringify(buildGoldenFixture(), null, 2)}\n`;
}

function main() {
  const mode = process.argv[2] ?? '--stdout';
  const output = serializeGoldenFixture();

  if (mode === '--write') {
    writeFileSync(DEFAULT_FIXTURE_PATH, output, 'utf8');
    console.log(`Wrote ${DEFAULT_FIXTURE_PATH}`);
    return;
  }

  if (mode === '--check') {
    const current = readFileSync(DEFAULT_FIXTURE_PATH, 'utf8');
    if (current !== output) {
      console.error('Golden fixture is stale. Run: node tools/generate-golden-fixture.mjs --write');
      process.exitCode = 1;
      return;
    }
    console.log(`Golden fixture matches seed 12345 (${EXPECTED_COUNTS.systems} systems, ${EXPECTED_COUNTS.undirectedGates} gate edges).`);
    return;
  }

  if (mode !== '--stdout') throw new Error(`Unknown mode: ${mode}`);
  process.stdout.write(output);
}

const invokedPath = process.argv[1] ? pathToFileURL(resolve(process.argv[1])).href : '';
if (invokedPath === import.meta.url) main();
