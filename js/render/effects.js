// Particle & beam effects with additive glow blending. Browser module.
// Particles use Kenney CC0 textures when available, gradient circles otherwise.
import { qualityPreset } from '../core/settings.js';
import { getFxSprite } from './sprites.js';

const CLS_SIZE = { frigate: 30, destroyer: 45, cruiser: 70, battleship: 110 };
const pick = (arr) => arr[(Math.random() * arr.length) | 0];
const FIRE_TEX = ['fire_01', 'flame_05', 'flame_01'];
const SMOKE_TEX = ['smoke_01', 'smoke_05', 'smoke_08'];

export class Effects {
  constructor() {
    this.particles = []; this.beams = []; this.texts = []; this.shockwaves = [];
    this.alarmT = 0; this.flashT = 0;
  }

  explosion(x, y, cls = 'frigate', color = '#ffaa55') {
    const size = CLS_SIZE[cls] ?? 30;
    const mult = qualityPreset().particleMult;
    // fireball
    const n = Math.round((Math.round(size / 2) + 16) * mult);
    for (let i = 0; i < n; i++) {
      const a = Math.random() * Math.PI * 2;
      const sp = 15 + Math.random() * size * 2;
      this.particles.push({
        x, y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
        life: 0.5 + Math.random() * 0.9, maxLife: 1.4,
        size: 2 + Math.random() * (size / 8 + 3),
        color: Math.random() < 0.35 ? '#fff2cc' : color, type: 'fire', drag: 0.92,
        tex: pick(FIRE_TEX), rot: Math.random() * Math.PI * 2,
      });
    }
    // sparks
    for (let i = 0; i < n / 2; i++) {
      const a = Math.random() * Math.PI * 2;
      const sp = size * 2.5 + Math.random() * size * 3;
      this.particles.push({
        x, y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
        life: 0.3 + Math.random() * 0.4, maxLife: 0.7,
        size: 1 + Math.random() * 1.5, color: '#ffeeaa', type: 'spark', drag: 0.97,
        tex: 'spark_01', rot: Math.random() * Math.PI * 2,
      });
    }
    // debris chunks
    for (let i = 0; i < Math.round((6 + size / 12) * mult); i++) {
      const a = Math.random() * Math.PI * 2;
      const sp = 20 + Math.random() * size;
      this.particles.push({
        x, y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
        life: 1.5 + Math.random() * 2, maxLife: 3.5,
        size: 2 + Math.random() * (size / 10 + 2),
        color: '#2a2622', type: 'debris',
        rot: Math.random() * Math.PI, rotV: (Math.random() - 0.5) * 6, drag: 0.99,
      });
    }
    // smoke
    for (let i = 0; i < Math.round(8 * mult); i++) {
      const a = Math.random() * Math.PI * 2;
      const sp = 5 + Math.random() * 20;
      this.particles.push({
        x: x + (Math.random() - 0.5) * size / 2, y: y + (Math.random() - 0.5) * size / 2,
        vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
        life: 1.5 + Math.random() * 1.5, maxLife: 3,
        size: 6 + Math.random() * size / 4, color: '#555a62', type: 'smoke', drag: 0.96,
        tex: pick(SMOKE_TEX), rot: Math.random() * Math.PI * 2,
      });
    }
    this.shockwaves.push({ x, y, r: 5, maxR: size * 2.6, life: 0.8, maxLife: 0.8 });
    this.flashT = Math.max(this.flashT, 0.12);
  }

  smallExplosion(x, y) {
    for (let i = 0; i < 10; i++) {
      const a = Math.random() * Math.PI * 2, sp = 30 + Math.random() * 70;
      this.particles.push({
        x, y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
        life: 0.35, maxLife: 0.35, size: 1.5 + Math.random() * 2,
        color: '#ffcc88', type: 'fire', drag: 0.94,
        tex: pick(FIRE_TEX), rot: Math.random() * Math.PI * 2,
      });
    }
  }

  beam(x1, y1, x2, y2, color) {
    this.beams.push({ x1, y1, x2, y2, color, life: 0.22, maxLife: 0.22 });
  }

  text(x, y, str, color = '#fff') {
    this.texts.push({ x, y, str, color, life: 1.4, maxLife: 1.4 });
  }

  trail(x, y, color = '#ff9a55', size = 2.2) {
    this.particles.push({
      x, y, vx: (Math.random() - 0.5) * 6, vy: (Math.random() - 0.5) * 6,
      life: 0.45, maxLife: 0.45, size: size * (0.7 + Math.random() * 0.6),
      color, type: 'trail', drag: 1,
      tex: 'light_01', rot: 0,
    });
  }

  warpStreak(x, y, angle) {
    this.particles.push({
      x, y, vx: 0, vy: 0, life: 0.3, maxLife: 0.3,
      size: 24 + Math.random() * 36, color: '#aaddff', type: 'streak', angle, drag: 1,
      tex: pick(['trace_01', 'trace_04']), rot: 0,
    });
  }

  warpOut(x, y) {
    for (let i = 0; i < 14; i++) {
      const a = Math.random() * Math.PI * 2;
      this.particles.push({
        x, y, vx: Math.cos(a) * 220, vy: Math.sin(a) * 220,
        life: 0.4, maxLife: 0.4, size: 2, color: '#aaddff', type: 'spark', drag: 0.98,
        tex: 'spark_04', rot: Math.random() * Math.PI * 2,
      });
    }
  }

