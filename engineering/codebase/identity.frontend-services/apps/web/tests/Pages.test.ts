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
/** The sign-in declaration requests the pages made, kept apart so `received` stays the page's own calls. */
const declarationRequests: URL[] = [];

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  declarationRequests.splice(0);
  vi.unstubAllGlobals();
});

type Answer = (body: unknown, url: URL) => Response;

const required = (message: string) => ({ code: 'required', params: null, message });

/** The service's declaration of an instance with sign-up on: password, then a phone code from Uzbekistan. */
const instanceDeclaration = {
  brandName: 'Identity',
  theme: 'platform',
  backgroundUrl: null,
  isSignUpEnabled: true,
  locale: 'en',
  locales: ['uz', 'ru', 'en'],
  methods: [
    {
      id: 'password',
      channels: [],
      fields: [
        { name: 'identifier', kind: 'text', countries: null, rules: [required('Enter your user name or email.')] },
        { name: 'password', kind: 'password', countries: null, rules: [required('Enter your password.')] },
      ],
      passwordRules: [
        {
          code: 'min',
          params: { min: 15, unit: 'characters' },
          message: 'Passwords must be at least 15 characters.',
        },
      ],
    },
    {
      id: 'phone-code',
      channels: ['telegram'],
      fields: [
        { name: 'phone', kind: 'phone', countries: ['UZ'], rules: [required('Enter your phone number.')] },
        { name: 'code', kind: 'pin', countries: null, rules: [required('Enter the code.')] },
      ],
      passwordRules: null,
    },
  ],
  requirements: {
    isConfirmedEmailRequired: false,
    isConfirmedPhoneNumberRequired: false,
    isSecondFactorRequired: false,
  },
};

/**
 * Stubs fetch with one answer per `METHOD /path`; the system status always answers, and the sign-in declaration answers
 * `instanceDeclaration` unless an answer replaces it.
 */
function stubApi(answers: Record<string, Answer> = {}) {
  const received: { method: string; url: URL; body: unknown }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost');
      const method = (init?.method ?? 'GET').toUpperCase();
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
      if (url.pathname === '/api/account/sign-in-options') {
        declarationRequests.push(url);
        const declaration = answers['GET /api/account/sign-in-options'];
        return declaration ? declaration(body, url) : Response.json({ data: instanceDeclaration });
      }
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
  await wrapper.get('input[name="identifier"]').setValue('ann@example.test');
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
    passwordRules: [],
  };
  vi.spyOn(signInOptionsApi, 'get').mockResolvedValue(ResultExtensions.ok({ ...base, ...overrides }));
}

/** The message shown under the input named `name`, found through the input's `aria-describedby`. */
function errorUnder(wrapper: ReturnType<typeof mount>, name: string) {
  const id = wrapper.get(`input[name="${name}"]`).attributes('aria-describedby');
  return id ? wrapper.find(`[id="${id}"]`).text() : null;
}

/** The declaration of `instanceDeclaration` with its password method's rules replaced. */
function declarationWith(passwordRules: unknown) {
  const [password, ...others] = instanceDeclaration.methods;
  return { ...instanceDeclaration, methods: [{ ...password, passwordRules }, ...others] };
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
    const signUp = wrapper.findAll('a').find((link) => link.text() === 'Sign up');
    expect(signUp?.attributes('href')).toBe(`/sign-up?returnUrl=${encodeURIComponent(authorize)}`);
  });

  it('continues to the local return URL once signed in, naming the client', async () => {
    const received = stubApi({ 'POST /api/account/sign-in': noContent });
    const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
    const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    await signInWithPassword(wrapper);

    expect(received.find((request) => request.url.pathname === '/api/account/sign-in')?.url.search).toBe(
      '?useCookies=true&clientId=desk',
    );
    expect(assign).toHaveBeenCalledWith(authorize);
  });

  it('continues to / when the return URL leaves the origin', async () => {
    stubApi({ 'POST /api/account/sign-in': noContent });
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
      'POST /api/account/sign-in': () => answers.shift()!(),
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

  it('shows an HTTPS background, as the settings allow', async () => {
    stubApi();
    withOptions({ backgroundImageUrl: 'https://cdn.example.test/bg.jpg' });
    const { wrapper } = await renderAt('/sign-in');

    expect(wrapper.get('.theme-platform').attributes('style')).toContain('https://cdn.example.test/bg.jpg');
  });

  it.each(['http://cdn.example.test/bg.jpg', '//cdn.example.test/bg.jpg', 'javascript:alert(1)'])(
    'refuses the background %s, neither same-origin nor HTTPS',
    async (backgroundImageUrl) => {
      stubApi();
      withOptions({ backgroundImageUrl });
      const { wrapper } = await renderAt('/sign-in');

      expect(wrapper.get('.theme-platform').attributes('style') ?? '').not.toContain('url(');
    },
  );
});

