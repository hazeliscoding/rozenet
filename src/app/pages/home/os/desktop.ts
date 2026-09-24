import { Component, OnDestroy, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IconComponent, IconName } from '../../../components/icon';
import { RozeOsBase } from './os-base';

type WinId = 'directory' | 'control' | 'denlog';

interface WinState {
  open: boolean;
  min: boolean;
  x: number;
  y: number;
  z: number;
}

interface DeskIcon {
  id: WinId;
  icon: IconName;
  label: string;
  tag?: string;
}

const WIN_IDS: WinId[] = ['directory', 'control', 'denlog'];

const WIN_LABELS: Record<WinId, string> = {
  directory: 'directory.exe',
  control: 'control room',
  denlog: 'den.log',
};

const WIN_WIDTHS: Record<WinId, number> = {
  directory: 720,
  control: 460,
  denlog: 420,
};

/**
 * ROZE-OS, desktop shell. Ported from the ROZE-OS Prototype: icons, draggable
 * windows with a z-order, a taskbar and a start menu.
 */
@Component({
  selector: 'roze-desktop',
  standalone: true,
  imports: [FormsModule, IconComponent],
  templateUrl: './desktop.html',
  styleUrl: './desktop.scss',
})
export class DesktopComponent extends RozeOsBase implements OnDestroy {
  protected readonly icons: DeskIcon[] = [
    { id: 'directory', icon: 'folder', label: 'the directory' },
    { id: 'control', icon: 'lock', label: 'control room', tag: '[locked]' },
    { id: 'denlog', icon: 'file', label: 'den.log' },
  ];

  protected readonly wins = signal<Record<WinId, WinState>>({
    directory: { open: false, min: false, x: 150, y: 70, z: 1 },
    control: { open: false, min: false, x: 320, y: 120, z: 1 },
    denlog: { open: true, min: false, x: 640, y: 320, z: 1 },
  });

  protected readonly menuOpen = signal(false);

  private topZ = 2;
  private drag: { id: WinId; dx: number; dy: number } | null = null;

  private readonly onPointerMove = (event: PointerEvent) => {
    const d = this.drag;
    if (!d) return;
    this.patch(d.id, {
      x: Math.max(0, event.clientX - d.dx),
      y: Math.max(0, event.clientY - d.dy),
    });
  };

  private readonly onPointerUp = () => {
    this.drag = null;
  };

  constructor() {
    super();
    this.clampToViewport();
    window.addEventListener('pointermove', this.onPointerMove);
    window.addEventListener('pointerup', this.onPointerUp);
  }

  override ngOnDestroy() {
    super.ngOnDestroy();
    window.removeEventListener('pointermove', this.onPointerMove);
    window.removeEventListener('pointerup', this.onPointerUp);
  }

  // ---- derived ----
  /** Open windows, as taskbar buttons. */
  protected readonly taskbar = computed(() => {
    const wins = this.wins();
    return WIN_IDS.filter((id) => wins[id].open).map((id) => ({
      id,
      label: WIN_LABELS[id],
      minimized: wins[id].min,
    }));
  });

  protected isShown(id: WinId): boolean {
    const w = this.wins()[id];
    return w.open && !w.min;
  }

  protected win(id: WinId): WinState {
    return this.wins()[id];
  }

  // ---- window management ----
  open(id: WinId) {
    if (!this.wins()[id].open) this.addLog('opened ' + WIN_LABELS[id]);
    this.patch(id, { open: true, min: false, z: ++this.topZ });
    this.menuOpen.set(false);
  }

  close(id: WinId, event: Event) {
    event.stopPropagation();
    this.patch(id, { open: false });
  }

  minimize(id: WinId, event: Event) {
    event.stopPropagation();
    this.patch(id, { min: true });
  }

  focus(id: WinId) {
    this.patch(id, { z: ++this.topZ });
  }

  toggleFromTaskbar(id: WinId) {
    if (this.wins()[id].min) this.open(id);
    else this.patch(id, { min: true });
  }

  startDrag(id: WinId, event: PointerEvent) {
    // Let the title-bar buttons work; narrow screens get the mobile shell.
    if ((event.target as HTMLElement).closest('button')) return;
    if (window.innerWidth <= 760) return;
    const w = this.wins()[id];
    this.drag = { id, dx: event.clientX - w.x, dy: event.clientY - w.y };
    this.focus(id);
  }

  // ---- chrome ----
  toggleMenu(event: Event) {
    event.stopPropagation();
    this.menuOpen.update((v) => !v);
  }

  dismissMenu() {
    if (this.menuOpen()) this.menuOpen.set(false);
  }

  onKeydown(event: KeyboardEvent) {
    if (event.key !== 'Escape') return;
    if (this.menuOpen()) this.menuOpen.set(false);
    else this.exit();
  }

  // ---- internals ----
  private patch(id: WinId, patch: Partial<WinState>) {
    this.wins.update((w) => ({ ...w, [id]: { ...w[id], ...patch } }));
  }

  /** Keep the seeded window positions on screen on smaller displays. */
  private clampToViewport() {
    if (typeof window === 'undefined') return;
    this.wins.update((wins) => {
      const next = { ...wins };
      for (const id of WIN_IDS) {
        const w = next[id];
        next[id] = {
          ...w,
          x: Math.max(0, Math.min(w.x, window.innerWidth - WIN_WIDTHS[id] - 8)),
          y: Math.max(0, Math.min(w.y, window.innerHeight - 200)),
        };
      }
      return next;
    });
  }
}
