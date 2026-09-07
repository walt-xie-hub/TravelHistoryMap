import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@core/services/auth.service';
import { ConfigService } from '@core/services/config.service';
import { extractErrorMessage } from '@core/utils/error-message.util';

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * 登录页（公开路由，独立于主布局）。
 * - 图片验证码：进入页面即加载，点击图片可刷新；一次性凭证，随登录提交
 * - 邮箱/密码/验证码均有字段级 hint 校验
 * - 支持从注册页跳转（?registered=1）时展示“注册成功，请登录”提示
 */
@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './login-page.html',
  styleUrl: '../auth-form.scss',
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly config = inject(ConfigService);

  protected readonly appName = this.config.appName;
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly successMessage = signal('');

  // 图片验证码
  protected readonly captchaId = signal('');
  protected readonly captchaImage = signal('');
  protected readonly captcha = signal('');
  protected readonly captchaLoading = signal(false);
  private readonly captchaTouched = signal(false);
  protected readonly captchaHint = computed(() =>
    this.captchaTouched() && !this.captcha().trim() ? '请输入图片上的 4 位数字。' : null,
  );

  private readonly emailTouched = signal(false);
  private readonly passwordTouched = signal(false);
  protected readonly emailHint = computed(() => {
    if (!this.emailTouched()) return null;
    const value = this.email().trim();
    if (!value) return '请输入邮箱。';
    return EMAIL_PATTERN.test(value) ? null : '邮箱格式不正确，如 you@example.com。';
  });
  protected readonly passwordHint = computed(() => {
    if (!this.passwordTouched()) return null;
    return this.password() ? null : '请输入密码。';
  });

  ngOnInit(): void {
    if (this.route.snapshot.queryParamMap.get('registered') === '1') {
      this.successMessage.set('注册成功，请使用新账号登录。');
    }
    void this.loadCaptcha();
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

  protected onCaptchaChange(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.captcha.set(value);
    this.captchaTouched.set(true);
  }

  protected onEmailBlur(): void {
    this.emailTouched.set(true);
  }

  protected onPasswordBlur(): void {
    this.passwordTouched.set(true);
  }

  protected onCaptchaBlur(): void {
    this.captchaTouched.set(true);
  }

  /** 加载新验证码（进入页面自动调用，点击图片/按钮可刷新）。 */
  protected async loadCaptcha(): Promise<void> {
    if (this.captchaLoading()) return;
    this.captchaLoading.set(true);
    this.captchaTouched.set(false);
    try {
      const captcha = await this.auth.getCaptcha();
      this.captchaId.set(captcha.captchaId);
      this.captchaImage.set(captcha.captchaImage);
      this.captcha.set('');
    } catch {
      this.captchaImage.set('');
      this.errorMessage.set('验证码加载失败，请稍后重试。');
    } finally {
      this.captchaLoading.set(false);
    }
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.emailTouched.set(true);
    this.passwordTouched.set(true);
    this.captchaTouched.set(true);

    if (this.emailHint() || this.passwordHint() || this.captchaHint()) {
      this.errorMessage.set('');
      return; // 字段 hint 就地展示，不发请求
    }

    this.errorMessage.set('');
    this.submitting.set(true);
    try {
      await this.auth.login({
        email: this.email().trim(),
        password: this.password(),
        captchaId: this.captchaId(),
        captchaAnswer: this.captcha().trim(),
      });
      await this.router.navigate(['/home']);
    } catch (error) {
      // 401 账密错误 / 400 验证码错误或过期：验证码均为一次性，已被后端消耗，需刷新
      if (error instanceof HttpErrorResponse) {
        if (error.status === 401) {
          this.errorMessage.set('邮箱或密码不正确，请重试。');
        } else if (error.status === 400) {
          this.errorMessage.set(extractErrorMessage(error));
        } else {
          this.errorMessage.set(extractErrorMessage(error));
        }
        if (error.status === 400 || error.status === 401) {
          void this.loadCaptcha();
        }
      } else {
        this.errorMessage.set(extractErrorMessage(error));
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
