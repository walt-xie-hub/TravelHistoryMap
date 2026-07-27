import { Routes } from '@angular/router';

export const USERS_ROUTES: Routes = [
  {
    path: '',
    title: '用户 · Travel Map',
    loadComponent: () =>
      import('./pages/user-list/user-list').then((m) => m.UserList),
  },
];
