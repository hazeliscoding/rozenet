import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { LinksService } from '../../services/links.service';
import { LinkCategory, LinkItem } from '../../data/links.data';
import { IconComponent } from '../../components/icon';

@Component({
  selector: 'links-page',
  standalone: true,
  imports: [FormsModule, RouterLink, IconComponent],
  templateUrl: './links.html',
})
export class LinksPage {
  private linksService = inject(LinksService);

  readonly categories = signal<LinkCategory[]>([]);
  readonly toast = signal<string | null>(null);
  private toastTimer: ReturnType<typeof setTimeout> | undefined;

  /** Toolbar state — the design gives directory.exe a search box and filters. */
  readonly query = signal('');
  readonly activeCategory = signal<string | null>(null);

  constructor() {
    this.linksService.getCategories().subscribe((cats) => this.categories.set(cats));
  }

  readonly linkCount = computed(() =>
    this.categories().reduce((n, c) => n + c.links.length, 0),
  );

  /** Categories after the toolbar filters, with empty ones dropped. */
  readonly visible = computed<LinkCategory[]>(() => {
    const q = this.query().trim().toLowerCase();
    const only = this.activeCategory();

    return this.categories()
      .filter((c) => !only || c.title === only)
      .map((c) => ({
        ...c,
        links: q
          ? c.links.filter(
              (l) =>
                l.label.toLowerCase().includes(q) ||
                l.description.toLowerCase().includes(q),
            )
          : c.links,
      }))
      .filter((c) => c.links.length > 0);
  });

  readonly shownCount = computed(() =>
    this.visible().reduce((n, c) => n + c.links.length, 0),
  );

  setCategory(title: string | null) {
    this.activeCategory.set(title);
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
