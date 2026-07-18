#!/usr/bin/env node

import { spawnSync } from 'node:child_process';

const rounds = Number(process.argv[2] ?? 100);
if (!Number.isSafeInteger(rounds) || rounds < 1) {
  console.error('Usage: node tools/repeat-tests.mjs [positive-round-count]');
  process.exit(2);
}

const suites = ['tests/run.mjs', 'tests/integration.mjs'];
const startedAt = performance.now();

for (let round = 1; round <= rounds; round++) {
  for (const suite of suites) {
    const result = spawnSync(process.execPath, [suite], {
      cwd: process.cwd(),
      encoding: 'utf8',
      env: process.env,
    });

    if (result.status !== 0) {
      console.error(`Round ${round}/${rounds} failed: ${suite}`);
      if (result.stdout) process.stderr.write(result.stdout);
      if (result.stderr) process.stderr.write(result.stderr);
      process.exit(result.status ?? 1);
    }
  }

  if (round % 10 === 0 || round === rounds) console.log(`Completed ${round}/${rounds} rounds.`);
}

const elapsedSeconds = ((performance.now() - startedAt) / 1000).toFixed(2);
console.log(`PASS: ${rounds} rounds, ${rounds * suites.length} deterministic suite executions (${elapsedSeconds}s).`);
