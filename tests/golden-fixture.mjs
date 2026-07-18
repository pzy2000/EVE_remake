import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

import {
  buildGoldenFixture,
  DEFAULT_FIXTURE_PATH,
  EXPECTED_COUNTS,
} from '../tools/generate-golden-fixture.mjs';

const checkedIn = JSON.parse(readFileSync(DEFAULT_FIXTURE_PATH, 'utf8'));
const generated = buildGoldenFixture();

assert.deepStrictEqual(checkedIn, generated, 'seed 12345 golden fixture must match the current generator');
assert.deepStrictEqual(checkedIn.counts, EXPECTED_COUNTS);
assert.equal(checkedIn.catalogs.factions.count, 10);
assert.equal(checkedIn.catalogs.ships.count, 18);
assert.equal(checkedIn.catalogs.modules.count, 14);
assert.equal(checkedIn.catalogs.items.count, 4);
assert.equal(checkedIn.undirectedGateEdges.length, EXPECTED_COUNTS.undirectedGates);

console.log(
  `Golden fixture verified: ${checkedIn.counts.systems} systems, ` +
  `${checkedIn.counts.belts} belts, ${checkedIn.counts.undirectedGates} undirected gate edges.`,
);
