import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@core/services/auth.service';
import { ConfigService } from '@core/services/config.service';
import { extractErrorMessage } from '@core/utils/error-message.util';

const MIN_PASSWORD_LENGTH = 8;

/** 注册页（公开路由，独立于主布局）：注册成功后自动登录并进入主界面。 */
@Component({
  selector: 'app-register-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './register-page.html',
  styleUrl: '../auth-form.scss',
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly config = inject(ConfigService);

  protected readonly appName = this.config.appName;
  protected readonly name = signal('');
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly confirmPassword = signal('');
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal('');

  protected onNameChange(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  protected onEmailChange(event: Event): void {
    this.email.set((event.target as HTMLInputElement).value);
  }

  protected onPasswordChange(event: Event): void {
    this.password.set((event.target as HTMLInputElement).value);
  }

  protected onConfirmPasswordChange(event: Event): void {
    this.confirmPassword.set((event.target as HTMLInputElement).value);
  }

  protected async submit(): Promise<void> {
    const name = this.name().trim();
    const email = this.email().trim();
    const password = this.password();

    if (!name || !email || !password) {
      this.errorMessage.set('请完整填写姓名、邮箱和密码。');
      return;
    }
    if (password.length < MIN_PASSWORD_LENGTH) {
      this.errorMessage.set(`密码至少需要 ${MIN_PASSWORD_LENGTH} 位字符。`);
      return;
    }
    if (password !== this.confirmPassword()) {
      this.errorMessage.set('两次输入的密码不一致。');
      return;
    }

    this.errorMessage.set('');
    this.submitting.set(true);
    try {
      // 注册成功即自动登录（后端直接签发 token）
      await this.auth.register({ name, email, password });
      await this.router.navigate(['/home']);
    } catch (error) {
      this.errorMessage.set(
        error instanceof HttpErrorResponse && error.status === 409
          ? '该邮箱已被注册，请直接登录或更换邮箱。'
          : extractErrorMessage(error),
      );
    } finally {
      this.submitting.set(false);
    }
  }
}
