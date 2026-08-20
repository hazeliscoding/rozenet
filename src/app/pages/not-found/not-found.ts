import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'not-found-page',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './not-found.html',
})
export class NotFoundPage {}
