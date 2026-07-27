import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ConfigService } from '@core/services/config.service';
import { NotificationService } from '@core/services/notification.service';

interface NavItem {
  path: string;
  label: string;
  icon: string;
}

/**
 * 主布局：顶部导航 + 内容区（router-outlet）+ 全局通知。
 * 作为所有业务页面的外壳（shell）。
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
  protected readonly notification = inject(NotificationService);

  protected readonly appName = this.config.appName;
  protected readonly year = new Date().getFullYear();

  protected readonly navItems: NavItem[] = [
    { path: '/home', label: '首页', icon: '🏠' },
    { path: '/map', label: '地图', icon: '🗺️' },
    { path: '/users', label: '用户', icon: '👤' },
  ];
}
