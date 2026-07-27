import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PageHeader } from '@shared/components/page-header/page-header';

interface FeatureCard {
  icon: string;
  title: string;
  desc: string;
  link: string;
}

@Component({
  selector: 'app-home-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader],
  templateUrl: './home-page.html',
  styleUrl: './home-page.scss',
})
export class HomePage {
  protected readonly cards: FeatureCard[] = [
    { icon: '🗺️', title: '地图', desc: '在地图上探索与标记你的旅行足迹。', link: '/map' },
    { icon: '👤', title: '用户', desc: '管理旅行者账号与资料。', link: '/users' },
  ];
}
