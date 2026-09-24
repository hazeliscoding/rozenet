import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { AuthService } from '../../../services/auth.service';
import { ForumService } from '../../../services/forum.service';
import { ThreadRow } from '../../../services/forum.types';
import { apiError } from '../../../services/errors';
import { IconComponent } from '../../../components/icon';

@Component({
  selector: 'board-page',
  standalone: true,
  imports: [FormsModule, RouterLink, DatePipe, IconComponent],
  templateUrl: './board.html',
})
export class BoardPage {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private forum = inject(ForumService);
  protected auth = inject(AuthService);

  slug = this.route.snapshot.paramMap.get('slug') ?? '';
  threads = signal<ThreadRow[]>([]);
  error = signal<string | null>(null);
  composing = signal(false);
  busy = signal(false);

  newTitle = '';
  newBody = '';

  constructor() {
    if (!this.auth.token) {
      this.router.navigate(['/forum']);
      return;
    }
    this.load();
  }

  private load() {
    this.forum.threads(this.slug).subscribe({
      next: (t) => this.threads.set(t),
      error: (e) => this.handle(e),
    });
  }

  private handle(e: unknown) {
    this.error.set(apiError(e));
    // A 401 means the session lapsed — send them back to the gate.
    if (String(this.error()).includes('members only')) this.router.navigate(['/forum']);
  }

  post() {
    this.error.set(null);
    this.busy.set(true);
    this.forum.createThread(this.slug, this.newTitle, this.newBody).subscribe({
      next: ({ id }) => this.router.navigate(['/forum/t', id]),
      error: (e) => {
        this.busy.set(false);
        this.error.set(apiError(e));
      },
    });
  }
}
