import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ForumService } from '../../services/forum.service';
import { Board, InviteResult, InviteRow } from '../../services/forum.types';
import { apiError } from '../../services/errors';
import { IconComponent } from '../../components/icon';

type Mode = 'login' | 'redeem';

@Component({
  selector: 'forum-page',
  standalone: true,
  imports: [FormsModule, RouterLink, IconComponent],
  templateUrl: './forum.html',
  styleUrl: './forum.scss',
})
export class ForumPage {
  protected auth = inject(AuthService);
  private forum = inject(ForumService);
  private route = inject(ActivatedRoute);

  mode = signal<Mode>('login');
  boards = signal<Board[]>([]);
  error = signal<string | null>(null);
  busy = signal(false);

  // auth form fields
  code = '';
  username = '';
  password = '';

  // admin invite panel
  inviteEmail = '';
  inviteResult = signal<InviteResult | null>(null);
  inviteError = signal<string | null>(null);
  inviteBusy = signal(false);
  invites = signal<InviteRow[]>([]);
  copied = signal(false);

  constructor() {
    // An invite link (…/forum?invite=CODE) drops the visitor straight into the
    // redeem form with the code filled in.
    const invite = this.route.snapshot.queryParamMap.get('invite');
    if (invite && !this.auth.user()) {
      this.code = invite;
      this.mode.set('redeem');
    }

    if (this.auth.user()) this.afterSignedIn();
  }

  setMode(mode: Mode) {
    this.mode.set(mode);
    this.error.set(null);
  }

  submit() {
    this.error.set(null);
    this.busy.set(true);
    const done = { next: () => this.onAuthed(), error: (e: unknown) => this.fail(e) };
    if (this.mode() === 'redeem') {
      this.auth.redeem(this.code, this.username, this.password).subscribe(done);
    } else {
      this.auth.login(this.username, this.password).subscribe(done);
    }
  }

  logout() {
    this.auth.logout();
    this.boards.set([]);
    this.invites.set([]);
    this.inviteResult.set(null);
  }

  // ---- admin invites ----
  sendEmailInvite() {
    if (!this.inviteEmail.trim()) return;
    this.inviteError.set(null);
    this.inviteBusy.set(true);
    this.copied.set(false);
    this.forum.emailInvite(this.inviteEmail).subscribe({
      next: (res) => this.onInvite(res),
      error: (e) => {
        this.inviteBusy.set(false);
        this.inviteError.set(apiError(e));
      },
    });
  }

  mintCode() {
    this.inviteError.set(null);
    this.inviteBusy.set(true);
    this.copied.set(false);
    this.forum.mintInvite().subscribe({
      next: (res) => this.onInvite(res),
      error: (e) => {
        this.inviteBusy.set(false);
        this.inviteError.set(apiError(e));
      },
    });
  }

  copyLink() {
    const link = this.inviteResult()?.link;
    if (!link) return;
    navigator.clipboard.writeText(link).then(() => this.copied.set(true));
  }

  private onInvite(res: InviteResult) {
    this.inviteBusy.set(false);
    this.inviteEmail = '';
    this.inviteResult.set(res);
    this.loadInvites();
  }

  private onAuthed() {
    this.busy.set(false);
    this.password = '';
    this.afterSignedIn();
  }

  private afterSignedIn() {
    this.loadBoards();
    if (this.auth.user()?.isAdmin) this.loadInvites();
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

  private loadInvites() {
    this.forum.invites().subscribe({
      next: (i) => this.invites.set(i),
      error: () => {},
    });
  }
}
