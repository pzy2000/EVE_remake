// Ship hulls, modules, and items. Pure module.

// ---- Ships ----------------------------------------------------------------
// speed: sublight u/s, warp: warp u/s, hp layers, slots, cargo (m3), lockRange
export const SHIPS = {
  // Aurelian (laser)
  acolyte:    { id: 'acolyte', name: 'Acolyte', faction: 'aurelian', cls: 'frigate', speed: 150, warp: 700, hp: { shield: 320, armor: 280, hull: 220 }, slots: { high: 2, mid: 2, low: 2 }, cargo: 160, lockRange: 320, price: 40000, desc: 'Fast Aurelian laser frigate.' },
  templar:    { id: 'templar', name: 'Templar', faction: 'aurelian', cls: 'destroyer', speed: 125, warp: 650, hp: { shield: 550, armor: 600, hull: 450 }, slots: { high: 3, mid: 2, low: 3 }, cargo: 320, lockRange: 380, price: 220000, desc: 'Aurelian destroyer with heavy pulse lasers.' },
  dawnbringer:{ id: 'dawnbringer', name: 'Dawnbringer', faction: 'aurelian', cls: 'cruiser', speed: 100, warp: 600, hp: { shield: 1100, armor: 1500, hull: 1000 }, slots: { high: 4, mid: 3, low: 4 }, cargo: 600, lockRange: 450, price: 1200000, desc: 'Aurelian cruiser, a floating battery of light.' },
  seraph:     { id: 'seraph', name: 'Seraph', faction: 'aurelian', cls: 'battleship', speed: 75, warp: 500, hp: { shield: 2600, armor: 4200, hull: 2800 }, slots: { high: 6, mid: 4, low: 6 }, cargo: 900, lockRange: 550, price: 9000000, desc: 'Golden Aurelian battleship of judgment.' },
  // Kaldari (missile/railgun)
  shrike:     { id: 'shrike', name: 'Shrike', faction: 'kaldari', cls: 'frigate', speed: 145, warp: 700, hp: { shield: 420, armor: 220, hull: 200 }, slots: { high: 2, mid: 3, low: 1 }, cargo: 160, lockRange: 340, price: 40000, desc: 'Kaldari missile frigate, long reach.' },
  heron:      { id: 'heron', name: 'Heron', faction: 'kaldari', cls: 'destroyer', speed: 120, warp: 650, hp: { shield: 800, armor: 380, hull: 400 }, slots: { high: 3, mid: 3, low: 2 }, cargo: 320, lockRange: 400, price: 220000, desc: 'Kaldari railgun destroyer.' },
  rook:       { id: 'rook', name: 'Rook', faction: 'kaldari', cls: 'cruiser', speed: 95, warp: 600, hp: { shield: 1900, armor: 800, hull: 900 }, slots: { high: 4, mid: 4, low: 3 }, cargo: 600, lockRange: 480, price: 1200000, desc: 'Kaldari missile cruiser with deep shields.' },
  onyx:       { id: 'onyx', name: 'Onyx', faction: 'kaldari', cls: 'battleship', speed: 70, warp: 500, hp: { shield: 5200, armor: 2200, hull: 2400 }, slots: { high: 6, mid: 5, low: 4 }, cargo: 900, lockRange: 600, price: 9000000, desc: 'Kaldari battleship, a fortress of shields.' },
  // Meridian (blaster)
  wasp:       { id: 'wasp', name: 'Wasp', faction: 'meridian', cls: 'frigate', speed: 160, warp: 700, hp: { shield: 300, armor: 320, hull: 240 }, slots: { high: 2, mid: 2, low: 2 }, cargo: 170, lockRange: 300, price: 40000, desc: 'Meridian blaster frigate, fast and mean.' },
  anvil:      { id: 'anvil', name: 'Anvil', faction: 'meridian', cls: 'destroyer', speed: 130, warp: 650, hp: { shield: 520, armor: 650, hull: 480 }, slots: { high: 3, mid: 2, low: 3 }, cargo: 340, lockRange: 360, price: 220000, desc: 'Meridian destroyer built for brawls.' },
  mantis:     { id: 'mantis', name: 'Mantis', faction: 'meridian', cls: 'cruiser', speed: 105, warp: 600, hp: { shield: 1000, armor: 1700, hull: 1100 }, slots: { high: 4, mid: 3, low: 4 }, cargo: 640, lockRange: 430, price: 1200000, desc: 'Meridian cruiser with crushing close-range damage.' },
  colossus:   { id: 'colossus', name: 'Colossus', faction: 'meridian', cls: 'battleship', speed: 78, warp: 500, hp: { shield: 2400, armor: 4600, hull: 3000 }, slots: { high: 6, mid: 4, low: 6 }, cargo: 950, lockRange: 520, price: 9000000, desc: 'Meridian battleship, an armored giant.' },
  // Varkhald (projectile)
  fang:       { id: 'fang', name: 'Fang', faction: 'varkhald', cls: 'frigate', speed: 170, warp: 720, hp: { shield: 300, armor: 280, hull: 260 }, slots: { high: 2, mid: 2, low: 2 }, cargo: 150, lockRange: 310, price: 40000, desc: 'Varkhald autocannon frigate, fastest hull afloat.' },
  maul:       { id: 'maul', name: 'Maul', faction: 'varkhald', cls: 'destroyer', speed: 135, warp: 660, hp: { shield: 540, armor: 580, hull: 520 }, slots: { high: 3, mid: 2, low: 3 }, cargo: 330, lockRange: 370, price: 220000, desc: 'Varkhald destroyer with relentless barrage.' },
  broadsword: { id: 'broadsword', name: 'Broadsword', faction: 'varkhald', cls: 'cruiser', speed: 110, warp: 600, hp: { shield: 1150, armor: 1500, hull: 1200 }, slots: { high: 4, mid: 3, low: 4 }, cargo: 620, lockRange: 440, price: 1200000, desc: 'Varkhald cruiser, balanced and brutal.' },
  stormcaller:{ id: 'stormcaller', name: 'Stormcaller', faction: 'varkhald', cls: 'battleship', speed: 82, warp: 510, hp: { shield: 2800, armor: 4000, hull: 3200 }, slots: { high: 6, mid: 4, low: 5 }, cargo: 900, lockRange: 540, price: 9000000, desc: 'Varkhald battleship that brings the storm.' },
  // Special
  pilgrim:    { id: 'pilgrim', name: 'Pilgrim', faction: 'sisters', cls: 'frigate', speed: 165, warp: 800, hp: { shield: 380, armor: 340, hull: 260 }, slots: { high: 2, mid: 3, low: 2 }, cargo: 220, lockRange: 380, price: 350000, desc: 'Sisters of the Veil exploration frigate.' },
  enforcer:   { id: 'enforcer', name: 'Enforcer', faction: 'directorate', cls: 'cruiser', speed: 130, warp: 900, hp: { shield: 3000, armor: 3000, hull: 2000 }, slots: { high: 5, mid: 4, low: 4 }, cargo: 400, lockRange: 600, price: 0, npcOnly: true, desc: 'Directorate response cruiser. Not for sale.' },
};

