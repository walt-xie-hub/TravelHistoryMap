import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '@core/services/auth.service';
import { NotificationService } from '@core/services/notification.service';

const MIN_PASSWORD_LENGTH = 8;

/**
 * 我的资料页：
 * - 基本信息（姓名 / 邮箱 / 电话 / 头像 URL）→ PUT /users/me
 * - 登录密码 → PUT /users/me/password
 * 页面位于主布局内，错误提示由全局通知（toast）承载。
 */
@Component({
  selector: 'app-profile-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './profile-page.html',
  styleUrl: './profile-page.scss',
})
export class ProfilePage {
  private readonly auth = inject(AuthService);
  private readonly notification = inject(NotificationService);

  protected readonly currentUser = this.auth.currentUser;

  // 基本信息表单（以当前用户资料作为初始值）
  protected readonly name = signal('');
  protected readonly email = signal('');
  protected readonly phoneNumber = signal('');
  protected readonly avatarUrl = signal('');
  protected readonly savingProfile = signal(false);

  // 修改密码表单
  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmPassword = signal('');
  protected readonly savingPassword = signal(false);

  protected readonly avatarPreview = computed(() => {
    const url = this.avatarUrl().trim();
    return url ? { url } : null;
  });
  protected readonly avatarInitial = computed(() =>
    (this.name().trim().charAt(0) || this.currentUser()?.name?.charAt(0) || '?').toUpperCase(),
  );

  constructor() {
    const user = this.auth.currentUser();
    this.name.set(user?.name ?? '');
    this.email.set(user?.email ?? '');
    this.phoneNumber.set(user?.phoneNumber ?? '');
    this.avatarUrl.set(user?.avatarUrl ?? '');
  }

  protected onNameChange(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  protected onEmailChange(event: Event): void {
    this.email.set((event.target as HTMLInputElement).value);
  }

  protected onPhoneChange(event: Event): void {
    this.phoneNumber.set((event.target as HTMLInputElement).value);
  }

  protected onAvatarUrlChange(event: Event): void {
    this.avatarUrl.set((event.target as HTMLInputElement).value);
  }

  protected onCurrentPasswordChange(event: Event): void {
    this.currentPassword.set((event.target as HTMLInputElement).value);
  }

  protected onNewPasswordChange(event: Event): void {
    this.newPassword.set((event.target as HTMLInputElement).value);
  }

  protected onConfirmPasswordChange(event: Event): void {
    this.confirmPassword.set((event.target as HTMLInputElement).value);
  }

  protected async saveProfile(): Promise<void> {
    const name = this.name().trim();
    const email = this.email().trim();
    if (!name || !email) {
      this.notification.error('姓名和邮箱不能为空。');
      return;
    }
    this.savingProfile.set(true);
    try {
      await this.auth.updateProfile({
        name,
        email,
        phoneNumber: this.phoneNumber().trim() || null,
        avatarUrl: this.avatarUrl().trim() || null,
      });
      this.notification.success('个人资料已更新。');
    } finally {
      this.savingProfile.set(false);
    }
  }

  protected async savePassword(): Promise<void> {
    const current = this.currentPassword();
    const next = this.newPassword();
    if (!current || !next) {
      this.notification.error('请填写当前密码与新密码。');
      return;
    }
    if (next.length < MIN_PASSWORD_LENGTH) {
      this.notification.error(`新密码至少需要 ${MIN_PASSWORD_LENGTH} 位字符。`);
      return;
    }
    if (next !== this.confirmPassword()) {
      this.notification.error('两次输入的新密码不一致。');
      return;
    }
    this.savingPassword.set(true);
    try {
      await this.auth.changePassword(current, next);
      this.currentPassword.set('');
      this.newPassword.set('');
      this.confirmPassword.set('');
      this.notification.success('密码已更新，下次登录请使用新密码。');
    } finally {
      this.savingPassword.set(false);
    }
  }
}
