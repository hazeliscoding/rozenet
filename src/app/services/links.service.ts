import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, timeout } from 'rxjs/operators';
import { LINK_CATEGORIES, LinkCategory } from '../data/links.data';

/** Serves the link directory. Tries the API (Postgres-backed when the
 *  docker stack is up); falls back to the bundled data so the site works
 *  as a pure static SPA too. */
@Injectable({ providedIn: 'root' })
export class LinksService {
  private http = inject(HttpClient);

  getCategories(): Observable<LinkCategory[]> {
    return this.http.get<LinkCategory[]>('/api/links').pipe(
      timeout(1500),
      catchError(() => of(LINK_CATEGORIES)),
    );
  }
}
