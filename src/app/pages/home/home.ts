import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'home-page',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './home.html',
})
export class HomePage {}
