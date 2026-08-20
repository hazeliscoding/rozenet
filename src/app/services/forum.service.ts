import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Board, ThreadRow, ThreadView } from './forum.types';

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
}
