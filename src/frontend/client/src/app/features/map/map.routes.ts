import { Routes } from '@angular/router';

export const MAP_ROUTES: Routes = [
  {
    path: '',
    title: '地图 · Travel Map',
    loadComponent: () => import('./pages/map-page/map-page').then((m) => m.MapPage),
  },
];
