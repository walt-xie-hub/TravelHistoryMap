import { Routes } from '@angular/router';
import { authGuard } from '@core/guards/auth.guard';

/**
 * 顶层路由：
 * - /login、/register 为公开认证页（独立于主布局之外）
 * - 主布局（home / map / profile …）整体受 authGuard 保护，仅登录用户可进入
 */
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('@features/auth/pages/login/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'register',
    loadComponent: () =>
      import('@features/auth/pages/register/register-page').then((m) => m.RegisterPage),
  },
  {
    path: '',
    canActivate: [authGuard],
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
        path: 'profile',
        loadChildren: () =>
          import('@features/profile/profile.routes').then((m) => m.PROFILE_ROUTES),
      },
      {
        path: '**',
        loadComponent: () =>
          import('@features/not-found/not-found').then((m) => m.NotFound),
      },
    ],
  },
];
