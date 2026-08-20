import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ForumService } from '../../services/forum.service';
import { Board } from '../../services/forum.types';
import { apiError } from '../../services/errors';

type Mode = 'login' | 'redeem';

@Component({
  selector: 'forum-page',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './forum.html',
})
export class ForumPage {
  protected auth = inject(AuthService);
  private forum = inject(ForumService);

  mode = signal<Mode>('login');
  boards = signal<Board[]>([]);
  error = signal<string | null>(null);
  busy = signal(false);

  // form fields
  code = '';
  username = '';
  password = '';

  constructor() {
    if (this.auth.user()) this.loadBoards();
  }

  setMode(mode: Mode) {
    this.mode.set(mode);
    this.error.set(null);
  }

  submit() {
    this.error.set(null);
    this.busy.set(true);
    const done = { next: () => this.afterAuth(), error: (e: unknown) => this.fail(e) };

    if (this.mode() === 'redeem') {
      this.auth.redeem(this.code, this.username, this.password).subscribe(done);
    } else {
      this.auth.login(this.username, this.password).subscribe(done);
    }
  }

  logout() {
    this.auth.logout();
    this.boards.set([]);
  }

  private afterAuth() {
    this.busy.set(false);
    this.password = '';
    this.loadBoards();
  }

  private fail(e: unknown) {
    this.busy.set(false);
    this.error.set(apiError(e));
  }

  private loadBoards() {
    this.forum.boards().subscribe({
      next: (b) => this.boards.set(b),
      error: (e) => this.error.set(apiError(e)),
    });
  }
}
