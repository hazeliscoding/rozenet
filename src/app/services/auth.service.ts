import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { AuthResponse, UserDto } from './forum.types';

const TOKEN_KEY = 'rozenet_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);

  /** Current member, or null when signed out. Components read this signal. */
  readonly user = signal<UserDto | null>(null);
  readonly ready = signal(false);

  constructor() {
    if (this.token) {
      // Validate the stored token against the API; clear it if stale.
      this.http.get<UserDto>('/api/auth/me').subscribe({
        next: (u) => {
          this.user.set(u);
          this.ready.set(true);
        },
        error: () => {
          this.clearToken();
          this.ready.set(true);
        },
      });
    } else {
      this.ready.set(true);
    }
  }

  get token(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  redeem(code: string, username: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/redeem', { code, username, password })
      .pipe(tap((res) => this.setSession(res)));
  }

  login(username: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/login', { username, password })
      .pipe(tap((res) => this.setSession(res)));
  }

  logout() {
    this.http.post('/api/auth/logout', {}).subscribe({ error: () => {} });
    this.clearToken();
    this.user.set(null);
  }

  private setSession(res: AuthResponse) {
    localStorage.setItem(TOKEN_KEY, res.token);
    this.user.set(res.user);
  }

  private clearToken() {
    localStorage.removeItem(TOKEN_KEY);
  }
}
