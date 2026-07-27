import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { PageHeader } from '@shared/components/page-header/page-header';
import { LoadingSpinner } from '@shared/components/loading-spinner/loading-spinner';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { NotificationService } from '@core/services/notification.service';
import { UserService } from '../../services/user.service';
import { User } from '../../models/user.model';
import { UserFormDialog } from '../../components/user-form-dialog/user-form-dialog';
import { UserPagination } from '../../components/user-pagination/user-pagination';

/**
 * 用户管理页：展示用户列表（分页），并支持新增 / 编辑（弹窗）/ 删除。
 * 所有数据通过 UserService 访问后端 user 微服务 `/api/users`。
 */
@Component({
  selector: 'app-user-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, LoadingSpinner, EmptyState, UserFormDialog, UserPagination],
  templateUrl: './user-list.html',
  styleUrl: './user-list.scss',
})
export class UserList {
  private readonly userService = inject(UserService);
  private readonly notification = inject(NotificationService);

  protected readonly users = signal<User[]>([]);
  protected readonly loading = signal(false);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly totalPages = signal(0);
  protected readonly totalCount = signal(0);

  protected readonly dialogOpen = signal(false);
  protected readonly editingUser = signal<User | null>(null);

  protected readonly deletingId = signal<number | null>(null);
  protected readonly removing = signal(false);

  constructor() {
    this.load();
  }

  load(after?: () => void): void {
    this.loading.set(true);
    this.userService.getPaged(this.page(), this.pageSize()).subscribe({
      next: (res) => {
        // 兼容：后端可能仍返回数组（旧 API），或分页对象；缺省字段兜底 []/0，防止 undefined
        const data = Array.isArray(res)
          ? {
              items: res,
              page: 1,
              pageSize: res.length,
              totalCount: res.length,
              totalPages: 1,
            }
          : res;

        this.users.set(data.items ?? []);
        this.totalCount.set(data.totalCount ?? 0);
        this.totalPages.set(data.totalPages ?? 0);
        this.loading.set(false);
        after?.();
      },
      error: () => this.loading.set(false),
    });
  }

  changePage(page: number): void {
    this.page.set(page);
    this.load();
  }

  changePageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.load();
  }

  openCreate(): void {
    this.editingUser.set(null);
    this.dialogOpen.set(true);
  }

  openEdit(user: User): void {
    this.editingUser.set(user);
    this.dialogOpen.set(true);
  }

  closeDialog(): void {
    this.dialogOpen.set(false);
    this.editingUser.set(null);
  }

  onSaved(user: User): void {
    const wasCreate = this.editingUser() === null;
    if (wasCreate) {
      // 新建用户回到首页，确保新纪录可见
      this.page.set(1);
    }
    this.closeDialog();
    this.load();
  }

  askDelete(user: User): void {
    this.deletingId.set(user.id);
  }

  cancelDelete(): void {
    this.deletingId.set(null);
  }

  confirmDelete(user: User): void {
    this.removing.set(true);
    this.userService.delete(user.id).subscribe({
      next: () => {
        this.notification.success(`用户「${user.name}」已删除`);
        this.deletingId.set(null);
        this.removing.set(false);
        // 删除后重新加载；若当前页已空（如删掉了末页最后一条）则回退一页
        this.load(() => {
          if (this.users().length === 0 && this.page() > 1) {
            this.page.update((p) => p - 1);
            this.load();
          }
        });
      },
      error: () => this.removing.set(false),
    });
  }
}
