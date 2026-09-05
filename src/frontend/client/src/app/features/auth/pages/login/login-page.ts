import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@core/services/auth.service';
import { ConfigService } from '@core/services/config.service';
import { extractErrorMessage } from '@core/utils/error-message.util';

/** 登录页（公开路由，独立于主布局）。 */
@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './login-page.html',
  styleUrl: '../auth-form.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly config = inject(ConfigService);

  protected readonly appName = this.config.appName;
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal('');

  protected onEmailChange(event: Event): void {
    this.email.set((event.target as HTMLInputElement).value);
  }

  protected onPasswordChange(event: Event): void {
    this.password.set((event.target as HTMLInputElement).value);
  }

  protected async submit(): Promise<void> {
    const email = this.email().trim();
    const password = this.password();
    if (!email || !password) {
      this.errorMessage.set('请输入邮箱和密码。');
      return;
    }
    this.errorMessage.set('');
    this.submitting.set(true);
    try {
      await this.auth.login({ email, password });
      await this.router.navigate(['/home']);
    } catch (error) {
      this.errorMessage.set(
        error instanceof HttpErrorResponse && error.status === 401
          ? '邮箱或密码不正确，请重试。'
          : extractErrorMessage(error),
      );
    } finally {
      this.submitting.set(false);
    }
  }
}
