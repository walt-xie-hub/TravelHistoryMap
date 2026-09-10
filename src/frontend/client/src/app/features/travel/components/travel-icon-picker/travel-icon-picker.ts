import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import type { TravelIcon, TravelIconCategory } from '../../utils/travel-icon.util';
import {
  TRAVEL_ICONS,
  iconFile,
  regionalCitiesOf,
  regionalIconKey,
  regionalIconKeys,
  travelIcon,
} from '../../utils/travel-icon.util';

type CategoryFilter = 'region' | TravelIconCategory;

interface GridIcon {
  key: string;
  label: string;
  file: string;
  /** 地区 tab 专用：关联城市名（展示「城市 → 图标」关联） */
  cities?: readonly string[];
}

/**
 * 旅行标识图标选择器（ADR-0016）：分类 chips + 图标网格 + 自动（按城市）提示。
 * 纯受控组件：`value` 为显式选择的 key（null = 自动/不设置），变更通过 `valueChange` 输出。
 */
@Component({
  selector: 'app-travel-icon-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './travel-icon-picker.html',
  styleUrl: './travel-icon-picker.scss',
})
export class TravelIconPicker {
  /** 显式选择的图标 key；null = 未选择（按城市自动） */
  readonly value = input<string | null>(null);
  /** 记录的城市快照（用于「自动将显示…」提示） */
  readonly city = input<string | null>(null);
  readonly valueChange = output<string | null>();

  protected readonly activeCategory = signal<CategoryFilter>('region');

  protected readonly categories: readonly { id: CategoryFilter; label: string }[] = [
    { id: 'region', label: '地区特色' },
    { id: 'animal', label: '动物' },
    { id: 'food', label: '美食' },
    { id: 'architecture', label: '建筑' },
    { id: 'plant', label: '植物' },
  ];

  /** 显式选择的图标（null / 库内不存在时 undefined） */
  protected readonly selected = computed<TravelIcon | undefined>(() => travelIcon(this.value()));

  /** 已选了值但图标库中不存在（历史数据 / 条目被删）：提示而不是静默显示成「未选择」 */
  protected readonly unknownSelection = computed(() => !!this.value() && !this.selected());

  /** 未显式选择时，按城市自动命中的地区特色图标 */
  protected readonly suggested = computed(() => travelIcon(regionalIconKey(this.city())));

  protected readonly icons = computed<GridIcon[]>(() => {
    const category = this.activeCategory();
    if (category === 'region') {
      return regionalIconKeys()
        .map((key) => travelIcon(key))
        .filter((icon): icon is TravelIcon => icon !== undefined)
        .map((icon) => ({
          key: icon.key,
          label: icon.label,
          file: iconFile(icon),
          cities: regionalCitiesOf(icon.key),
        }));
    }
    return TRAVEL_ICONS.filter((icon) => icon.category === category).map((icon) => ({
      key: icon.key,
      label: icon.label,
      file: iconFile(icon),
    }));
  });

  protected selectCategory(id: CategoryFilter): void {
    this.activeCategory.set(id);
  }

  protected isActive(id: CategoryFilter): boolean {
    return this.activeCategory() === id;
  }

  protected isSelected(key: string): boolean {
    return this.value() === key;
  }

  protected pick(key: string): void {
    this.valueChange.emit(key);
  }

  protected clear(): void {
    this.valueChange.emit(null);
  }
}
