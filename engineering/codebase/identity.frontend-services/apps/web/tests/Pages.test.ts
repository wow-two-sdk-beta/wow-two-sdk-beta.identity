import { flushPromises, mount } from '@vue/test-utils';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import { RouterHistoryMode } from '@wow-two-beta/fe/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { hostedNavigation } from '../src/application/account';
import AppRoot from '../src/bootstrap/AppRoot.vue';
import { createWebRouter } from '../src/bootstrap/router/Routes';
import { SignInMethodId, type SignInOptions } from '../src/domain/account';
import { signInOptionsApi } from '../src/integration/account';

const wrappers: ReturnType<typeof mount>[] = [];

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  vi.unstubAllGlobals();
});

type Answer = (body: unknown, url: URL) => Response;

/** Stubs fetch with one answer per `METHOD /path`; the system status always answers. */
function stubApi(answers: Record<string, Answer> = {}) {
  const received: { method: string; url: URL; body: unknown }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost');
      const method = (init?.method ?? 'GET').toUpperCase();
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
      received.push({ method, url, body });
      if (url.pathname === '/api/system/status') {
        return Response.json({ data: { service: 'identity', status: 'ok', version: '0.1.4+abc1234' } });
      }
      const answer = answers[`${method} ${url.pathname}`];
      return answer
        ? answer(body, url)
        : Response.json(
            { status: 404, detail: 'Not found' },
            { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
          );
    }),
  );
  return received;
}

const noContent = () => new Response(null, { status: 204 });
const unauthorized = () =>
  Response.json(
    { status: 401, detail: 'Sign in to continue.' },
    { status: 401, headers: { 'Content-Type': 'application/problem+json' } },
  );

/** Mounts the app at `path`, routed as in the browser but over memory history. */
async function renderAt(path: string) {
  const router = createWebRouter(RouterHistoryMode.Memory);
  await router.push(path);
  await router.isReady();
  const wrapper = mount(AppRoot, { global: { plugins: [router] }, attachTo: document.body });
  wrappers.push(wrapper);
  await flushPromises();
  return { wrapper, router };
}

async function signInWithPassword(wrapper: ReturnType<typeof mount>) {
  await wrapper.get('input[name="login"]').setValue('ann@example.test');
  await wrapper.get('input[name="password"]').setValue('a-long-password');
  await wrapper.get('section[aria-label="Password"] form').trigger('submit');
  await flushPromises();
}

function withOptions(overrides: Partial<SignInOptions>) {
  const base: SignInOptions = {
    brandName: 'Identity',
    theme: 'platform',
    backgroundImageUrl: null,
    isSignUpEnabled: true,
    methods: [{ id: SignInMethodId.Password }],
  };
  vi.spyOn(signInOptionsApi, 'get').mockResolvedValue(ResultExtensions.ok({ ...base, ...overrides }));
}

const authorize = '/connect/authorize?client_id=desk&response_type=code&state=s1';

describe('console route', () => {
  it('shows the running versions and links the hosted pages', async () => {
    stubApi();
    const { wrapper } = await renderAt('/');

    expect(wrapper.get('[aria-label="App version"]').text()).toBe('app test');
    expect(wrapper.get('[aria-label="API version"]').text()).toBe('api 0.1.4+abc1234');
    expect(wrapper.findAll('a').map((link) => link.attributes('href'))).toContain('/sign-in');
  });
});

