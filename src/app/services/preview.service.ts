import { Injectable, signal } from '@angular/core';

const PREVIEW_KEY = 'rozenet_preview';

/**
 * Preview mode lets a visitor past the under-construction page to browse the
 * unfinished site. It lives in sessionStorage, so it lasts until the tab closes.
 */
@Injectable({ providedIn: 'root' })
export class PreviewService {
  readonly active = signal(readPreview());

  enter() {
    this.active.set(true);
    try {
      sessionStorage.setItem(PREVIEW_KEY, '1');
    } catch {
      // Storage blocked: preview still works until the next reload.
    }
  }
}

function readPreview(): boolean {
  try {
    return sessionStorage.getItem(PREVIEW_KEY) === '1';
  } catch {
    return false;
  }
}
