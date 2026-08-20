import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { AuthService } from '../../../services/auth.service';
import { ForumService } from '../../../services/forum.service';
import { ThreadView } from '../../../services/forum.types';
import { apiError } from '../../../services/errors';

@Component({
  selector: 'thread-page',
  standalone: true,
  imports: [FormsModule, RouterLink, DatePipe],
  templateUrl: './thread.html',
})
export class ThreadPage {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private forum = inject(ForumService);
  protected auth = inject(AuthService);

  id = Number(this.route.snapshot.paramMap.get('id'));
  view = signal<ThreadView | null>(null);
  error = signal<string | null>(null);
  busy = signal(false);

  replyBody = '';

  constructor() {
    if (!this.auth.token) {
      this.router.navigate(['/forum']);
      return;
    }
    this.load();
  }

  private load() {
    this.forum.thread(this.id).subscribe({
      next: (v) => this.view.set(v),
      error: (e) => {
        this.error.set(apiError(e));
        if (String(this.error()).includes('members only')) this.router.navigate(['/forum']);
      },
    });
  }

  reply() {
    this.error.set(null);
    this.busy.set(true);
    this.forum.reply(this.id, this.replyBody).subscribe({
      next: () => {
        this.replyBody = '';
        this.busy.set(false);
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(apiError(e));
      },
    });
  }
}
