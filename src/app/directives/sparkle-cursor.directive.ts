import { Directive, HostListener } from '@angular/core';

const GLYPHS = ['✦', '✧', '✩'];
const COLORS = ['#ff2e55', '#ff9db2', '#f2c14e'];
const THROTTLE_MS = 45;

/** Red-and-gold sparkle trail following the cursor. Purely decorative;
 *  hidden entirely under prefers-reduced-motion (see styles.scss). */
@Directive({
  selector: '[appSparkleCursor]',
  standalone: true,
})
export class SparkleCursorDirective {
  private lastSpawn = 0;

  @HostListener('document:mousemove', ['$event'])
  onMove(event: MouseEvent) {
    const now = performance.now();
    if (now - this.lastSpawn < THROTTLE_MS) return;
    this.lastSpawn = now;

    const bit = document.createElement('span');
    bit.className = 'sparkle-bit';
    bit.textContent = GLYPHS[Math.floor(Math.random() * GLYPHS.length)];
    bit.style.color = COLORS[Math.floor(Math.random() * COLORS.length)];
    bit.style.left = `${event.clientX + (Math.random() * 14 - 7)}px`;
    bit.style.top = `${event.clientY + (Math.random() * 14 - 7)}px`;
    bit.style.fontSize = `${1 + Math.random() * 0.8}rem`;
    document.body.appendChild(bit);
    setTimeout(() => bit.remove(), 700);
  }
}
