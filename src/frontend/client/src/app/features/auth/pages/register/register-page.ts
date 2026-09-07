import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@core/services/auth.service';
import { ConfigService } from '@core/services/config.service';
import { extractErrorMessage } from '@core/utils/error-message.util';

const MIN_PASSWORD_LENGTH = 8;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** 注册成功后跳转登录页前的停留时长（让用户看清成功提示） */
const REDIRECT_DELAY_MS = 1500;

/**
 * 注册页（公开路由，独立于主布局）。
 * - 字段输入即实时校验，在对应控件下方即时显示 hint（红字），无需等提交
 * - 点击“注册”后先就地提示结果：失败显示红框错误，成功显示绿框成功提示
 * - 注册成功将用户写入数据库，短暂停留后跳转登录页，由用户重新登录
 */
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
  protected readonly successMessage = signal('');

  // 是否已开始填写/失焦（避免表单一打开就满屏“必填”提示）
  private readonly nameTouched = signal(false);
  private readonly emailTouched = signal(false);
  private readonly passwordTouched = signal(false);
  private readonly confirmPasswordTouched = signal(false);
  protected readonly hints = computed(() => ({
    name: this.nameTouched() ? this.validateName(this.name()) : null,
    email: this.emailTouched() ? this.validateEmail(this.email()) : null,
    password: this.passwordTouched() ? this.validatePassword(this.password()) : null,
    confirmPassword: this.confirmPasswordTouched()
      ? this.validateConfirmPassword(this.confirmPassword())
      : null,
  }));

  protected onNameChange(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.name.set(value);
    if (value.trim()) this.nameTouched.set(true);
  }

  protected onEmailChange(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.email.set(value);
    if (value.trim()) this.emailTouched.set(true);
  }

  protected onPasswordChange(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.password.set(value);
    if (value) this.passwordTouched.set(true);
  }

  protected onConfirmPasswordChange(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.confirmPassword.set(value);
    if (value) this.confirmPasswordTouched.set(true);
  }

  protected onNameBlur(): void {
    this.nameTouched.set(true);
  }

  protected onEmailBlur(): void {
    this.emailTouched.set(true);
  }

  protected onPasswordBlur(): void {
    this.passwordTouched.set(true);
  }

  protected onConfirmPasswordBlur(): void {
    this.confirmPasswordTouched.set(true);
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    // 已注册成功、正在跳转中，忽略重复点击
    if (this.successMessage()) return;

    // 提交时即使空字段也展示全部必填提示
    this.nameTouched.set(true);
    this.emailTouched.set(true);
    this.passwordTouched.set(true);
    this.confirmPasswordTouched.set(true);

    const { name, email, password, confirmPassword } = this.hints();
    if (name || email || password || confirmPassword) {
      this.errorMessage.set('');
      return; // hint 已就地展示，不发请求
    }

    this.errorMessage.set('');
    this.submitting.set(true);
    try {
      await this.auth.register({
        name: this.name().trim(),
        email: this.email().trim(),
        password: this.password(),
      });
      // 注册成功：后端已写入数据库 → 先就地提示成功，稍后再跳转登录页重新登录
      this.successMessage.set('注册成功！正在跳转到登录页…');
      window.setTimeout(() => {
        void this.router.navigate(['/login'], { queryParams: { registered: '1' } });
      }, REDIRECT_DELAY_MS);
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

  private validateName(value: string): string | null {
    return value.trim() ? null : '请输入姓名。';
  }

  private validateEmail(value: string): string | null {
    const trimmed = value.trim();
    if (!trimmed) return '请输入邮箱。';
    return EMAIL_PATTERN.test(trimmed) ? null : '邮箱格式不正确，如 you@example.com。';
  }

  private validatePassword(value: string): string | null {
    if (!value) return '请输入密码。';
    return value.length >= MIN_PASSWORD_LENGTH
      ? null
      : `密码至少 ${MIN_PASSWORD_LENGTH} 位字符。`;
  }

  private validateConfirmPassword(value: string): string | null {
    if (!value) return '请再次输入密码。';
    return value === this.password() ? null : '两次输入的密码不一致。';
  }
}
