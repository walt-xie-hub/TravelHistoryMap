import { ChangeDetectionStrategy, Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NotificationService } from '@core/services/notification.service';
import { UserService } from '../../services/user.service';
import { CreateUserDto, UpdateUserDto, User } from '../../models/user.model';

/**
 * 用户新增/编辑弹窗，由列表页在 create / edit 两种模式下复用。
 * 通过 `user` 输入区分模式：为空为新增，否则为编辑并回填。
 */
@Component({
  selector: 'app-user-form-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './user-form-dialog.html',
  styleUrl: './user-form-dialog.scss',
})
export class UserFormDialog implements OnInit {
  readonly user = input<User | null>(null);
  readonly saved = output<User>();
  readonly cancelled = output<void>();

  private readonly userService = inject(UserService);
  private readonly notification = inject(NotificationService);
  private readonly fb = inject(FormBuilder);

  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(50)]],
    email: ['', [Validators.required, Validators.email]],
  });

  ngOnInit(): void {
    const u = this.user();
    if (u) {
      this.form.setValue({ name: u.name, email: u.email });
    }
  }

  get isEdit(): boolean {
    return this.user() !== null;
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.submitting.set(true);

    const editing = this.user();
    const request = editing
      ? this.userService.update(editing.id, value as UpdateUserDto)
      : this.userService.create(value as CreateUserDto);

    request.subscribe({
      next: (result) => {
        this.notification.success(editing ? `用户「${result.name}」已更新` : `用户「${result.name}」已创建`);
        this.saved.emit(result);
        this.submitting.set(false);
      },
      error: () => this.submitting.set(false),
    });
  }

  cancel(): void {
    this.cancelled.emit();
  }
}
