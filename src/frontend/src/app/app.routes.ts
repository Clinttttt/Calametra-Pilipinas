import { type Routes } from '@angular/router';

/**
 * Route table.
 *
 * Navigation is intentionally small — six destinations, matching the project
 * concept. Every feature is lazily loaded so the initial payload carries only
 * the map, which is what the user came for.
 */
export const routes: Routes = [
  {
    path: 'explore',
    title: 'Explore · Calametra Pilipinas',
    loadComponent: () => import('./features/explore/explore').then((m) => m.Explore),
  },
  {
    path: 'events',
    title: 'Earthquake catalogue · Calametra Pilipinas',
    loadComponent: () => import('./features/events/events').then((m) => m.Events),
  },
  {
    path: 'stories',
    title: 'Stories · Calametra Pilipinas',
    loadComponent: () => import('./features/story/story').then((m) => m.StoryView),
  },
  {
    path: 'about-data',
    title: 'About the data · Calametra Pilipinas',
    loadComponent: () => import('./features/about-data/about-data').then((m) => m.AboutData),
  },
  { path: '', pathMatch: 'full', redirectTo: 'explore' },
  { path: '**', redirectTo: 'explore' },
];
