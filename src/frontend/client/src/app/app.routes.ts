import { Routes } from '@angular/router';

/**
 * 顶层路由：以主布局为外壳，各特性模块按需懒加载。
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('@layout/main-layout/main-layout').then((m) => m.MainLayout),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'home' },
      {
        path: 'home',
        loadChildren: () =>
          import('@features/home/home.routes').then((m) => m.HOME_ROUTES),
      },
      {
        path: 'map',
        loadChildren: () =>
          import('@features/map/map.routes').then((m) => m.MAP_ROUTES),
      },
      {
        path: 'users',
        loadChildren: () =>
          import('@features/users/users.routes').then((m) => m.USERS_ROUTES),
      },
      {
        path: '**',
        loadComponent: () =>
          import('@features/not-found/not-found').then((m) => m.NotFound),
      },
    ],
  },
];
