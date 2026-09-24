import {
  Component,
  OnDestroy,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { IconComponent } from '../../components/icon';
import { DesktopComponent } from './os/desktop';
import { MobileComponent } from './os/mobile';

type SceneId = 'den' | 'stay' | 'door' | 'ask' | 'mem';

interface Scene {
  /** Which backdrop this scene stands in front of. */
  bg: 'den' | 'door' | 'figure';
  /** Nameplate above the dialogue box. */
  name: string;
  /** Vertical Japanese accent down the right edge. */
  vert: string;
  lines: string[];
}

interface Choice {
  label: string;
  /** Mono suffix — a ✓ once you've already been somewhere. */
  mark: string;
  visited: boolean;
  run: () => void;
}

interface SaveSlot {
  id: number;
  file: string;
  title: string;
  meta: string;
}


const SCENES: Record<Exclude<SceneId, 'mem'>, Scene> = {
  den: {
    bg: 'den',
    name: 'roze [angel]',
    vert: '扉はもう開いていた',
    lines: [
      "hi. i'm roze. software developer by day — but that's the other website.",
      "this one is for everything else: the anime i'm watching, the games i'm not finishing, the figures i absolutely did not need.",
      'pull up a cushion. lurkers are welcome, and the guestbook never judges.',
    ],
  },
  stay: {
    bg: 'den',
    name: 'roze [angel]',
    vert: '扉はもう開いていた',
    lines: [
      '…',
      'the den hums. somewhere, a backlog grows by one.',
      'take your time. the door was already open when you got here.',
    ],
  },
  door: {
    bg: 'door',
    name: 'the door',
    vert: '招待状はありますか',
    lines: [
      'a private forum sits behind this door, for invited friends.',
      "invites are personal. if we know each other, just ask — she'll mint you a code.",
      "if you're meant to be inside, you already know.",
    ],
  },
  ask: {
    bg: 'door',
    name: 'the door',
    vert: '招待状はありますか',
    lines: [
      'leave a note. she reads them all — the ones from humans, anyway.',
      '(request an invite: hazel.granados@protonmail.com)',
    ],
  },
};

const BOOT_LINES = [
  'ROZE-BIOS v2.6 ✦ memory check: comfy',
  'loading den.sys … OK',
  'loading backlog.dat … 眠い … OK',
  'mounting shrine … 3 FILES RECOVERED',
  'sparkle driver … OK',
  'LAST CONNECTION: UNKNOWN',
];

const FIG_LINES: Record<number, string> = {
  0: 'slot 01. the one that started the shelf. no regrets. some regrets. no — none.',
  1: "slot 02. the new arrival ✦ i absolutely did not need her. she's perfect.",
  2: 'slot 03. a preorder that took eleven months to arrive. worth every day.',
};

const SAVE_KEY = 'rozenet-vn-save';
/** Below this, ROZE-OS wears its mobile shell instead of the desktop one. */
const MOBILE_QUERY = '(max-width: 760px)';
const BOOT_TICK = 350;
const TYPE_TICK = 28;
const TYPE_STEP = 2;

@Component({
  selector: 'home-page',
  standalone: true,
  imports: [IconComponent, DesktopComponent, MobileComponent],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class HomePage implements OnDestroy {
  private router = inject(Router);

  private readonly reducedMotion =
    typeof matchMedia !== 'undefined' &&
    matchMedia('(prefers-reduced-motion: reduce)').matches;

  private readonly mobileQuery =
    typeof matchMedia !== 'undefined' ? matchMedia(MOBILE_QUERY) : null;

  /** Drives both the choice's wording and which ROZE-OS shell opens. */
  protected readonly isMobile = signal(this.mobileQuery?.matches ?? false);

  private readonly onBreakpoint = (e: MediaQueryListEvent) => this.isMobile.set(e.matches);

  // ---- state ----
  protected readonly booting = signal(true);
  protected readonly bootN = signal(0);
  protected readonly node = signal<SceneId>('den');
  protected readonly lineIndex = signal(0);
  protected readonly chars = signal(0);
  protected readonly choicesShown = signal(false);
  /** Re-entering somewhere you've read: the whole passage is shown at once. */
  protected readonly revisited = signal(false);
  protected readonly shrineShown = signal(false);
  /** ROZE-OS: the desktop holds the directory and the control room. */
  protected readonly desktopShown = signal(false);
  protected readonly logShown = signal(false);
  protected readonly toast = signal<string | null>(null);
  protected readonly backlog = signal<{ who: string; line: string }[]>([]);
  protected readonly figSel = signal(-1);

  /** Scenes already read this session — revisiting skips to the choices. */
  private readonly seen = signal<Record<string, boolean>>({});
  /** Filled in when a shrine slot is picked; rendered as the `mem` scene. */
  private memLine = '';

  private bootTimer: ReturnType<typeof setInterval>;
  private typeTimer: ReturnType<typeof setInterval>;
  private toastTimer: ReturnType<typeof setTimeout> | undefined;
  private pendingSave: SceneId | null = null;

  protected readonly slots: SaveSlot[] = [0, 1, 2].map((i) => ({
    id: i,
    file: `FIG_004${i + 1}`,
    title: `SLOT 0${i + 1} — 《 figure name 》`,
    meta: `FIG_004${i + 1}.JPG · 2026/0${7 + i}/1${i + 2}`,
  }));

  constructor() {
    this.mobileQuery?.addEventListener('change', this.onBreakpoint);

    this.bootTimer = setInterval(() => {
      this.bootN.update((n) => (n < BOOT_LINES.length ? n + 1 : n));
    }, BOOT_TICK);

    this.typeTimer = setInterval(() => {
      if (this.booting()) return;
      const full = this.currentLine().length;
      if (this.chars() < full) {
        this.chars.update((c) => Math.min(full, c + TYPE_STEP));
      }
    }, TYPE_TICK);

    try {
      const raw = localStorage.getItem(SAVE_KEY);
      const saved = raw ? (JSON.parse(raw) as { node?: string }) : null;
      if (saved?.node && saved.node in SCENES) {
        this.pendingSave = saved.node as SceneId;
      }
    } catch {
      // a corrupt save is not worth crashing the den over
    }
  }

  ngOnDestroy() {
    clearInterval(this.bootTimer);
    clearInterval(this.typeTimer);
    clearTimeout(this.toastTimer);
    this.mobileQuery?.removeEventListener('change', this.onBreakpoint);
  }

  // ---- derived ----
  protected readonly bootLines = computed(() => BOOT_LINES.slice(0, this.bootN()));

  protected readonly scene = computed<Scene>(() => {
    if (this.node() === 'mem') {
      return {
        // The picked slot fills the screen while she talks about it.
        bg: 'figure',
        name: 'roze [angel]',
        vert: '祭壇',
        lines: [this.memLine],
      };
    }
    return SCENES[this.node() as Exclude<SceneId, 'mem'>];
  });

  protected readonly typedText = computed(() =>
    this.currentLine().slice(0, this.chars()),
  );

  protected readonly advanceShown = computed(
    () => this.chars() >= this.currentLine().length && !this.choicesShown(),
  );

  protected readonly statusLine = computed(() =>
    this.node() === 'door' || this.node() === 'ask'
      ? 'DOOR STATUS: LOCKED · MEMBERS INSIDE: CLASSIFIED'
      : 'DEN STATUS: COMFY · DOOR: LOCKED',
  );

  protected readonly backdropHint = computed(() =>
    this.scene().bg === 'door'
      ? 'backdrop — a door at the end of a hallway, at night'
      : 'backdrop — your room / desk / shelf at night',
  );

  /** The slot on screen during a `mem` scene, or null anywhere else. */
  protected readonly figure = computed(() =>
    this.scene().bg === 'figure' ? (this.slots[this.figSel()] ?? null) : null,
  );

  protected readonly choices = computed<Choice[]>(() => {
    const seen = this.seen();
    const mk = (label: string, key: string, run: () => void): Choice => ({
      label,
      mark: seen[key] ? ' ✓' : '',
      visited: !!seen[key],
      run,
    });

    switch (this.node()) {
      case 'den':
        return [
          // The directory and the control room both live in ROZE-OS now, which
          // wears a desktop or a mobile shell depending on the screen.
          mk(
            this.isMobile() ? 'browse the device' : 'browse the desktop',
            'desktop',
            () => this.openDesktop(),
          ),
          mk('visit the shrine', 'shrine', () => this.shrineShown.set(true)),
          mk('stay a while', 'stay', () => this.goto('stay')),
        ];
      case 'door':
        return [
          mk('knock (i have a code)', 'forum', () => this.router.navigate(['/forum'])),
          mk('ask for an invite', 'ask', () => this.goto('ask')),
          mk('walk away', 'den', () => this.goto('den')),
        ];
      case 'ask':
        return [
          mk('back to the door', 'door', () => this.goto('door')),
          mk('walk away', 'den', () => this.goto('den')),
        ];
      case 'mem':
        return [
          mk('back to the shrine', 'shrine', () => this.shrineShown.set(true)),
          mk('back', 'den', () => this.goto('den')),
        ];
      default:
        return [mk('back', 'den', () => this.goto('den'))];
    }
  });

  // ---- interaction ----
  finishBoot() {
    if (!this.booting()) return;
    this.booting.set(false);
    clearInterval(this.bootTimer);
    if (this.pendingSave) {
      this.node.set(this.pendingSave);
      this.showToast('save data loaded — おかえり');
      this.pendingSave = null;
    }
    this.resetLine();
  }

  /** Click / Space / Enter on the scene: finish the line, then step through it. */
  advance() {
    if (this.booting()) {
      this.finishBoot();
      return;
    }
    if (this.shrineShown() || this.logShown() || this.desktopShown()) return;

    const full = this.currentLine().length;
    if (this.chars() < full) {
      this.chars.set(full);
      return;
    }

    this.logLine();
    const scene = this.scene();
    if (this.lineIndex() + 1 < scene.lines.length) {
      this.lineIndex.update((i) => i + 1);
      this.chars.set(this.reducedMotion ? scene.lines[this.lineIndex()].length : 0);
    } else {
      this.choicesShown.set(true);
      this.markSeen(this.node());
    }
  }

  onKeydown(event: KeyboardEvent) {
    // The desktop handles its own Escape.
    if (event.key === 'Escape') {
      if (this.shrineShown()) this.shrineShown.set(false);
      else if (this.logShown()) this.logShown.set(false);
      return;
    }
    if (event.key === ' ' || event.key === 'Enter') {
      const target = event.target as HTMLElement | null;
      // Let real controls handle their own keys.
      if (target?.closest('button, a, input, textarea')) return;
      event.preventDefault();
      this.advance();
    }
  }

  toggleLog() {
    this.logShown.update((v) => !v);
  }

  closeShrine() {
    this.shrineShown.set(false);
  }

  openDesktop() {
    this.markSeen('desktop');
    this.desktopShown.set(true);
  }

  closeDesktop() {
    this.desktopShown.set(false);
  }

  /** The desktop's control room sends you back to the door, in the den. */
  desktopToDoor() {
    this.desktopShown.set(false);
    this.goto('door');
  }

  pickSlot(slot: SaveSlot) {
    this.memLine = FIG_LINES[slot.id];
    this.figSel.set(slot.id);
    this.shrineShown.set(false);
    this.markSeen('shrine');
    this.node.set('mem');
    this.resetLine();
  }

  saveGame() {
    const node = this.node() === 'mem' ? 'den' : this.node();
    try {
      localStorage.setItem(SAVE_KEY, JSON.stringify({ node }));
      this.showToast('保存 — saved.');
    } catch {
      this.showToast('save failed — storage is full or blocked');
    }
  }

  goTitle() {
    this.booting.set(true);
    this.bootN.set(BOOT_LINES.length);
  }

  // ---- internals ----
  private currentLine(): string {
    return this.scene().lines[this.lineIndex()] ?? '';
  }

  private goto(next: SceneId) {
    const alreadyRead = !!this.seen()[next];
    this.node.set(next);
    // Somewhere you've already read: don't retype it a line at a time — lay the
    // whole passage out and go straight to the choices.
    if (alreadyRead) {
      const lines = this.scene().lines;
      this.revisited.set(true);
      this.lineIndex.set(lines.length - 1);
      this.chars.set(lines[lines.length - 1].length);
      this.choicesShown.set(true);
    } else {
      this.resetLine();
    }
  }

  private resetLine() {
    this.revisited.set(false);
    this.lineIndex.set(0);
    this.chars.set(this.reducedMotion ? this.currentLine().length : 0);
    this.choicesShown.set(false);
  }

  private markSeen(key: string) {
    this.seen.update((s) => ({ ...s, [key]: true }));
  }

  private logLine() {
    const scene = this.scene();
    const line = scene.lines[this.lineIndex()];
    if (!line) return;
    this.backlog.update((b) => [...b, { who: scene.name, line }].slice(-40));
  }

  private showToast(message: string) {
    this.toast.set(message);
    clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toast.set(null), 1800);
  }
}