export const SHIP_CLASSES = ['frigate', 'destroyer', 'cruiser', 'battleship'];
export const CLASS_MULTIPLIER = { frigate: 1, destroyer: 2.2, cruiser: 5, battleship: 12 };

// ---- Modules ---------------------------------------------------------------
// slot: high|mid|low. type: weapon|mining|shield_boost|armor_rep|propulsion|passive
export const MODULES = {
  pulse_laser:   { id: 'pulse_laser', name: 'Pulse Laser', slot: 'high', type: 'weapon', damage: 16, rate: 2.0, range: 75, beam: '#ffd76a', price: 15000, desc: 'Aurelian energy turret.' },
  heavy_laser:   { id: 'heavy_laser', name: 'Heavy Beam Laser', slot: 'high', type: 'weapon', damage: 34, rate: 3.5, range: 100, beam: '#ffbf40', price: 90000, desc: 'Capital-grade beam, cruiser+ punch.' },
  railgun:       { id: 'railgun', name: 'Railgun', slot: 'high', type: 'weapon', damage: 20, rate: 3.0, range: 150, beam: '#7ac2ff', price: 18000, desc: 'Kaldari long-range hybrid turret.' },
  missile_launcher:{ id: 'missile_launcher', name: 'Missile Launcher', slot: 'high', type: 'weapon', damage: 30, rate: 4.0, range: 170, projectile: true, price: 20000, desc: 'Launches seeker missiles.' },
  blaster:       { id: 'blaster', name: 'Ion Blaster', slot: 'high', type: 'weapon', damage: 26, rate: 2.2, range: 50, beam: '#6affd8', price: 16000, desc: 'Meridian close-range hybrid turret.' },
  autocannon:    { id: 'autocannon', name: 'Autocannon', slot: 'high', type: 'weapon', damage: 18, rate: 1.5, range: 65, beam: '#ff9a5a', price: 14000, desc: 'Varkhald rapid projectile turret.' },
  mining_laser:  { id: 'mining_laser', name: 'Mining Laser', slot: 'high', type: 'mining', yield: 10, rate: 4.0, range: 70, beam: '#8aff8a', price: 12000, desc: 'Extracts ore from asteroids.' },
  shield_booster:{ id: 'shield_booster', name: 'Shield Booster', slot: 'mid', type: 'shield_boost', amount: 90, rate: 8.0, price: 25000, desc: 'Active shield restoration burst.' },
  afterburner:   { id: 'afterburner', name: 'Afterburner', slot: 'mid', type: 'propulsion', speedMult: 1.8, price: 20000, desc: 'Toggle: +80% sublight speed.' },
  armor_repairer:{ id: 'armor_repairer', name: 'Armor Repairer', slot: 'low', type: 'armor_rep', amount: 70, rate: 10.0, price: 25000, desc: 'Active armor restoration.' },
  shield_extender:{ id: 'shield_extender', name: 'Shield Extender', slot: 'low', type: 'passive', shieldBonus: 200, price: 22000, desc: 'Passive: +200 max shield.' },
  armor_plate:   { id: 'armor_plate', name: 'Armor Plate', slot: 'low', type: 'passive', armorBonus: 250, price: 22000, desc: 'Passive: +250 max armor.' },
  damage_amp:    { id: 'damage_amp', name: 'Weapon Amplifier', slot: 'low', type: 'passive', dmgMult: 1.18, price: 45000, desc: 'Passive: +18% weapon damage.' },
  cargo_expander:{ id: 'cargo_expander', name: 'Cargo Expander', slot: 'low', type: 'passive', cargoBonus: 250, price: 15000, desc: 'Passive: +250 m3 cargo.' },
};

// ---- Items (commodities) ----------------------------------------------------
export const ITEMS = {
  ferrite:    { id: 'ferrite', name: 'Ferrite Ore', volume: 1, basePrice: 12, desc: 'Common high-security ore.' },
  novacite:   { id: 'novacite', name: 'Novacite Ore', volume: 1.5, basePrice: 45, desc: 'Uncommon low-security ore.' },
  crystalline:{ id: 'crystalline', name: 'Crystalline Ore', volume: 2, basePrice: 140, desc: 'Rare null-security ore.' },
  sealed_cargo:{ id: 'sealed_cargo', name: 'Sealed Cargo', volume: 1, basePrice: 0, noMarket: true, desc: 'Mission cargo. Handle with care.' },
};

export function shipList() { return Object.values(SHIPS); }
export function moduleList() { return Object.values(MODULES); }
