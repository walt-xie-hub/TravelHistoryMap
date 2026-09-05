import { Routes } from '@angular/router';

/** 我的资料：修改个人资料与登录密码（受主布局 authGuard 保护）。 */
export const PROFILE_ROUTES: Routes = [
  {
    path: '',
    title: '我的资料',
    loadComponent: () =>
      import('./pages/profile-page/profile-page').then((m) => m.ProfilePage),
  },
];
