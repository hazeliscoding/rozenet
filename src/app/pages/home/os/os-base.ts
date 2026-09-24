import { Directive, OnDestroy, computed, inject, output, signal } from '@angular/core';
import { Router } from '@angular/router';
import { LinkItem } from '../../../data/links.data';
import { LinksService } from '../../../services/links.service';

/** A directory row, flattened out of its category for the OS tables. */
export interface DirRow {
  glyph: string;
  label: string;
  note: string;
  action: string;
  category: string;
  link: LinkItem;
}

export interface LogLine {
  t: string;
  message: string;
  anomalous?: boolean;
}

/**
 * Everything ROZE-OS does regardless of which shell it's wearing: the
 * directory, den.log, the clock, toasts, and the way out.
 *
 * The desktop (windows + taskbar) and the mobile shell (app grid + dock) are
 * two presentations of this.
 */
@Directive()
export abstract class RozeOsBase implements OnDestroy {
  protected readonly router = inject(Router);
  private readonly linksService = inject(LinksService);

  /** Leave ROZE-OS and go back to the scene. */
  readonly closed = output<void>();
  /** Leave ROZE-OS and pick the visual novel up at the door. */
  readonly door = output<void>();

  readonly dirRows = signal<DirRow[]>([]);
  readonly query = signal('');
  readonly category = signal<string | null>(null);
  readonly toast = signal<string | null>(null);
  readonly clock = signal(this.now());
  readonly log = signal<LogLine[]>([
    { t: '[02:14]', message: 'den status: comfy' },
    { t: '[02:14]', message: 'guest connected · lurking is allowed' },
    { t: '[--:--]', message: 'LAST CONNECTION: UNKNOWN', anomalous: true },
  ]);

  private toastTimer: ReturnType<typeof setTimeout> | undefined;
  private readonly clockTimer: ReturnType<typeof setInterval>;

  constructor() {
    this.clockTimer = setInterval(() => this.clock.set(this.now()), 30_000);

    // Same source as /links — the OS is another view of the directory, not a
    // second copy of it.
    this.linksService.getCategories().subscribe((cats) =>
      this.dirRows.set(
        cats.flatMap((c) =>
          c.links.map((link) => ({
            glyph: link.glyph,
            label: link.label,
            note: link.description,
            action: link.copyText ? 'copy »' : 'visit »',
            category: c.title,
            link,
          })),
        ),
      ),
    );
  }

  ngOnDestroy() {
    clearInterval(this.clockTimer);
    clearTimeout(this.toastTimer);
  }

  // ---- directory ----
  readonly linkCount = computed(() => this.dirRows().length);

  readonly categories = computed(() => [...new Set(this.dirRows().map((r) => r.category))]);

  readonly visibleLinks = computed(() => {
    const q = this.query().trim().toLowerCase();
    const only = this.category();
    return this.dirRows().filter(
      (r) =>
        (!only || r.category === only) &&
        (!q || r.label.toLowerCase().includes(q) || r.note.toLowerCase().includes(q)),
    );
  });

  setCategory(title: string | null) {
    this.category.set(title);
  }

  /** Opens the link, or copies the handle when that's what the row is for. */
  openLink(row: DirRow) {
    const { link } = row;
    if (link.copyText) {
      navigator.clipboard
        .writeText(link.copyText)
        .then(() => {
          this.showToast('copied to clipboard ♥');
          this.addLog('copied ' + link.label);
        })
        .catch(() => this.showToast("couldn't copy… it's " + link.copyText));
      return;
    }
    this.addLog('visited ' + link.label);
    window.open(link.url, '_blank', 'noopener');
  }

  /** The directory also has a full page of its own. */
  openDirectoryPage() {
    this.router.navigate(['/links']);
  }

  // ---- the way out ----
  /** The control room is members-only: send them to the door in the den. */
  standAtDoor() {
    this.addLog('knock detected · identity unverified', true);
    this.door.emit();
  }

  exit() {
    this.closed.emit();
  }

  // ---- shared chrome ----
  protected addLog(message: string, anomalous = false) {
    this.log.update((l) => [...l, { t: `[${this.now()}]`, message, anomalous }].slice(-30));
  }

  protected showToast(message: string) {
    this.toast.set(message);
    clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toast.set(null), 1800);
  }

  protected now(): string {
    const d = new Date();
    return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  }
}
