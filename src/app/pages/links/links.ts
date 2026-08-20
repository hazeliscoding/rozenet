import { Component, inject, signal } from '@angular/core';
import { LinksService } from '../../services/links.service';
import { LinkCategory, LinkItem } from '../../data/links.data';

@Component({
  selector: 'links-page',
  standalone: true,
  imports: [],
  templateUrl: './links.html',
})
export class LinksPage {
  private linksService = inject(LinksService);

  categories = signal<LinkCategory[]>([]);
  toast = signal<string | null>(null);
  private toastTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.linksService
      .getCategories()
      .subscribe((cats) => this.categories.set(cats));
  }

  get linkCount(): number {
    return this.categories().reduce((n, c) => n + c.links.length, 0);
  }

  open(link: LinkItem) {
    if (link.copyText) {
      navigator.clipboard
        .writeText(link.copyText)
        .then(() => this.showToast('copied my handle! ok now message me ♥'))
        .catch(() => this.showToast("couldn't copy... it's " + link.copyText));
      return;
    }
    window.open(link.url, '_blank', 'noopener');
  }

  private showToast(message: string) {
    this.toast.set(message);
    clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toast.set(null), 2600);
  }
}