  jumpFlash() { this.flashT = Math.max(this.flashT, 0.5); }
  alarm() { this.alarmT = 1.2; }

  update(dt) {
    for (const p of this.particles) {
      p.x += p.vx * dt; p.y += p.vy * dt;
      if (p.drag) { p.vx *= p.drag; p.vy *= p.drag; }
      if (p.rotV) p.rot += p.rotV * dt;
      p.life -= dt;
      if (p.type === 'smoke') p.size += dt * 8;
    }
    this.particles = this.particles.filter(p => p.life > 0);
    const cap = qualityPreset().particleCap;
    if (this.particles.length > cap) this.particles.splice(0, this.particles.length - cap);
    for (const b of this.beams) b.life -= dt;
    this.beams = this.beams.filter(b => b.life > 0);
    for (const t of this.texts) { t.y -= 18 * dt; t.life -= dt; }
    this.texts = this.texts.filter(t => t.life > 0);
    for (const s of this.shockwaves) { s.r += (s.maxR - s.r) * dt * 6; s.life -= dt; }
    this.shockwaves = this.shockwaves.filter(s => s.life > 0);
    if (this.alarmT > 0) this.alarmT -= dt;
    if (this.flashT > 0) this.flashT -= dt;
  }

  // Draw a textured particle; returns false if no texture (caller falls back).
  drawTex(ctx, p, alpha, stretch = 1, angle = null) {
    const img = p.tex && getFxSprite(p.tex);
    if (!img) return false;
    ctx.globalAlpha = alpha;
    const s = p.size * 2.2;
    ctx.save();
    ctx.translate(p.x, p.y);
    ctx.rotate(angle ?? p.rot ?? 0);
    ctx.drawImage(img, -s * stretch / 2, -s / 2, s * stretch, s);
    ctx.restore();
    return true;
  }

  draw(ctx) {
    // pass 1: normal blending (smoke, debris)
    for (const p of this.particles) {
      if (p.type !== 'smoke' && p.type !== 'debris') continue;
      const a = Math.max(0, p.life / p.maxLife);
      if (p.type === 'debris') {
        ctx.globalAlpha = a;
        ctx.fillStyle = p.color;
        ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.rot);
        ctx.fillRect(-p.size / 2, -p.size / 3, p.size, p.size / 1.5);
        ctx.restore();
      } else if (!this.drawTex(ctx, p, a * 0.35)) {
        ctx.globalAlpha = a * 0.25;
        ctx.fillStyle = p.color;
        ctx.beginPath(); ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2); ctx.fill();
      }
    }
    // pass 2: additive glow (fire, sparks, trails, beams, shockwaves)
    ctx.globalCompositeOperation = 'lighter';
    for (const p of this.particles) {
      if (p.type === 'smoke' || p.type === 'debris') continue;
      const a = Math.max(0, p.life / p.maxLife);
      if (p.type === 'streak') {
        if (!this.drawTex(ctx, p, a * 0.35, 2.2, p.angle)) {
          ctx.globalAlpha = a * 0.3;
          ctx.strokeStyle = p.color; ctx.lineWidth = 1.5;
          ctx.beginPath();
          ctx.moveTo(p.x - Math.cos(p.angle) * p.size, p.y - Math.sin(p.angle) * p.size);
          ctx.lineTo(p.x, p.y);
          ctx.stroke();
        }
        continue;
      }
      if (!this.drawTex(ctx, p, a * (p.type === 'trail' ? 0.6 : 0.9))) {
        ctx.globalAlpha = a * (p.type === 'trail' ? 0.6 : 0.9);
        const g = ctx.createRadialGradient(p.x, p.y, 0, p.x, p.y, p.size);
        g.addColorStop(0, p.color);
        g.addColorStop(1, 'transparent');
        ctx.fillStyle = g;
        ctx.beginPath(); ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2); ctx.fill();
      }
    }
    // beams: outer glow + white core
    for (const b of this.beams) {
      const a = Math.max(0, b.life / b.maxLife);
      ctx.globalAlpha = a * 0.55;
      ctx.strokeStyle = b.color; ctx.lineWidth = 5;
      ctx.beginPath(); ctx.moveTo(b.x1, b.y1); ctx.lineTo(b.x2, b.y2); ctx.stroke();
      ctx.globalAlpha = a;
      ctx.strokeStyle = '#ffffff'; ctx.lineWidth = 1.4;
      ctx.beginPath(); ctx.moveTo(b.x1, b.y1); ctx.lineTo(b.x2, b.y2); ctx.stroke();
    }
    for (const s of this.shockwaves) {
      ctx.globalAlpha = Math.max(0, s.life / s.maxLife) * 0.6;
      ctx.strokeStyle = '#ffddaa'; ctx.lineWidth = 2.5;
      ctx.beginPath(); ctx.arc(s.x, s.y, s.r, 0, Math.PI * 2); ctx.stroke();
    }
    ctx.globalCompositeOperation = 'source-over';
    ctx.globalAlpha = 1;
    // floating texts
    ctx.font = '11px "Rajdhani", "Segoe UI", sans-serif';
    ctx.textAlign = 'center';
    for (const t of this.texts) {
      ctx.globalAlpha = Math.max(0, t.life / t.maxLife);
      ctx.fillStyle = t.color;
      ctx.fillText(t.str, t.x, t.y);
    }
    ctx.globalAlpha = 1;
  }
}
