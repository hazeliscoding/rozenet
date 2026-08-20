import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { AuthService } from '../../../services/auth.service';
import { ForumService } from '../../../services/forum.service';
import { Post, ThreadView, REACTION_PALETTE } from '../../../services/forum.types';
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

  readonly palette = REACTION_PALETTE;

  id = Number(this.route.snapshot.paramMap.get('id'));
  view = signal<ThreadView | null>(null);
  error = signal<string | null>(null);
  busy = signal(false);

  replyBody = '';

  // per-post transient UI state
  editingId = signal<number | null>(null);
  editBody = '';
  pickerId = signal<number | null>(null);

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

  canEdit(post: Post): boolean {
    return this.view()?.viewerId === post.authorId;
  }

  canDelete(post: Post): boolean {
    const v = this.view();
    return !!v && (v.viewerId === post.authorId || v.viewerIsAdmin);
  }

  isFirst(post: Post): boolean {
    return this.view()?.posts[0]?.id === post.id;
  }

  // ---- reactions ----
  togglePicker(postId: number) {
    this.pickerId.set(this.pickerId() === postId ? null : postId);
  }

  react(post: Post, kaomoji: string) {
    this.pickerId.set(null);
    this.forum.react(post.id, kaomoji).subscribe({
      next: (reactions) => {
        const v = this.view();
        if (!v) return;
        this.view.set({
          ...v,
          posts: v.posts.map((p) => (p.id === post.id ? { ...p, reactions } : p)),
        });
      },
      error: (e) => this.error.set(apiError(e)),
    });
  }

  // ---- edit ----
  startEdit(post: Post) {
    this.editingId.set(post.id);
    this.editBody = post.body;
  }

  cancelEdit() {
    this.editingId.set(null);
  }

  saveEdit(post: Post) {
    this.busy.set(true);
    this.forum.editPost(post.id, this.editBody).subscribe({
      next: () => {
        this.busy.set(false);
        this.editingId.set(null);
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(apiError(e));
      },
    });
  }

  // ---- delete ----
  deletePost(post: Post) {
    const opening = this.isFirst(post);
    const msg = opening
      ? 'delete this whole thread? every reply goes with it.'
      : 'delete this post?';
    if (!confirm(msg)) return;

    this.forum.deletePost(post.id).subscribe({
      next: (res) => {
        if (res.threadDeleted) this.router.navigate(['/forum/b', this.view()?.thread.boardSlug]);
        else this.load();
      },
      error: (e) => this.error.set(apiError(e)),
    });
  }

  // ---- mod tools ----
  toggleLock() {
    const v = this.view();
    if (!v) return;
    this.forum.moderate(this.id, { locked: !v.thread.locked }).subscribe({
      next: () => this.load(),
      error: (e) => this.error.set(apiError(e)),
    });
  }

  toggleSticky() {
    const v = this.view();
    if (!v) return;
    this.forum.moderate(this.id, { sticky: !v.thread.sticky }).subscribe({
      next: () => this.load(),
      error: (e) => this.error.set(apiError(e)),
    });
  }

  // ---- reply ----
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
