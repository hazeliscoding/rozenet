import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'forum-page',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './forum.html',
})
export class ForumPage {}
