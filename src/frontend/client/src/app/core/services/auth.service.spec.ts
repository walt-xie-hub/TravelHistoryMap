import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

const TOKEN_KEY = 'travel-map.auth.token';
const REFRESH_TOKEN_KEY = 'travel-map.auth.refresh-token';

/**
 * 会话恢复的失败语义。
 *
 * 这组断言的来由是一个真实现象：user-service 没起来时，浏览器里
 * `login → users/me 失败 → 再 login → users/me 失败`，用户被反复要求重新登录
 * （每次都还得重读一次验证码）。根因是恢复会话的失败处理把**任何**错误都当成
 * “令牌失效”，于是后端一次抖动就丢掉了仍然有效的 refresh token。
 */
describe('AuthService.initialize', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    localStorage.clear();
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  function givenStoredSession(): void {
    localStorage.setItem(TOKEN_KEY, 'access-token');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-token');
  }

  it('后端暂时不可用（连不上）时保留会话，不把用户登出', async () => {
    givenStoredSession();

    const initialized = auth.initialize();
    http.expectOne('users/me').error(new ProgressEvent('error'));

    await expect(initialized).resolves.toBe(false);
    expect(localStorage.getItem(TOKEN_KEY)).toBe('access-token');
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-token');
  });

  it('网关 5xx 同样保留会话', async () => {
    givenStoredSession();

    const initialized = auth.initialize();
    http.expectOne('users/me').flush(
      { message: 'boom' },
      { status: 503, statusText: 'Service Unavailable' },
    );

    await expect(initialized).resolves.toBe(false);
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-token');
  });

  it('401 才销毁会话', async () => {
    givenStoredSession();

    const initialized = auth.initialize();
    http.expectOne('users/me').flush(
      { message: 'nope' },
      { status: 401, statusText: 'Unauthorized' },
    );

    await expect(initialized).resolves.toBe(false);
    expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('暂时性故障不会被永久记忆为“未登录”，后端恢复后能重试成功', async () => {
    givenStoredSession();

    const first = auth.initialize();
    http.expectOne('users/me').error(new ProgressEvent('error'));
    await expect(first).resolves.toBe(false);

    const second = auth.initialize();
    http
      .expectOne('users/me')
      .flush({ id: 100008, name: 'Demo User', email: 'demo@travel.local' });

    await expect(second).resolves.toBe(true);
    expect(auth.currentUser()?.name).toBe('Demo User');
  });
});
