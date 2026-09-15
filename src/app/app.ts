import { Component, isDevMode } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SparkleCursorDirective } from './directives/sparkle-cursor.directive';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SparkleCursorDirective],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  // Prod builds show only the under-construction page, without the site chrome.
  protected readonly underConstruction = !isDevMode();
}