describe('sign-in declaration', () => {
  it('asks the service for the app the return URL authorizes', async () => {
    stubApi();
    await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    expect(declarationRequests.map((url) => url.search)).toEqual(['?clientId=desk']);
  });

  it("asks for the instance's own declaration on a page with no app", async () => {
    stubApi();
    await renderAt('/sign-in');

    expect(declarationRequests.map((url) => url.search)).toEqual(['']);
  });

  it("lays out the app's look and methods in the declared order, skipping a method it does not know", async () => {
    const [password, phone] = instanceDeclaration.methods;
    stubApi({
      'GET /api/account/sign-in-options': () =>
        Response.json({
          data: {
            ...instanceDeclaration,
            brandName: 'Ocharo',
            theme: 'ocharo',
            backgroundUrl: '/backgrounds/ocharo.jpg',
            isSignUpEnabled: false,
            methods: [{ ...phone, id: 'passkey' }, phone, password],
          },
        }),
    });
    const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    expect(wrapper.findAll('section').map((section) => section.attributes('aria-label'))).toEqual([
      'Phone',
      'Password',
    ]);
    expect(wrapper.get('.theme-ocharo').attributes('style')).toContain('/backgrounds/ocharo.jpg');
    expect(wrapper.text()).toContain('Ocharo');
    expect(wrapper.text()).not.toMatch(/create an account/i);
  });

  it('says the methods could not load while the declaration fails', async () => {
    stubApi({
      'GET /api/account/sign-in-options': () =>
        Response.json(
          { status: 503, detail: 'Unavailable' },
          { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
        ),
    });
    const { wrapper } = await renderAt('/sign-in');

    expect(wrapper.findAll('section')).toHaveLength(0);
    expect(wrapper.text()).toContain('We could not load the ways to sign in.');
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

describe('password rules from the declaration', () => {
  const shortMessage = 'Passwords must be at least 15 characters.';

  it('blocks an email that is not one and a password the declared rules refuse on the sign-up page', async () => {
    const received = stubApi({ 'POST /api/account/sign-up': () => Response.json({ data: { userId: 'u1' } }) });
    const { wrapper } = await renderAt('/sign-up');

    await wrapper.get('input[name="email"]').setValue('sdfsdf');
    await wrapper.get('input[name="password"]').setValue('short');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'email')).toBe('must be a valid email address');
    expect(errorUnder(wrapper, 'password')).toBe(shortMessage);
    expect(received.filter((request) => request.method === 'POST')).toHaveLength(0);

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('a-much-longer-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toEqual({ email: 'ann@example.test', password: 'a-much-longer-password' });
  });

  it('takes the rules and their messages from the declaration it was served, not from a fixed policy', async () => {
    const received = stubApi({
      'GET /api/account/sign-in-options': () =>
        Response.json({
          data: declarationWith([
            {
              code: 'min',
              params: { min: 20, unit: 'characters' },
              message: 'Пароль должен содержать не менее 20 символов.',
            },
            { code: 'creditCard', params: null, message: 'A rule this page cannot map.' },
          ]),
        }),
    });
    const { wrapper } = await renderAt('/sign-up');

    // 18 characters: enough for the instance's 15, short of this declaration's 20.
    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('a-long-password-12');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'password')).toBe('Пароль должен содержать не менее 20 символов.');
    expect(received.filter((request) => request.method === 'POST')).toHaveLength(0);
  });

  it('keeps only the required checks when the declaration lists no password method', async () => {
    const [, phone] = instanceDeclaration.methods;
    const received = stubApi({
      'GET /api/account/sign-in-options': () => Response.json({ data: { ...instanceDeclaration, methods: [phone] } }),
      'POST /api/account/sign-up': () => Response.json({ data: { userId: 'u1' } }),
    });
    const { wrapper } = await renderAt('/sign-up');

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('x');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toEqual({ email: 'ann@example.test', password: 'x' });
  });

  it('keeps only the required checks while the declaration fails to load', async () => {
    const received = stubApi({
      'GET /api/account/sign-in-options': () =>
        Response.json(
          { status: 503, detail: 'Unavailable' },
          { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      'POST /api/account/sign-up': () => Response.json({ data: { userId: 'u1' } }),
    });
    const { wrapper } = await renderAt('/sign-up');

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('x');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toEqual({ email: 'ann@example.test', password: 'x' });
  });

  it('applies them to the new password of a reset link, and rejects the email it names', async () => {
    const received = stubApi({ 'POST /api/account/reset-password': noContent });
    const { wrapper } = await renderAt('/reset-password?email=ann%40example.test&token=r1');

    await wrapper.get('input[name="newPassword"]').setValue('short');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'newPassword')).toBe(shortMessage);
    expect(received.filter((request) => request.method === 'POST')).toHaveLength(0);
  });

  it('applies them to the new password of a change link', async () => {
    const received = stubApi({ 'POST /api/account/password/change': noContent });
    const { wrapper } = await renderAt('/change-password#token=link-token');

    await wrapper.get('input[name="newPassword"]').setValue('short');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'newPassword')).toBe(shortMessage);
    expect(received.filter((request) => request.method === 'POST')).toHaveLength(0);
  });

  it('applies them to the password change a sign-in requires', async () => {
    const received = stubApi({
      'POST /api/account/sign-in': () =>
        Response.json({ data: { isPasswordChangeRequired: true, confirmation: 'None', ticket: 'tk' } }),
      'POST /api/account/manage/password': noContent,
    });
    const { wrapper } = await renderAt('/sign-in');

    await signInWithPassword(wrapper);
    await wrapper.get('input[name="newPassword"]').setValue('short');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'newPassword')).toBe(shortMessage);
    expect(received.map((request) => request.url.pathname)).not.toContain('/api/account/manage/password');
  });

  it('applies them to the password change on the security page', async () => {
    const received = stubApi({
      'GET /api/account/manage/info': () => Response.json({ data: { userId: 'u1', email: 'ann@example.test' } }),
      'POST /api/account/manage/password/confirmation': () =>
        Response.json({ data: { confirmation: 'None', isSent: false } }),
      'POST /api/account/manage/password': noContent,
    });
    const { wrapper } = await renderAt('/account/security');

    const start = wrapper.findAll('button').find((button) => button.text() === 'Change the password');
    await start?.trigger('click');
    await flushPromises();
    await wrapper.get('input[name="currentPassword"]').setValue('old-password');
    await wrapper.get('input[name="newPassword"]').setValue('short');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'newPassword')).toBe(shortMessage);
    expect(received.map((request) => request.url.pathname)).not.toContain('/api/account/manage/password');
  });
});
