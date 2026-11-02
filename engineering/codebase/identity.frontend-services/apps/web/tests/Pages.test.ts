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
  signUpMode: 'open',
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
    signUpMode: 'open',
    isPersistenceOffered: false,
    isEmailEnabled: true,
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

const consoleAdmin = () => Response.json({ data: { userId: 'u1', name: 'Lola Karimova', roles: ['admin'] } });

describe('console route', () => {
  it('shows the signed-in admin, the running versions and links the hosted pages', async () => {
    stubApi({ 'GET /api/console/session': consoleAdmin });
    const { wrapper } = await renderAt('/');

    expect(wrapper.get('[aria-label="App version"]').text()).toBe('app test');
    expect(wrapper.get('[aria-label="API version"]').text()).toBe('api 0.1.4+abc1234');
    expect(wrapper.get('[aria-label="Signed in"]').text()).toBe('Signed in as Lola Karimova · admin');
    expect(wrapper.findAll('a').map((link) => link.attributes('href'))).toContain('/sign-in');
  });

  it('sends a signed-out visitor to sign in, coming back to the console', async () => {
    stubApi({ 'GET /api/console/session': unauthorized });
    const { wrapper, router } = await renderAt('/');

    // The sign-in page returns to the console by default.
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/sign-in'));
    await flushPromises();
    expect(wrapper.findAll('h1').map((heading) => heading.text())).toEqual(['Sign in']);
  });

  it('tells a person without an instance role and shows nothing else', async () => {
    stubApi({
      'GET /api/console/session': () =>
        Response.json({ status: 403 }, { status: 403, headers: { 'Content-Type': 'application/problem+json' } }),
    });
    const { wrapper } = await renderAt('/');

    expect(wrapper.text()).toContain('This account has no console access.');
    expect(wrapper.find('[aria-labelledby="hosted-heading"]').exists()).toBe(false);
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

  it('resends the confirmation of an unconfirmed email with the return URL, so its link comes back here', async () => {
    const received = stubApi({
      'POST /api/account/sign-in': () =>
        Response.json(
          { status: 403, detail: 'Confirm your email to sign in.', identityCode: 'EmailConfirmationRequired' },
          { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      'POST /api/account/resend-confirmation-email': noContent,
    });
    const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    await signInWithPassword(wrapper);
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Send the confirmation email again')
      ?.trigger('click');
    await flushPromises();

    expect(received.at(-1)?.url.pathname).toBe('/api/account/resend-confirmation-email');
    expect(received.at(-1)?.body).toEqual({ email: 'ann@example.test', returnUrl: authorize });
  });

  it('checks the phone code against the length the declaration gives it before sending', async () => {
    const codeLength = { code: 'length', params: { length: 6, unit: 'characters' }, message: 'The code has 6 digits.' };
    const received = stubApi({
      'GET /api/account/sign-in-options': () =>
        Response.json({
          data: {
            ...instanceDeclaration,
            methods: instanceDeclaration.methods.map((method) => ({
              ...method,
              fields: method.fields.map((field) =>
                field.name === 'code' ? { ...field, rules: [...field.rules, codeLength] } : field,
              ),
            })),
          },
        }),
      'POST /api/account/phone-sign-in/send': () =>
        Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } }),
    });
    const { wrapper } = await renderAt('/sign-in');

    await wrapper.get('section[aria-label="Phone"] input[type="tel"]').setValue('901234567');
    await wrapper.get('section[aria-label="Phone"] form').trigger('submit');
    await flushPromises();
    await wrapper.get('input[name="code"]').setValue('12345');
    await wrapper.get('section[aria-label="Phone"] form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'code')).toBe('The code has 6 digits.');
    expect(received.map((request) => request.url.pathname)).not.toContain('/api/account/phone-sign-in');
  });

  it('offers "Keep me signed in" only where the declaration does', async () => {
    stubApi();
    withOptions({ isPersistenceOffered: true });
    const offered = await renderAt('/sign-in');
    expect(offered.wrapper.text()).toContain('Keep me signed in');

    withOptions({ isPersistenceOffered: false });
    const withheld = await renderAt('/sign-in');
    expect(withheld.wrapper.text()).not.toContain('Keep me signed in');
  });

  it('offers no sign-up while the declaration keeps it closed', async () => {
    stubApi();
    withOptions({ signUpMode: 'invite' });
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
            signUpMode: 'invite',
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

describe('hosted layout', () => {
  const returnUrl = encodeURIComponent(authorize);

  /** Records whether the layout left a click to the browser, then cancels it so happy-dom does not navigate. */
  function watchClicks() {
    const clicks: { isHandled: boolean }[] = [];
    const record = (event: Event) => {
      clicks.push({ isHandled: event.defaultPrevented });
      event.preventDefault();
    };
    document.addEventListener('click', record);
    return { clicks, stop: () => document.removeEventListener('click', record) };
  }

  it('keeps one card, one frame and one declaration request while sign-in gives way to sign-up', async () => {
    stubApi();
    const { wrapper, router } = await renderAt(`/sign-in?returnUrl=${returnUrl}`);
    const card = wrapper.get('.hosted-card').element;
    const frame = wrapper.get('.theme-platform').element;
    expect(wrapper.get('h1').text()).toBe('Sign in');

    await router.push(`/sign-up?returnUrl=${returnUrl}`);
    await flushPromises();

    expect(wrapper.get('h1').text()).toBe('Sign up');
    expect(wrapper.find('input[name="email"]').exists()).toBe(true);
    expect(wrapper.find('input[name="identifier"]').exists()).toBe(false);
    expect(wrapper.get('.hosted-card').element).toBe(card);
    expect(wrapper.get('.theme-platform').element).toBe(frame);
    expect(wrapper.findAll('main')).toHaveLength(1);
    expect(declarationRequests.map((url) => url.search)).toEqual(['?clientId=desk']);
  });

  it('opens the page a plain link names in the same card, keeping the return URL', async () => {
    stubApi();
    const { wrapper, router } = await renderAt(`/sign-in?returnUrl=${returnUrl}`);
    const card = wrapper.get('.hosted-card').element;
    const { clicks, stop } = watchClicks();

    await wrapper
      .findAll('a')
      .find((link) => link.text() === 'Forgot your password?')
      ?.trigger('click');
    stop();

    expect(clicks).toEqual([{ isHandled: true }]);
    // The router loads the page's chunk before it navigates.
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe(`/forgot-password?returnUrl=${returnUrl}`));
    await flushPromises();
    expect(wrapper.get('h1').text()).toBe('Reset your password');
    expect(wrapper.get('.hosted-card').element).toBe(card);
  });

  it('leaves a modified click and a new-tab link to the browser', async () => {
    stubApi();
    const { wrapper, router } = await renderAt('/sign-in');
    const signUp = wrapper.findAll('a').find((link) => link.text() === 'Sign up');
    const { clicks, stop } = watchClicks();

    await signUp?.trigger('click', { ctrlKey: true });
    signUp?.element.setAttribute('target', '_blank');
    await signUp?.trigger('click');
    stop();

    expect(clicks).toEqual([{ isHandled: false }, { isHandled: false }]);
    expect(router.currentRoute.value.path).toBe('/sign-in');
  });

  it('shows no heading or footer of the page that left', async () => {
    stubApi();
    const { wrapper, router } = await renderAt('/sign-in');

    await router.push('/forgot-password');
    await flushPromises();

    expect(wrapper.findAll('h1').map((heading) => heading.text())).toEqual(['Reset your password']);
    expect(wrapper.findAll('footer')).toHaveLength(1);
  });

  it('serves the console without the card', async () => {
    stubApi({ 'GET /api/console/session': consoleAdmin });
    const { wrapper } = await renderAt('/');

    expect(wrapper.find('.hosted-card').exists()).toBe(false);
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

  it('offers no password change on a phone-only instance', async () => {
    const [, phone] = instanceDeclaration.methods;
    stubApi({
      'GET /api/account/sign-in-options': () =>
        Response.json({ data: { ...instanceDeclaration, isEmailEnabled: false, methods: [phone] } }),
      'GET /api/account/manage/info': () => Response.json({ data: { userId: 'u1', userName: '+998901234567' } }),
    });
    const { wrapper } = await renderAt('/account/security');

    expect(wrapper.text()).toContain('+998901234567');
    expect(wrapper.find('[aria-labelledby="password-heading"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('there is no password to change');
  });

  it('confirms an email from its link', async () => {
    const received = stubApi({ 'GET /api/account/confirm-email': noContent });
    const { wrapper } = await renderAt('/confirm-email?userId=u1&token=t%2B1');

    expect(received.at(-1)?.url.search).toBe('?userId=u1&token=t%2B1');
    expect(wrapper.text()).toContain('Your email is confirmed.');
    expect(
      wrapper
        .findAll('a')
        .find((link) => link.text() === 'Sign in')
        ?.attributes('href'),
    ).toBe('/sign-in');
  });

  it("signs in from a confirmed email toward the link's return URL, not the console", async () => {
    const received = stubApi({ 'GET /api/account/confirm-email': noContent });
    const { wrapper } = await renderAt(`/confirm-email?userId=u1&token=t1&returnUrl=${encodeURIComponent(authorize)}`);

    expect(received.at(-1)?.url.search).toBe(`?userId=u1&token=t1`);
    expect(
      wrapper
        .findAll('a')
        .find((link) => link.text() === 'Sign in')
        ?.attributes('href'),
    ).toBe(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);
  });

  it("signs in from a reset password toward the link's return URL", async () => {
    stubApi({ 'POST /api/account/reset-password': noContent });
    const { wrapper } = await renderAt(
      `/reset-password?email=ann%40example.test&token=r1&returnUrl=${encodeURIComponent(authorize)}`,
    );

    const hrefOf = (text: string) =>
      wrapper
        .findAll('a')
        .find((link) => link.text() === text)
        ?.attributes('href');
    expect(hrefOf('Send a new reset link')).toBe(`/forgot-password?returnUrl=${encodeURIComponent(authorize)}`);
    await wrapper.get('input[name="newPassword"]').setValue('a-much-longer-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(hrefOf('Sign in')).toBe(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);
  });

  it("keeps a change link's return URL for the sign-in once the token is cleared", async () => {
    stubApi();
    const { wrapper, router } = await renderAt(
      `/change-password?returnUrl=${encodeURIComponent(authorize)}#token=link-token`,
    );

    expect(router.currentRoute.value.hash).toBe('');
    expect(router.currentRoute.value.query).toEqual({ returnUrl: authorize });
    expect(
      wrapper
        .findAll('a')
        .find((link) => link.text() === 'Sign in')
        ?.attributes('href'),
    ).toBe(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);
  });

  it.each([
    [
      'carries the return URL into the reset link',
      `/forgot-password?returnUrl=${encodeURIComponent(authorize)}`,
      { returnUrl: authorize },
    ],
    ['carries no return URL to the console', '/forgot-password', {}],
  ])('%s', async (_case, path, extra) => {
    const received = stubApi({ 'POST /api/account/forgot-password': noContent });
    const { wrapper } = await renderAt(path);

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toEqual({ email: 'ann@example.test', ...extra });
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

describe('sign-up route', () => {
  const pending = {
    organizationName: 'Ocharo',
    role: 'editor',
    status: 'pending',
    phoneNumberHint: null,
    emailHint: 'a***@example.test',
    expiresAt: '2026-11-09T07:00:00+00:00',
  };

  it('carries the return URL into the confirmation link of a sign-up', async () => {
    const received = stubApi({
      'POST /api/account/sign-up': () => Response.json({ data: { userId: 'u1', isConfirmationSent: true } }),
    });
    const { wrapper } = await renderAt(`/sign-up?returnUrl=${encodeURIComponent(authorize)}`);

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('a-much-longer-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toEqual({
      email: 'ann@example.test',
      password: 'a-much-longer-password',
      returnUrl: authorize,
    });
  });

  it('offers no form while sign-up is by invitation only and no link came', async () => {
    stubApi();
    withOptions({ signUpMode: 'invite' });
    const { wrapper } = await renderAt('/sign-up');

    expect(wrapper.text()).toContain('Sign-up is by invitation only');
    expect(wrapper.find('input[name="email"]').exists()).toBe(false);
  });

  it("names the link's organization and role, then signs up by email with its token", async () => {
    const received = stubApi({
      'GET /api/account/manage/info': unauthorized,
      'GET /api/account/invitations/tok-1': () => Response.json({ data: pending }),
      'POST /api/account/sign-up': () => Response.json({ data: { userId: 'u2', isConfirmationSent: true } }),
    });
    withOptions({
      signUpMode: 'invite',
      methods: [{ id: SignInMethodId.Password }, { id: SignInMethodId.PhoneCode, countries: ['UZ'], codeRules: [] }],
    });
    const { wrapper } = await renderAt('/sign-up?invitation=tok-1');

    expect(wrapper.get('h1').text()).toBe('Join');
    expect(wrapper.text()).toContain('You are invited to join Ocharo as editor.');
    expect(wrapper.text()).toContain('a***@example.test');
    expect(wrapper.findAll('section').map((section) => section.attributes('aria-label'))).toEqual(['Email', 'Phone']);
    const signIn = wrapper.findAll('a').find((link) => link.text() === 'Sign in');
    expect(signIn?.attributes('href')).toBe(`/sign-in?returnUrl=${encodeURIComponent('/sign-up?invitation=tok-1')}`);

    await wrapper.get('section[aria-label="Email"] input[name="email"]').setValue('ann@example.test');
    await wrapper.get('section[aria-label="Email"] input[name="password"]').setValue('a-long-password');
    await wrapper.get('section[aria-label="Email"] form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toEqual({
      email: 'ann@example.test',
      password: 'a-long-password',
      invitationToken: 'tok-1',
    });
  });

  it('lets a signed-in person join at once, then continues', async () => {
    const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
    const received = stubApi({
      'GET /api/account/manage/info': () => Response.json({ data: { userId: 'u1', email: 'ann@example.test' } }),
      'GET /api/account/invitations/tok-1': () => Response.json({ data: pending }),
      'POST /api/account/invitations/tok-1/accept': noContent,
    });
    const { wrapper } = await renderAt(`/sign-up?invitation=tok-1&returnUrl=${encodeURIComponent(authorize)}`);

    expect(wrapper.find('input[name="email"]').exists()).toBe(false);
    const join = wrapper.findAll('button').find((button) => button.text() === 'Join Ocharo');
    await join?.trigger('click');
    await flushPromises();

    expect(received.at(-1)).toMatchObject({
      method: 'POST',
      url: new URL('http://localhost/api/account/invitations/tok-1/accept'),
    });
    expect(assign).toHaveBeenCalledWith(authorize);
  });

  it.each([
    ['expired', 'This invitation has expired.'],
    ['accepted', 'This invitation was already used.'],
    ['revoked', 'This invitation was withdrawn.'],
  ])('says a link that is %s no longer works', async (status, message) => {
    stubApi({
      'GET /api/account/manage/info': unauthorized,
      'GET /api/account/invitations/tok-1': () => Response.json({ data: { ...pending, status } }),
    });
    const { wrapper } = await renderAt('/sign-up?invitation=tok-1');

    expect(wrapper.text()).toContain(message);
    expect(wrapper.find('form').exists()).toBe(false);
  });

  it('says a link naming no invitation is not valid', async () => {
    stubApi({ 'GET /api/account/manage/info': unauthorized });
    const { wrapper } = await renderAt('/sign-up?invitation=unknown');

    expect(wrapper.text()).toContain('This invitation link is not valid.');
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

  it('offers the phone method, not the email form, when an open declaration lists no password method', async () => {
    const [, phone] = instanceDeclaration.methods;
    stubApi({
      'GET /api/account/sign-in-options': () => Response.json({ data: { ...instanceDeclaration, methods: [phone] } }),
    });
    const { wrapper } = await renderAt('/sign-up');

    expect(wrapper.find('input[name="email"]').exists()).toBe(false);
    expect(wrapper.find('input[name="password"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('+998');
  });

  it('asks for no email on a phone-only instance: no forgot-password link and no email form', async () => {
    const [password, phone] = instanceDeclaration.methods;
    stubApi({
      'GET /api/account/sign-in-options': () =>
        Response.json({ data: { ...instanceDeclaration, isEmailEnabled: false, methods: [password, phone] } }),
    });
    const { wrapper, router } = await renderAt('/sign-in');

    expect(wrapper.find('section[aria-label="Password"]').exists()).toBe(true);
    expect(wrapper.findAll('a').map((link) => link.text())).not.toContain('Forgot your password?');
    await router.push('/sign-up');
    await flushPromises();
    expect(wrapper.find('input[name="email"]').exists()).toBe(false);
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
