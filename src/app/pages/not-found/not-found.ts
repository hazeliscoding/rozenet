import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../components/icon';

@Component({
  selector: 'not-found-page',
  standalone: true,
  imports: [RouterLink, IconComponent],
  templateUrl: './not-found.html',
  styleUrl: './not-found.scss',
})
export class NotFoundPage {}
