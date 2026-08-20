import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/home/home').then((m) => m.HomePage),
  },
  {
    path: 'links',
    loadComponent: () => import('./pages/links/links').then((m) => m.LinksPage),
  },
  {
    path: 'forum',
    loadComponent: () => import('./pages/forum/forum').then((m) => m.ForumPage),
  },
  {
    path: '**',
    loadComponent: () =>
      import('./pages/not-found/not-found').then((m) => m.NotFoundPage),
  },
];
