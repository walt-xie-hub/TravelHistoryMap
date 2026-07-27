import { Routes } from '@angular/router';

export const HOME_ROUTES: Routes = [
  {
    path: '',
    title: '首页 · Travel Map',
    loadComponent: () => import('./pages/home-page/home-page').then((m) => m.HomePage),
  },
];
