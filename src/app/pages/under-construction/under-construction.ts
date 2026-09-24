import { Component, HostListener, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { LinksService } from '../../services/links.service';
import { PreviewService } from '../../services/preview.service';
import { LinkCategory, LinkItem } from '../../data/links.data';

@Component({
  selector: 'under-construction-page',
  standalone: true,
  templateUrl: './under-construction.html',
  styleUrl: './under-construction.scss',
})
export class UnderConstructionPage {
  private linksService = inject(LinksService);
  private router = inject(Router);
  private preview = inject(PreviewService);

  categories = signal<LinkCategory[]>([]);
  linksOpen = signal(false);
  note = signal<string | null>(null);
  private noteTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.linksService
      .getCategories()
      .subscribe((cats) => this.categories.set(cats));
  }

  get linkCount(): number {
    return this.categories().reduce((n, c) => n + c.links.length, 0);
  }

  /** Preview the unfinished site, starting from whatever path the visitor landed on. */
  stepInside() {
    this.preview.enter();
    // Same URL, but this page's route no longer matches it — re-run the match.
    this.router.navigateByUrl(this.router.url, { onSameUrlNavigation: 'reload' });
  }

  openLinks() {
    this.linksOpen.set(true);
  }

  closeLinks() {
    this.linksOpen.set(false);
    this.note.set(null);
  }

  @HostListener('document:keydown.escape')
  onEscape() {
    this.closeLinks();
  }

  open(link: LinkItem) {
    if (link.copyText) {
      navigator.clipboard
        .writeText(link.copyText)
        .then(() => this.showNote('copied my handle ♥'))
        .catch(() => this.showNote("couldn't copy... it's " + link.copyText));
      return;
    }
    window.open(link.url, '_blank', 'noopener');
  }

  private showNote(message: string) {
    this.note.set(message);
    clearTimeout(this.noteTimer);
    this.noteTimer = setTimeout(() => this.note.set(null), 2600);
  }
}
