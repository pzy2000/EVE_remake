import { makeRng } from '../../js/core/rng.js';

/**
 * Replaces Math.random for the lifetime of a headless test process.
 *
 * The browser runtime intentionally keeps using the platform RNG. Tests install
 * the game's Mulberry32 implementation so combat rolls, mission variants, and
 * IDs are reproducible without changing production modules.
 */
export function installDeterministicMathRandom(seed) {
  if (!Number.isInteger(seed)) throw new TypeError('Test RNG seed must be an integer.');

  const original = Math.random;
  Math.random = makeRng(seed);

  let restored = false;
  return () => {
    if (restored) return;
    Math.random = original;
    restored = true;
  };
}
