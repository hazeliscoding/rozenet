import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SparkleCursorDirective } from './directives/sparkle-cursor.directive';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SparkleCursorDirective],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
