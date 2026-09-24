import { inject, isDevMode } from '@angular/core';
import { Routes } from '@angular/router';
import { PreviewService } from './services/preview.service';

const siteRoutes: Routes = [
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
    path: 'forum/b/:slug',
    loadComponent: () => import('./pages/forum/board/board').then((m) => m.BoardPage),
  },
  {
    path: 'forum/t/:id',
    loadComponent: () => import('./pages/forum/thread/thread').then((m) => m.ThreadPage),
  },
  {
    path: '**',
    loadComponent: () =>
      import('./pages/not-found/not-found').then((m) => m.NotFoundPage),
  },
];

// The site is under construction: production builds show the placeholder page
// on every path, while `ng serve` (dev builds) keeps the real site. The
// placeholder's preview button lets the real site through for that tab.
const underConstructionRoutes: Routes = [
  {
    path: '**',
    canMatch: [() => !inject(PreviewService).active()],
    loadComponent: () =>
      import('./pages/under-construction/under-construction').then(
        (m) => m.UnderConstructionPage,
      ),
  },
  ...siteRoutes,
];

export const routes: Routes = isDevMode()
  ? siteRoutes
  : underConstructionRoutes;
