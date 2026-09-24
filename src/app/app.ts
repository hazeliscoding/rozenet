import { Component, computed, inject, isDevMode } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs/operators';
import { SparkleCursorDirective } from './directives/sparkle-cursor.directive';
import { AuthService } from './services/auth.service';
import { PreviewService } from './services/preview.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SparkleCursorDirective],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private router = inject(Router);
  protected auth = inject(AuthService);

  private preview = inject(PreviewService);

  // Prod builds show only the under-construction page, without the site chrome,
  // until the visitor chooses to preview the site.
  protected readonly underConstruction = computed(() => !isDevMode() && !this.preview.active());

  private readonly path = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects.split(/[?#]/)[0]),
    ),
    { initialValue: this.router.url.split(/[?#]/)[0] },
  );

  /** The den is a full-viewport visual novel — it carries its own chrome. */
  protected readonly immersive = computed(() => this.path() === '/');
  protected readonly showChrome = computed(() => !this.underConstruction() && !this.immersive());

  protected readonly onForum = computed(() => this.path().startsWith('/forum'));

  /** Mono breadcrumb in the nav bar, mirroring the ROZE-OS screens. */
  protected readonly crumb = computed(() => {
    const p = this.path();
    if (p.startsWith('/links')) return '⌂ den » links';
    if (p.startsWith('/forum')) return '⌂ den » control room';
    return '⌂ den';
  });

  logout() {
    this.auth.logout();
    this.router.navigate(['/forum']);
  }
}