describe('sign-in route', () => {
  it('lays out password, then phone, and links the sign-up keeping the return URL', async () => {
    stubApi();
    const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    const sections = wrapper.findAll('section').map((section) => section.attributes('aria-label'));
    expect(sections).toEqual(['Password', 'Phone']);
    expect(wrapper.text()).toContain('or');
    expect(wrapper.text()).toContain('+998');
    const signUp = wrapper.findAll('a').find((link) => link.text() === 'Create an account');
    expect(signUp?.attributes('href')).toBe(`/sign-up?returnUrl=${encodeURIComponent(authorize)}`);
  });

  it('continues to the local return URL once signed in, naming the client', async () => {
    const received = stubApi({ 'POST /api/account/login': noContent });
    const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
    const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    await signInWithPassword(wrapper);

    expect(received.find((request) => request.url.pathname === '/api/account/login')?.url.search).toBe(
      '?useCookies=true&clientId=desk',
    );
    expect(assign).toHaveBeenCalledWith(authorize);
  });

  it('continues to / when the return URL leaves the origin', async () => {
    stubApi({ 'POST /api/account/login': noContent });
    const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
    const { wrapper } = await renderAt('/sign-in?returnUrl=%2F%2Fevil.test%2Fphish');

    await signInWithPassword(wrapper);

    expect(assign).toHaveBeenCalledWith('/');
  });

  it('changes a password the rules refuse with the ticket, then signs in anew before continuing', async () => {
    const answers = [
      () => Response.json({ data: { isPasswordChangeRequired: true, confirmation: 'None', ticket: 'tk' } }),
      noContent,
    ];
    const received = stubApi({
      'POST /api/account/login': () => answers.shift()!(),
      'POST /api/account/manage/password': noContent,
    });
    const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
    const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    await signInWithPassword(wrapper);
    expect(assign).not.toHaveBeenCalled();
    expect(wrapper.get('h1').text()).toBe('Change your password');

    expect(wrapper.find('input[name="currentPassword"]').exists()).toBe(false);
    await wrapper.get('input[name="newPassword"]').setValue('a-much-longer-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toEqual({ ticket: 'tk', newPassword: 'a-much-longer-password' });
    expect(assign).not.toHaveBeenCalled();
    expect(wrapper.text()).toContain('Your password is changed. Sign in with the new one.');

    await signInWithPassword(wrapper);

    expect(assign).toHaveBeenCalledWith(authorize);
  });

  it('offers no sign-up while the declaration keeps it closed', async () => {
    stubApi();
    withOptions({ isSignUpEnabled: false });
    const { wrapper } = await renderAt('/sign-in');

    expect(wrapper.text()).not.toMatch(/create an account/i);
  });

  it("shows the app's theme and background behind a readable card", async () => {
    stubApi();
    withOptions({ theme: 'ocharo', backgroundImageUrl: '/backgrounds/ocharo.jpg', brandName: 'Ocharo' });
    const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    const frame = wrapper.get('.theme-ocharo');
    expect(frame.attributes('style')).toContain('/backgrounds/ocharo.jpg');
    expect(wrapper.text()).toContain('Ocharo');
    expect(signInOptionsApi.get).toHaveBeenCalledWith('desk', expect.anything());
  });

  it('refuses a background image from another origin', async () => {
    stubApi();
    withOptions({ backgroundImageUrl: 'https://cdn.example.test/bg.jpg' });
    const { wrapper } = await renderAt('/sign-in');

    expect(wrapper.get('.theme-platform').attributes('style') ?? '').not.toContain('cdn.example.test');
  });
});

describe('password pages', () => {
  it('spends a change link token from the fragment and clears the fragment', async () => {
    const received = stubApi({ 'POST /api/account/password/change': noContent });
    const { wrapper, router } = await renderAt('/change-password#token=link-token');

    expect(router.currentRoute.value.hash).toBe('');
    expect(wrapper.find('input[name="currentPassword"]').exists()).toBe(false);
    await wrapper.get('input[name="newPassword"]').setValue('a-much-longer-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toEqual({ token: 'link-token', newPassword: 'a-much-longer-password' });
    expect(wrapper.text()).toContain('Your password is changed.');
  });

  it('sends a signed-out person from the security page to the sign-in and back', async () => {
    stubApi({ 'GET /api/account/manage/info': unauthorized });
    const { router } = await renderAt('/account/security');
    await flushPromises();

    expect(router.currentRoute.value.path).toBe('/sign-in');
    expect(router.currentRoute.value.query).toEqual({ returnUrl: '/account/security' });
  });

  it('says a link went out instead of showing a form under the Link confirmation', async () => {
    stubApi({
      'GET /api/account/manage/info': () => Response.json({ data: { userId: 'u1', email: 'ann@example.test' } }),
      'POST /api/account/manage/password/confirmation': () =>
        Response.json({ data: { confirmation: 'Link', isSent: true } }),
    });
    const { wrapper } = await renderAt('/account/security');

    expect(wrapper.text()).toContain('ann@example.test');
    const start = wrapper.findAll('button').find((button) => button.text() === 'Change the password');
    await start?.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('We sent you a link to change the password.');
    expect(wrapper.find('input[type="password"]').exists()).toBe(false);
  });

  it('confirms an email from its link', async () => {
    const received = stubApi({ 'GET /api/account/confirm-email': noContent });
    const { wrapper } = await renderAt('/confirm-email?userId=u1&token=t%2B1');

    expect(received.at(-1)?.url.search).toBe('?userId=u1&token=t%2B1');
    expect(wrapper.text()).toContain('Your email is confirmed.');
  });

  it('takes the reset token and email from the link', async () => {
    const received = stubApi({ 'POST /api/account/reset-password': noContent });
    const { wrapper } = await renderAt('/reset-password?email=ann%40example.test&token=r1');

    await wrapper.get('input[name="newPassword"]').setValue('a-much-longer-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toEqual({
      email: 'ann@example.test',
      resetToken: 'r1',
      newPassword: 'a-much-longer-password',
    });
  });

  it('says an incomplete reset link is incomplete', async () => {
    stubApi();
    const { wrapper } = await renderAt('/reset-password?email=ann%40example.test');

    expect(wrapper.text()).toContain('This link is incomplete.');
  });
});
