import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/**
 * 通用分页控件：展示总条数、当前页 / 总页数，支持翻页与设置每页数量。
 * 纯展示组件，翻页与改页大小通过事件上抛，由父组件负责取数。
 */
@Component({
  selector: 'app-user-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './user-pagination.html',
  styleUrl: './user-pagination.scss',
})
export class UserPagination {
  /** 当前页（从 1 开始） */
  page = input.required<number>();
  /** 每页条数 */
  pageSize = input.required<number>();
  /** 总条数 */
  totalCount = input.required<number>();
  /** 总页数 */
  totalPages = input.required<number>();

  /** 翻页事件（target = 目标页，从 1 开始） */
  pageChange = output<number>();
  /** 每页大小变更事件 */
  pageSizeChange = output<number>();

  protected readonly pageSizeOptions = [5, 10, 20, 50];

  /** 当前页是否为首页 / 末页 */
  protected readonly isFirst = computed(() => this.page() <= 1);
  protected readonly isLast = computed(() => this.page() >= this.totalPages());

  /** 需要渲染的页码窗口（当前页前后各 2 页，不足则向两端补齐） */
  protected readonly pages = computed<number[]>(() => {
    const total = this.totalPages();
    const current = this.page();
    if (total <= 1) {
      return total === 0 ? [] : [1];
    }

    const span = 2;
    let start = Math.max(1, current - span);
    let end = Math.min(total, current + span);

    if (end - start < span * 2) {
      if (start === 1) {
        end = Math.min(total, start + span * 2);
      } else if (end === total) {
        start = Math.max(1, end - span * 2);
      }
    }

    const result: number[] = [];
    for (let i = start; i <= end; i++) {
      result.push(i);
    }
    return result;
  });

  protected onPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.page()) {
      return;
    }
    this.pageChange.emit(page);
  }

  protected onPageSizeChange(event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value);
    if (!Number.isNaN(value)) {
      this.pageSizeChange.emit(value);
    }
  }
}
