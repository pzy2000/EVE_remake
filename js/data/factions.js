// Faction definitions and inter-faction standing matrix. Pure module.

export const FACTIONS = {
  aurelian: {
    id: 'aurelian', name: 'Aurelian Empire', short: 'AUR', type: 'empire',
    color: '#d4af37', desc: 'A theocratic golden empire, master of laser weaponry.',
    homePirate: 'blood_reavers',
  },
  kaldari: {
    id: 'kaldari', name: 'Kaldari State', short: 'KAL', type: 'empire',
    color: '#4a9edd', desc: 'A corporate state built on missiles and railguns.',
    homePirate: 'nathari',
  },
  meridian: {
    id: 'meridian', name: 'Meridian Federation', short: 'MER', type: 'empire',
    color: '#3ec6b8', desc: 'A free federation favoring blasters and drones.',
    homePirate: 'crimson_hand',
  },
  varkhald: {
    id: 'varkhald', name: 'Varkhald Republic', short: 'VAR', type: 'empire',
    color: '#c96a3b', desc: 'A rugged republic of projectile-weapon clans.',
    homePirate: 'ashfang',
  },
  blood_reavers: {
    id: 'blood_reavers', name: 'Blood Reavers', short: 'BLD', type: 'pirate',
    color: '#a01830', desc: 'Fanatical raiders bleeding the Aurelian frontier.',
  },
  nathari: {
    id: 'nathari', name: 'Nathari Hive', short: 'NAT', type: 'pirate',
    color: '#7a3ec9', desc: 'A cybernetic hive mind infesting Kaldari space.',
  },
  crimson_hand: {
    id: 'crimson_hand', name: 'Crimson Hand', short: 'CRI', type: 'pirate',
    color: '#d13050', desc: 'Smugglers and cartel enforcers of the Federation.',
  },
  ashfang: {
    id: 'ashfang', name: 'Ashfang Cartel', short: 'ASH', type: 'pirate',
    color: '#8a8f3a', desc: 'Nomad cartel preying on the Varkhald Republic.',
  },
  sisters: {
    id: 'sisters', name: 'Sisters of the Veil', short: 'SIS', type: 'sisters',
    color: '#e8e4d8', desc: 'A humanitarian order devoted to exploration and mercy.',
  },
  directorate: {
    id: 'directorate', name: 'The Directorate', short: 'DIR', type: 'police',
    color: '#e8b400', desc: 'The unified stellar authority policing all of known space.',
  },
};

export const EMPIRES = ['aurelian', 'kaldari', 'meridian', 'varkhald'];
export const PIRATES = ['blood_reavers', 'nathari', 'crimson_hand', 'ashfang'];

// Inter-faction standings, -10..+10. Kept symmetric.
export const STANDING_MATRIX = {
  aurelian:     { aurelian: 10, kaldari: 5, meridian: -5, varkhald: -2, blood_reavers: -8, nathari: -5, crimson_hand: -3, ashfang: -5, sisters: 3, directorate: 8 },
  kaldari:      { aurelian: 5, kaldari: 10, meridian: -2, varkhald: -5, blood_reavers: -5, nathari: -8, crimson_hand: -3, ashfang: -5, sisters: 2, directorate: 8 },
  meridian:     { aurelian: -5, kaldari: -2, meridian: 10, varkhald: 5, blood_reavers: -3, nathari: -5, crimson_hand: -8, ashfang: -5, sisters: 5, directorate: 8 },
  varkhald:     { aurelian: -2, kaldari: -5, meridian: 5, varkhald: 10, blood_reavers: -5, nathari: -3, crimson_hand: -5, ashfang: -8, sisters: 3, directorate: 8 },
  blood_reavers:{ aurelian: -8, kaldari: -5, meridian: -3, varkhald: -5, blood_reavers: 10, nathari: 5, crimson_hand: 2, ashfang: 2, sisters: -5, directorate: -10 },
  nathari:      { aurelian: -5, kaldari: -8, meridian: -5, varkhald: -3, blood_reavers: 5, nathari: 10, crimson_hand: 2, ashfang: 2, sisters: -5, directorate: -10 },
  crimson_hand: { aurelian: -3, kaldari: -3, meridian: -8, varkhald: -5, blood_reavers: 2, nathari: 2, crimson_hand: 10, ashfang: 5, sisters: -5, directorate: -10 },
  ashfang:      { aurelian: -5, kaldari: -5, meridian: -5, varkhald: -8, blood_reavers: 2, nathari: 2, crimson_hand: 5, ashfang: 10, sisters: -5, directorate: -10 },
  sisters:      { aurelian: 3, kaldari: 2, meridian: 5, varkhald: 3, blood_reavers: -5, nathari: -5, crimson_hand: -5, ashfang: -5, sisters: 10, directorate: 5 },
  directorate:  { aurelian: 8, kaldari: 8, meridian: 8, varkhald: 8, blood_reavers: -10, nathari: -10, crimson_hand: -10, ashfang: -10, sisters: 5, directorate: 10 },
};

export function factionRelation(a, b) {
  const row = STANDING_MATRIX[a];
  if (!row) return 0;
  return row[b] ?? 0;
}

export function isPirate(fid) { return FACTIONS[fid]?.type === 'pirate'; }
export function isEmpire(fid) { return FACTIONS[fid]?.type === 'empire'; }
