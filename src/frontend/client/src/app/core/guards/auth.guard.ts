import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '@core/services/auth.service';

/**
 * 认证守卫：仅登录用户可进入主界面。
 * 有本地会话时先尝试恢复用户资料，失败（token 失效）会清理会话并回到登录页。
 */
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const authenticated = await auth.initialize();
  return authenticated ? true : router.createUrlTree(['/login']);
};
