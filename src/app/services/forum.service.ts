import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Board, InviteResult, InviteRow, Reaction, ThreadRow, ThreadView } from './forum.types';

@Injectable({ providedIn: 'root' })
export class ForumService {
  private http = inject(HttpClient);

  boards(): Observable<Board[]> {
    return this.http.get<Board[]>('/api/boards');
  }

  threads(slug: string): Observable<ThreadRow[]> {
    return this.http.get<ThreadRow[]>(`/api/boards/${slug}/threads`);
  }

  thread(id: number): Observable<ThreadView> {
    return this.http.get<ThreadView>(`/api/threads/${id}`);
  }

  createThread(slug: string, title: string, body: string): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`/api/boards/${slug}/threads`, { title, body });
  }

  reply(threadId: number, body: string): Observable<unknown> {
    return this.http.post(`/api/threads/${threadId}/posts`, { body });
  }

  react(postId: number, kaomoji: string): Observable<Reaction[]> {
    return this.http.post<Reaction[]>(`/api/posts/${postId}/reactions`, { kaomoji });
  }

  editPost(postId: number, body: string): Observable<unknown> {
    return this.http.patch(`/api/posts/${postId}`, { body });
  }

  deletePost(postId: number): Observable<{ threadDeleted: boolean; threadId: number }> {
    return this.http.delete<{ threadDeleted: boolean; threadId: number }>(`/api/posts/${postId}`);
  }

  moderate(threadId: number, change: { locked?: boolean; sticky?: boolean }): Observable<unknown> {
    return this.http.post(`/api/threads/${threadId}/moderate`, change);
  }

  // ---- admin: invites ----
  mintInvite(): Observable<InviteResult> {
    return this.http.post<InviteResult>('/api/admin/invites', {});
  }

  emailInvite(email: string): Observable<InviteResult> {
    return this.http.post<InviteResult>('/api/admin/invites/email', { email });
  }

  invites(): Observable<InviteRow[]> {
    return this.http.get<InviteRow[]>('/api/admin/invites');
  }
}
