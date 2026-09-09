import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '@core/services/auth.service';
import { ConfigService } from '@core/services/config.service';
import { NotificationService } from '@core/services/notification.service';

interface NavItem {
  path: string;
  label: string;
  icon: string;
}

/**
 * 主布局：顶部导航（含右上角用户菜单）+ 内容区（router-outlet）+ 全局通知。
 * 作为所有业务页面的外壳（shell），整体受 authGuard 保护。
 */
@Component({
  selector: 'app-main-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss',
})
export class MainLayout {
  private readonly config = inject(ConfigService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  protected readonly notification = inject(NotificationService);

  protected readonly appName = this.config.appName;
  protected readonly year = new Date().getFullYear();

  protected readonly navItems: NavItem[] = [
    { path: '/home', label: '首页', icon: '🏠' },
    { path: '/map', label: '地图', icon: '🗺️' },
    { path: '/photos', label: '照片墙', icon: '🖼️' },
  ];

  /** 当前登录用户（无头像时显示姓名首字母占位图）。 */
  protected readonly user = this.auth.currentUser;
  protected readonly userMenuOpen = signal(false);
  protected readonly userAvatarInitial = computed(() =>
    (this.user()?.name?.trim()?.charAt(0) ?? '?').toUpperCase(),
  );

  protected toggleUserMenu(): void {
    this.userMenuOpen.update((open) => !open);
  }

  protected closeUserMenu(): void {
    this.userMenuOpen.set(false);
  }

  protected openProfile(): void {
    this.userMenuOpen.set(false);
    void this.router.navigate(['/profile']);
  }

  protected logout(): void {
    this.userMenuOpen.set(false);
    this.auth.logout();
    void this.router.navigate(['/login']);
  }
}
