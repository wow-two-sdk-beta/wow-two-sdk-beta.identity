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
  vi.useRealTimers();
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
      const body =
        typeof init?.body === 'string' ? JSON.parse(init.body) : init?.body instanceof FormData ? init.body : undefined;
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

/** An invitation sign-up that asks for nothing beside the phone. */
const noProfile: SignInOptions['signUpProfile'] = {
  displayName: 'off',
  picture: 'off',
  pictureRules: {
    acceptedTypes: ['image/jpeg', 'image/png', 'image/webp'],
    maxBytes: 5_242_880,
    minDimension: 256,
    maxDimension: 4096,
  },
};

function withOptions(overrides: Partial<SignInOptions>) {
  const base: SignInOptions = {
    brandName: 'Identity',
    theme: 'platform',
    backgroundImageUrl: null,
    signUpMode: 'open',
    isPersistenceOffered: false,
    isEmailEnabled: true,
    isTrustedContactSignInEnabled: false,
    methods: [{ id: SignInMethodId.Password }],
    passwordRules: [],
    signUpProfile: noProfile,
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

const organizationId = '0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5b';
const ocharo = { id: organizationId, name: 'Ocharo', slug: 'ocharo', role: 'owner' };
const membersPath = `/organizations/${organizationId}/members`;
const invitationsPath = `/organizations/${organizationId}/invitations`;

/** A console session answer: Lola, an instance admin owning Ocharo, unless `overrides` say otherwise. */
const sessionOf =
  (overrides: Record<string, unknown> = {}) =>
  () =>
    Response.json({
      data: { userId: 'u1', name: 'Lola Karimova', roles: ['admin'], organizations: [ocharo], ...overrides },
    });
const consoleAdmin = sessionOf();
const forbidden = () =>
  Response.json({ status: 403 }, { status: 403, headers: { 'Content-Type': 'application/problem+json' } });

/** The console's sidebar navigation. */
function sidebar(wrapper: ReturnType<typeof mount>) {
  return wrapper.get('nav[aria-label="Sidebar"]');
}

/** Each sidebar link as its text and address. */
function navLinks(wrapper: ReturnType<typeof mount>) {
  return sidebar(wrapper)
    .findAll('a')
    .map((link) => [link.text(), link.attributes('href')]);
}

/** Opens the account menu at the end of the app bar; its panel renders at the end of the document. */
async function openAccountMenu(wrapper: ReturnType<typeof mount>) {
  await wrapper.get('button[aria-label^="Account menu for"]').trigger('click');
  await flushPromises();
}

/** Clicks the account menu's row whose text is `text`. */
async function selectMenuItem(text: string) {
  const row = [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find((item) =>
    item.textContent?.includes(text),
  );
  expect(row, text).toBeDefined();
  row?.click();
  await flushPromises();
}

describe('console shell', () => {
  it('frames the console: the brand, the signed-in person with their instance roles, and the navigation', async () => {
    stubApi({ 'GET /api/console/session': consoleAdmin });
    const { wrapper } = await renderAt('/');

    expect(wrapper.find('.hosted-card').exists()).toBe(false);
    expect(wrapper.get('a[aria-label="Console overview"]').text()).toContain('Identity');
    const person = wrapper.get('[aria-label="Signed in"]');
    expect(person.text()).toContain('Lola Karimova');
    expect(person.get('[aria-label="Instance roles"]').text()).toBe('admin');
    expect(wrapper.find('button[aria-label="Account menu for Lola Karimova"]').exists()).toBe(true);
    expect(navLinks(wrapper)).toEqual([
      ['Overview', '/'],
      ['Members', membersPath],
      ['Invitations', invitationsPath],
      ['Create organization', '/organizations/new'],
    ]);
    expect(sidebar(wrapper).get('a[aria-current="page"]').text()).toBe('Overview');
  });

  it('shows the overview: the running versions, the organizations and the hosted pages', async () => {
    stubApi({ 'GET /api/console/session': consoleAdmin });
    const { wrapper } = await renderAt('/');

    expect(wrapper.get('h1').text()).toBe('Overview');
    expect(wrapper.get('[aria-label="App version"]').text()).toBe('app test');
    expect(wrapper.get('[aria-label="API version"]').text()).toBe('api 0.1.4+abc1234');
    const organizations = wrapper.get('[aria-labelledby="organizations-heading"]');
    expect(organizations.text()).toContain('Ocharo');
    expect(organizations.findAll('a').map((link) => link.attributes('href'))).toEqual([
      '/organizations/new',
      membersPath,
      invitationsPath,
    ]);
    expect(wrapper.findAll('a').map((link) => link.attributes('href'))).toContain('/sign-in');
  });

  it("gives an organization's owner without an instance role its pages but no organization creation", async () => {
    stubApi({ 'GET /api/console/session': sessionOf({ name: 'Gulnora', roles: [] }) });
    const { wrapper } = await renderAt('/');

    expect(wrapper.get('[aria-label="Signed in"]').text()).toBe('Gulnora');
    expect(wrapper.find('[aria-label="Instance roles"]').exists()).toBe(false);
    expect(navLinks(wrapper).map(([text]) => text)).toEqual(['Overview', 'Members', 'Invitations']);
    expect(wrapper.get('[aria-labelledby="organizations-heading"]').text()).not.toContain('Create organization');
  });

  it('says there is nobody to invite when the admin manages no organization', async () => {
    stubApi({ 'GET /api/console/session': sessionOf({ organizations: [] }) });
    const { wrapper } = await renderAt('/');

    expect(wrapper.text()).toContain('You own or administer no organization');
    expect(navLinks(wrapper).map(([text]) => text)).toEqual(['Overview', 'Create organization']);
    expect(sidebar(wrapper).text()).toContain('None yet');
  });

  it("opens an organization's members and invitations as their own pages", async () => {
    const received = stubApi({ 'GET /api/console/session': consoleAdmin });
    const { wrapper, router } = await renderAt(membersPath);
    await flushPromises();

    expect(wrapper.get('h1').text()).toBe('Ocharo');
    expect(wrapper.get('nav[aria-label="Ocharo pages"] a[aria-current="page"]').text()).toBe('Members');
    expect(sidebar(wrapper).get('a[aria-current="page"]').attributes('href')).toBe(membersPath);
    expect(received.map((request) => request.url.pathname)).toContain(`/api/organizations/${organizationId}/members`);

    await sidebar(wrapper)
      .findAll('a')
      .find((link) => link.text() === 'Invitations')
      ?.trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe(invitationsPath));
    await flushPromises();

    expect(wrapper.get('nav[aria-label="Ocharo pages"] a[aria-current="page"]').text()).toBe('Invitations');
    expect(received.map((request) => request.url.pathname)).toContain(
      `/api/organizations/${organizationId}/invitations`,
    );
  });

  it('tells the person when the route names an organization they do not manage', async () => {
    stubApi({ 'GET /api/console/session': consoleAdmin });
    const { wrapper } = await renderAt('/organizations/0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5f/members');
    await flushPromises();

    expect(wrapper.text()).toContain('You do not own or administer this organization.');
  });

  it('reads the session again on every move, so a role granted since shows without a new sign-in', async () => {
    let roles: string[] = [];
    stubApi({ 'GET /api/console/session': () => sessionOf({ roles })() });
    const { wrapper, router } = await renderAt('/');
    expect(navLinks(wrapper).map(([text]) => text)).not.toContain('Create organization');

    roles = ['admin'];
    await router.push(membersPath);
    await flushPromises();

    expect(navLinks(wrapper).map(([text]) => text)).toContain('Create organization');
    expect(wrapper.get('[aria-label="Instance roles"]').text()).toBe('admin');
  });

  it('shows the no-access view once a revoke leaves the person nothing to manage', async () => {
    let isRevoked = false;
    stubApi({
      'GET /api/console/session': () => (isRevoked ? forbidden() : sessionOf({ organizations: [] })()),
    });
    const { wrapper, router } = await renderAt('/');

    isRevoked = true;
    await router.push('/organizations/new');
    await flushPromises();

    expect(wrapper.text()).toContain('This account has no console access.');
    expect(wrapper.find('nav[aria-label="Sidebar"]').exists()).toBe(false);
  });

  it('sends a signed-out visitor to sign in, coming back to the page they asked for', async () => {
    stubApi({ 'GET /api/console/session': unauthorized, 'GET /api/account/manage/info': unauthorized });
    const { wrapper, router } = await renderAt(membersPath);

    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/sign-in'));
    expect(router.currentRoute.value.query['returnUrl']).toBe(membersPath);
    await flushPromises();
    expect(wrapper.findAll('h1').map((heading) => heading.text())).toEqual(['Sign in']);
  });

  it('sends a signed-out visitor to sign in, returning to the console by default', async () => {
    stubApi({ 'GET /api/console/session': unauthorized, 'GET /api/account/manage/info': unauthorized });
    const { wrapper, router } = await renderAt('/');

    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/sign-in'));
    await flushPromises();
    expect(wrapper.findAll('h1').map((heading) => heading.text())).toEqual(['Sign in']);
  });

  it('tells a person with neither an instance role nor an organization, and lets them sign out', async () => {
    const received = stubApi({
      'GET /api/console/session': forbidden,
      'POST /api/account/sign-out': noContent,
      'GET /api/account/manage/info': unauthorized,
    });
    const { wrapper, router } = await renderAt('/');

    expect(wrapper.text()).toContain('This account has no console access.');
    expect(wrapper.find('[aria-labelledby="hosted-heading"]').exists()).toBe(false);
    expect(wrapper.find('[aria-label="App version"]').exists()).toBe(false);
    expect(wrapper.find('[aria-label="Signed in"]').exists()).toBe(false);

    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Sign out')
      ?.trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/sign-in'));
    await flushPromises();

    expect(
      received.some((request) => request.method === 'POST' && request.url.pathname === '/api/account/sign-out'),
    ).toBe(true);
    expect(wrapper.findAll('section').map((section) => section.attributes('aria-label'))).toEqual([
      'Password',
      'Phone',
    ]);
  });

  it('shows nothing but a loading state until the session resolves', async () => {
    let answer: (response: Response) => void = () => undefined;
    stubApi({ 'GET /api/console/session': () => new Promise<Response>((resolve) => (answer = resolve)) as never });
    const { wrapper } = await renderAt('/');

    expect(wrapper.get('[aria-label="Loading"]').text()).toContain('Loading the console');
    expect(wrapper.find('h1').exists()).toBe(false);
    expect(wrapper.find('[aria-label="Signed in"]').exists()).toBe(false);
    expect(wrapper.find('nav[aria-label="Sidebar"]').exists()).toBe(false);

    answer(consoleAdmin());
    await flushPromises();
    expect(wrapper.find('[aria-label="Loading"]').exists()).toBe(false);
    await vi.waitFor(() => expect(wrapper.get('h1').text()).toBe('Overview'));
  });
});

describe('console sign-out', () => {
  it('signs out from the account menu and lands on the sign-in page, which returns to the console', async () => {
    const received = stubApi({
      'GET /api/console/session': consoleAdmin,
      'POST /api/account/sign-out': noContent,
      'GET /api/account/manage/info': unauthorized,
    });
    const { wrapper, router } = await renderAt(membersPath);

    await openAccountMenu(wrapper);
    expect(document.querySelector('[data-account-menu-identity]')?.textContent).toContain('admin');
    await selectMenuItem('Sign out');
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/sign-in'));
    await flushPromises();

    const signOut = received.find((request) => request.url.pathname === '/api/account/sign-out');
    expect(signOut?.method).toBe('POST');
    expect(wrapper.findAll('h1').map((heading) => heading.text())).toEqual(['Sign in']);
    expect(wrapper.findAll('section').map((section) => section.attributes('aria-label'))).toEqual([
      'Password',
      'Phone',
    ]);
  });

  it('stays in the console and says so when the sign-out fails', async () => {
    stubApi({
      'GET /api/console/session': consoleAdmin,
      'POST /api/account/sign-out': () =>
        Response.json({ status: 500 }, { status: 500, headers: { 'Content-Type': 'application/problem+json' } }),
    });
    const { wrapper, router } = await renderAt('/');

    await openAccountMenu(wrapper);
    await selectMenuItem('Sign out');

    expect(router.currentRoute.value.path).toBe('/');
    expect(wrapper.text()).toContain('We could not sign you out. Try again.');
  });

  it('opens the account security page from the account menu', async () => {
    stubApi({
      'GET /api/console/session': consoleAdmin,
      'GET /api/account/manage/info': () => Response.json({ data: { userId: 'u1', email: 'lola@example.test' } }),
    });
    const { wrapper, router } = await renderAt('/');

    await openAccountMenu(wrapper);
    await selectMenuItem('Account security');

    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/account/security'));
  });
});

describe('console organization creation', () => {
  it('lets an instance admin create an organization, then opens its members', async () => {
    const created = { id: '0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5d', name: 'Desk', slug: 'desk', role: 'owner' };
    let organizations = [ocharo];
    const received = stubApi({
      'GET /api/console/session': () => sessionOf({ organizations })(),
      'POST /api/console/organizations': () => {
        organizations = [ocharo, created];
        return Response.json({ data: created }, { status: 201 });
      },
    });
    const { wrapper, router } = await renderAt('/organizations/new');
    await flushPromises();

    expect(wrapper.get('h1').text()).toBe('Create organization');
    await wrapper.get('input[name="name"]').setValue(' Desk ');
    await wrapper.get('input[name="slug"]').setValue('Desk');
    await wrapper.get('input[name="id"]').setValue(created.id);
    await wrapper.get('form[aria-label="Create organization"]').trigger('submit');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe(`/organizations/${created.id}/members`));
    await flushPromises();

    const post = received.find(
      (request) => request.method === 'POST' && request.url.pathname === '/api/console/organizations',
    );
    expect(post?.body).toEqual({ name: 'Desk', slug: 'desk', id: created.id });
    expect(wrapper.get('h1').text()).toBe('Desk');
    expect(received.map((request) => request.url.pathname)).toContain(`/api/organizations/${created.id}/members`);
  });

  it('leaves the id out when empty and shows a taken handle under its field', async () => {
    const received = stubApi({
      'GET /api/console/session': consoleAdmin,
      'POST /api/console/organizations': () =>
        Response.json(
          { status: 409, detail: "Handle 'ocharo' is already taken.", identityCode: 'DuplicateOrganizationSlug' },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        ),
    });
    const { wrapper, router } = await renderAt('/organizations/new');
    await flushPromises();

    await wrapper.get('input[name="name"]').setValue('Ocharo Two');
    await wrapper.get('input[name="slug"]').setValue('ocharo');
    await wrapper.get('form[aria-label="Create organization"]').trigger('submit');
    await flushPromises();

    expect(received.find((request) => request.method === 'POST')?.body).toEqual({ name: 'Ocharo Two', slug: 'ocharo' });
    expect(errorUnder(wrapper, 'slug')).toContain("Handle 'ocharo' is already taken.");
    expect(router.currentRoute.value.path).toBe('/organizations/new');
  });

  it('checks the handle and the id before sending anything', async () => {
    const received = stubApi({ 'GET /api/console/session': consoleAdmin });
    const { wrapper } = await renderAt('/organizations/new');
    await flushPromises();

    await wrapper.get('input[name="name"]').setValue('Desk');
    await wrapper.get('input[name="slug"]').setValue('not a handle');
    await wrapper.get('input[name="id"]').setValue('42');
    await wrapper.get('form[aria-label="Create organization"]').trigger('submit');
    await flushPromises();

    expect(received.some((request) => request.method === 'POST')).toBe(false);
    expect(errorUnder(wrapper, 'slug')).toContain('lowercase letters, digits and single hyphens');
    expect(errorUnder(wrapper, 'id')).toContain('Enter a uuid');
  });

  it('tells a person without the instance role admin that only an admin creates organizations', async () => {
    stubApi({ 'GET /api/console/session': sessionOf({ roles: ['support'] }) });
    const { wrapper } = await renderAt('/organizations/new');
    await flushPromises();

    expect(wrapper.text()).toContain('Only an instance admin creates organizations.');
    expect(wrapper.find('form[aria-label="Create organization"]').exists()).toBe(false);
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

  describe('when the browser is signed in already', () => {
    const signedIn = () => Response.json({ data: { userId: 'u1', email: 'ann@example.test' } });

    it.each([
      ['the return URL', `/sign-in?returnUrl=${encodeURIComponent(authorize)}`, authorize],
      ['the console without one', '/sign-in', '/'],
    ])('continues to %s at once, showing only a loading state', async (_case, path, target) => {
      let answer: (response: Response) => void = () => undefined;
      stubApi({
        'GET /api/account/manage/info': () => new Promise<Response>((resolve) => (answer = resolve)) as never,
      });
      const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
      const { wrapper } = await renderAt(path);

      expect(wrapper.get('[aria-label="Loading"]').text()).toContain('Checking your sign-in');
      expect(wrapper.find('h1').exists()).toBe(false);
      expect(wrapper.find('section').exists()).toBe(false);

      answer(signedIn());
      await flushPromises();
      expect(assign).toHaveBeenCalledWith(target);
      expect(wrapper.find('section').exists()).toBe(false);
    });

    it.each([
      ['prompt=login', '&prompt=login'],
      ['a prompt list with login', '&prompt=consent%20login'],
      ['max_age', '&max_age=0'],
    ])('shows the methods when the authorization request asks for a fresh sign-in with %s', async (_case, extra) => {
      const received = stubApi({ 'GET /api/account/manage/info': signedIn });
      const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
      const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize + extra)}`);

      expect(wrapper.findAll('section').map((section) => section.attributes('aria-label'))).toEqual([
        'Password',
        'Phone',
      ]);
      expect(assign).not.toHaveBeenCalled();
      expect(received.map((request) => request.url.pathname)).not.toContain('/api/account/manage/info');
    });
  });

  it('shows the methods to a signed-out browser', async () => {
    const received = stubApi({ 'GET /api/account/manage/info': unauthorized });
    const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
    const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    expect(received.map((request) => request.url.pathname)).toContain('/api/account/manage/info');
    expect(wrapper.find('[aria-label="Loading"]').exists()).toBe(false);
    expect(wrapper.get('h1').text()).toBe('Sign in');
    expect(wrapper.findAll('section').map((section) => section.attributes('aria-label'))).toEqual([
      'Password',
      'Phone',
    ]);
    expect(assign).not.toHaveBeenCalled();
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

  describe('by phone', () => {
    const telegramLink = 'https://t.me/identity_bot?start=su-1';

    /** Opens an invitation link on a phone-only instance that asks for a name, ready to verify the phone. */
    async function openPhoneInvitation(status: Record<string, unknown>) {
      const received = stubApi({
        'GET /api/account/manage/info': unauthorized,
        'GET /api/account/invitations/tok-1': () => Response.json({ data: { ...pending, emailHint: null } }),
        'POST /api/account/invitations/tok-1/sign-up': () =>
          Response.json({ data: { telegramLinkUrl: telegramLink, expiresAt: '2099-01-01T00:00:00Z' } }),
        'GET /api/account/invitations/sign-up/status': () =>
          Response.json({ data: { failureCode: null, isSignedIn: false, ...status } }),
      });
      vi.spyOn(window, 'open').mockReturnValue(null);
      withOptions({
        signUpMode: 'invite',
        isEmailEnabled: false,
        methods: [{ id: SignInMethodId.PhoneCode, countries: ['UZ'], codeRules: [] }],
        signUpProfile: { ...noProfile, displayName: 'required' },
      });
      const rendered = await renderAt(`/sign-up?invitation=tok-1&returnUrl=${encodeURIComponent(authorize)}`);
      return { received, ...rendered };
    }

    async function verifyInTelegram(wrapper: ReturnType<typeof mount>) {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
      await wrapper.get('input[name="displayName"]').setValue('Lola');
      await wrapper.get('section[aria-label="Phone"] form').trigger('submit');
      await flushPromises();
      await vi.advanceTimersByTimeAsync(2000);
    }

    it('asks for the declared profile and verifies the phone in Telegram, then continues signed in', async () => {
      const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
      const { wrapper, received } = await openPhoneInvitation({ status: 'completed', isSignedIn: true });

      expect(wrapper.find('input[name="phone"]').exists()).toBe(false);
      expect(wrapper.find('input[name="code"]').exists()).toBe(false);
      expect(wrapper.findAll('button').map((button) => button.text())).toContain('Verify phone in Telegram');
      await verifyInTelegram(wrapper);

      const started = received.find((request) => request.method === 'POST');
      expect(started?.url.pathname).toBe('/api/account/invitations/tok-1/sign-up');
      expect(started?.url.search).toBe('?clientId=desk');
      expect((started?.body as FormData).get('displayName')).toBe('Lola');
      expect(window.open).toHaveBeenCalledWith(telegramLink, '_blank', 'noopener,noreferrer');
      expect(assign).toHaveBeenCalledWith(authorize);
    });

    it('sends a person with a second factor to sign in, keeping the return URL', async () => {
      const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
      const { wrapper, router } = await openPhoneInvitation({ status: 'completed', isSignedIn: false });

      await verifyInTelegram(wrapper);

      expect(router.currentRoute.value.fullPath).toBe(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);
      expect(assign).not.toHaveBeenCalled();
    });
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

describe('sign-in with trusted contacts', () => {
  const trusted = () => Response.json({ data: { ...instanceDeclaration, isTrustedContactSignInEnabled: true } });
  const contacts = () => Response.json({ data: { trustedContacts: [{ id: 't1', label: 'L*** K.' }] } });
  const sent = () => Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } });
  const phoneForm = 'section[aria-label="Phone"] form';
  const choice = {
    token: 'choice-1',
    accounts: [
      { userId: 'u1', displayName: 'Anvar', pictureUrl: null, isSelf: true },
      { userId: 'u2', displayName: 'Lola', pictureUrl: null, isSelf: false },
    ],
  };

  async function enterPhone(wrapper: ReturnType<typeof mount>) {
    await wrapper.get('section[aria-label="Phone"] input[type="tel"]').setValue('901234567');
    await wrapper.get(phoneForm).trigger('submit');
    await flushPromises();
  }

  async function enterCode(wrapper: ReturnType<typeof mount>) {
    await wrapper.get('input[name="code"]').setValue('123456');
    await wrapper.get(phoneForm).trigger('submit');
    await flushPromises();
  }

  it('asks nothing of the phone’s contacts while the declaration keeps them off', async () => {
    const received = stubApi({ 'POST /api/account/phone-sign-in/send': sent });
    const { wrapper } = await renderAt('/sign-in');

    await enterPhone(wrapper);

    expect(received.map((request) => request.url.pathname)).not.toContain('/api/account/phone-sign-in/targets');
    expect(wrapper.find('input[name="code"]').exists()).toBe(true);
  });

  it('offers my Telegram or a contact after the number, and sends the code to the contact picked', async () => {
    const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
    const received = stubApi({
      'GET /api/account/sign-in-options': trusted,
      'POST /api/account/phone-sign-in/targets': contacts,
      'POST /api/account/phone-sign-in/send': sent,
      'POST /api/account/phone-sign-in': noContent,
    });
    const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    await enterPhone(wrapper);

    const labels = wrapper.findAll('input[type="radio"]').map((radio) => radio.element.closest('label')?.textContent);
    expect(labels).toHaveLength(2);
    expect(labels[0]).toContain('My Telegram');
    expect(labels[1]).toContain('L*** K.');
    await wrapper.findAll('input[type="radio"]')[1]?.setValue(true);
    await wrapper.get(phoneForm).trigger('submit');
    await flushPromises();
    await enterCode(wrapper);

    const calls = received
      .filter((request) => request.method === 'POST')
      .map((request) => [request.url.pathname, request.body]);
    expect(calls).toEqual([
      ['/api/account/phone-sign-in/targets', { phone: '+998901234567' }],
      ['/api/account/phone-sign-in/send', { phone: '+998901234567', targetId: 't1' }],
      ['/api/account/phone-sign-in', { phone: '+998901234567', code: '123456', isPersistent: false }],
    ]);
    expect(assign).toHaveBeenCalledWith(authorize);
  });

  it('sends the code to my Telegram by default, with no target', async () => {
    const received = stubApi({
      'GET /api/account/sign-in-options': trusted,
      'POST /api/account/phone-sign-in/targets': contacts,
      'POST /api/account/phone-sign-in/send': sent,
    });
    const { wrapper } = await renderAt('/sign-in');

    await enterPhone(wrapper);
    await wrapper.get(phoneForm).trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toEqual({ phone: '+998901234567' });
  });

  it('lists the accounts a trusted contact can sign in as, and continues once one is picked', async () => {
    const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
    const received = stubApi({
      'GET /api/account/sign-in-options': trusted,
      'POST /api/account/phone-sign-in/targets': () => Response.json({ data: { trustedContacts: [] } }),
      'POST /api/account/phone-sign-in/send': sent,
      'POST /api/account/phone-sign-in': () => Response.json({ data: { accountChoice: choice } }),
      'POST /api/account/phone-sign-in/choose': noContent,
    });
    const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

    await enterPhone(wrapper);
    await enterCode(wrapper);

    expect(assign).not.toHaveBeenCalled();
    const accounts = wrapper.findAll('section[aria-label="Phone"] li');
    expect(accounts.map((account) => account.text().includes('You'))).toEqual([true, false]);
    expect(accounts[1]?.text()).toContain('Lola');
    await accounts[1]?.get('button').trigger('click');
    await flushPromises();

    const chosen = received.find((request) => request.url.pathname === '/api/account/phone-sign-in/choose');
    expect(chosen?.url.search).toBe('?useCookies=true&clientId=desk');
    expect(chosen?.body).toEqual({ token: 'choice-1', userId: 'u2', isPersistent: false });
    expect(assign).toHaveBeenCalledWith(authorize);
  });

  it('waits for a trusted contact on a new device and continues once they approve', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    try {
      const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
      const received = stubApi({
        'GET /api/account/sign-in-options': trusted,
        'POST /api/account/phone-sign-in/targets': () => Response.json({ data: { trustedContacts: [] } }),
        'POST /api/account/phone-sign-in/send': sent,
        'POST /api/account/phone-sign-in': () =>
          Response.json({
            data: {
              deviceConfirmation: {
                token: 'dc-1',
                expiresAt: new Date(Date.now() + 600_000).toISOString(),
                contacts: ['L*** K.'],
              },
            },
          }),
        'POST /api/account/phone-sign-in/confirmation': noContent,
      });
      const { wrapper } = await renderAt(`/sign-in?returnUrl=${encodeURIComponent(authorize)}`);

      await enterPhone(wrapper);
      await enterCode(wrapper);

      expect(wrapper.text()).toContain('We asked L*** K. to confirm it’s you in Telegram.');
      expect(assign).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(3000);

      const polled = received.find((request) => request.url.pathname === '/api/account/phone-sign-in/confirmation');
      expect(polled?.url.search).toBe('?useCookies=true&clientId=desk');
      expect(polled?.body).toEqual({ token: 'dc-1', isPersistent: false });
      expect(assign).toHaveBeenCalledWith(authorize);
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('sign-up by a trusted contact', () => {
  const hint = 'A*** K.';
  const preview = {
    organizationName: 'Ocharo',
    role: 'owner',
    status: 'pending',
    phoneNumberHint: null,
    emailHint: null,
    trustedContactHint: hint,
    expiresAt: '2026-11-09T07:00:00+00:00',
  };

  /** Opens an invitation link on a phone-only instance, whose preview names a trusted contact unless `data` says not. */
  async function openInvitation(data: Record<string, unknown> = preview) {
    const received = stubApi({
      'GET /api/account/manage/info': unauthorized,
      'GET /api/account/invitations/tok-1': () => Response.json({ data }),
      'POST /api/account/invitations/tok-1/sign-up': () =>
        Response.json({ data: { isCodeSentToTrustedContact: true, expiresAt: '2099-01-01T00:00:00Z' } }),
      'POST /api/account/invitations/sign-up/verify': noContent,
    });
    vi.spyOn(window, 'open').mockReturnValue(null);
    withOptions({
      signUpMode: 'invite',
      isEmailEnabled: false,
      isPersistenceOffered: true,
      methods: [{ id: SignInMethodId.PhoneCode, countries: ['UZ'], codeRules: [] }],
      signUpProfile: { ...noProfile, displayName: 'required' },
    });
    const rendered = await renderAt(`/sign-up?invitation=tok-1&returnUrl=${encodeURIComponent(authorize)}`);
    return { received, ...rendered };
  }

  it('offers no choice while the link names no contact', async () => {
    const { wrapper } = await openInvitation({ ...preview, trustedContactHint: null });

    expect(wrapper.find('input[type="radio"]').exists()).toBe(false);
    expect(wrapper.findAll('button').map((button) => button.text())).toContain('Verify phone in Telegram');
  });

  it('verifies the sign-up with the contact’s code, then continues signed in', async () => {
    const assign = vi.spyOn(hostedNavigation, 'assign').mockImplementation(() => undefined);
    const { wrapper, received } = await openInvitation();

    expect(wrapper.get('section[aria-label="Phone"]').text()).toContain('With my own Telegram');
    await wrapper.findAll('input[type="radio"]')[1]?.setValue(true);
    expect(wrapper.text()).toContain('full access to your account');
    await wrapper.get('input[name="displayName"]').setValue('Lola');
    await wrapper.get('section[aria-label="Phone"] form').trigger('submit');
    await flushPromises();

    const started = received.find((request) => request.url.pathname === '/api/account/invitations/tok-1/sign-up');
    expect(started?.url.search).toBe('?clientId=desk');
    expect([...(started?.body as FormData).entries()]).toEqual([
      ['displayName', 'Lola'],
      ['verification', 'trusted-contact'],
    ]);
    expect(window.open).not.toHaveBeenCalled();
    expect(wrapper.text()).toContain(`We sent a code to ${hint} in Telegram.`);

    await wrapper.get('input[name="code"]').setValue('123456');
    await wrapper.get('input[type="checkbox"]').setValue(true);
    await wrapper.get('section[aria-label="Phone"] form').trigger('submit');
    await flushPromises();

    const verified = received.find((request) => request.url.pathname === '/api/account/invitations/sign-up/verify');
    expect(verified?.url.search).toBe('?clientId=desk');
    expect(verified?.body).toEqual({ code: '123456', isPersistent: true });
    expect(assign).toHaveBeenCalledWith(authorize);
  });
});

describe('account security', () => {
  const info = () => Response.json({ data: { userId: 'u1', userName: '+998901234567' } });
  const lola = {
    userId: 'u2',
    displayName: 'Lola',
    phoneHint: '+998*******45',
    isDeliverable: true,
    createdAt: '2026-11-03T08:00:00+00:00',
  };
  const anvar = { userId: 'u3', displayName: 'Anvar', createdAt: '2026-11-03T08:00:00+00:00', canLeave: true };
  const session = {
    id: 's1',
    createdAt: '2026-11-03T08:00:00+00:00',
    lastSeenAt: '2026-11-03T09:00:00+00:00',
    expiresAt: '2026-11-10T08:00:00+00:00',
    authenticationMethods: ['otp'],
    ipMasked: '203.0.113.0',
    userAgent: 'Firefox on macOS',
    isCurrent: true,
  };
  const phone = (data: Record<string, unknown>) => () => Response.json({ data });
  const trustedOn = () => Response.json({ data: { ...instanceDeclaration, isTrustedContactSignInEnabled: true } });

  /** The answers of an account that signs in with a phone and has a contact, an account it trusts and two sessions. */
  const account = (overrides: Record<string, () => Response> = {}) => ({
    'GET /api/account/sign-in-options': trustedOn,
    'GET /api/account/manage/info': info,
    'GET /api/account/manage/phone': phone({ phoneHint: '+998*******67', isConfirmed: true, canChange: true }),
    'GET /api/account/manage/trusted-contacts': phone({ canRevoke: true, items: [lola] }),
    'GET /api/account/manage/trusted-by': () => Response.json({ data: [anvar] }),
    'GET /api/account/manage/sessions': () =>
      Response.json({
        data: [session, { ...session, id: 's2', isCurrent: false, trustedContactDisplayName: 'Anvar' }],
      }),
    ...overrides,
  });

  it('shows the phone, the trusted contacts, the people who trust the person and the sessions', async () => {
    stubApi(account());
    const { wrapper } = await renderAt('/account/security');

    const headings = wrapper.findAll('h2').map((heading) => heading.text());
    expect(headings).toEqual(['Password', 'Phone', 'Trusted contacts', 'People who trust you', 'Sessions']);
    expect(wrapper.text()).toContain('Your phone: +998*******67');
    expect(wrapper.get('section[aria-label="Trusted contacts"]').text()).toContain('Lola');
    expect(wrapper.get('section[aria-label="People who trust you"]').text()).toContain('Anvar');
    const sessions = wrapper.get('section[aria-label="Sessions"]').findAll('li');
    expect(sessions).toHaveLength(2);
    expect(sessions[1]?.text()).toContain('Signed in through Anvar');
  });

  it('sends the person to the sign-in and back once a changed phone ended the session', async () => {
    const held = phone({ phoneHint: '+998*******67', isConfirmed: true, canChange: true });
    let isEnded = false;
    vi.stubGlobal('open', vi.fn());
    stubApi(
      account({
        'GET /api/account/manage/phone': () => (isEnded ? unauthorized() : held()),
        'POST /api/account/manage/phone/telegram-link': () =>
          Response.json({ data: { telegramLinkUrl: 'https://t.me/ocharo_bot?start=ap-1' } }),
      }),
    );
    const { wrapper, router } = await renderAt('/account/security');
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });

    await wrapper
      .get('section[aria-label="Phone"]')
      .findAll('button')
      .find((button) => button.text() === 'Change phone')
      ?.trigger('click');
    await flushPromises();
    isEnded = true;
    await vi.advanceTimersByTimeAsync(2000);
    await flushPromises();

    expect(router.currentRoute.value.path).toBe('/sign-in');
    expect(router.currentRoute.value.query).toEqual({ returnUrl: '/account/security' });
  });

  it('revokes a trusted contact from a session signed in with the own phone', async () => {
    const received = stubApi(account({ 'DELETE /api/account/manage/trusted-contacts/u2': noContent }));
    const { wrapper } = await renderAt('/account/security');

    const section = wrapper.get('section[aria-label="Trusted contacts"]');
    await section
      .findAll('button')
      .find((button) => button.text() === 'Revoke')
      ?.trigger('click');
    await section
      .findAll('button')
      .find((button) => button.text() === 'Yes, revoke')
      ?.trigger('click');
    await flushPromises();

    expect(received.at(-1)).toMatchObject({ method: 'DELETE' });
    expect(received.at(-1)?.url.pathname).toBe('/api/account/manage/trusted-contacts/u2');
    expect(wrapper.get('section[aria-label="Trusted contacts"]').text()).toContain('You have no trusted contacts.');
  });

  it('leaves out the revoke and the leave the session may not do, and says why', async () => {
    stubApi(
      account({
        'GET /api/account/manage/phone': phone({ phoneHint: '+998*******67', isConfirmed: true, canChange: false }),
        'GET /api/account/manage/trusted-contacts': phone({ canRevoke: false, items: [lola] }),
        'GET /api/account/manage/trusted-by': () => Response.json({ data: [{ ...anvar, canLeave: false }] }),
      }),
    );
    const { wrapper } = await renderAt('/account/security');

    const buttons = wrapper.findAll('section button').map((button) => button.text());
    expect(buttons).not.toContain('Revoke');
    expect(buttons).not.toContain('Leave');
    expect(buttons).not.toContain('Change phone');
    expect(wrapper.text()).toContain('To revoke a trusted contact, sign in with your own phone.');
    expect(wrapper.text()).toContain('To change your phone, sign in with the phone you have now.');
    expect(wrapper.text()).toContain('You are the only contact of an account with no phone of its own');
  });

  it('leaves an account that trusts the person', async () => {
    const received = stubApi(account({ 'DELETE /api/account/manage/trusted-by/u3': noContent }));
    const { wrapper } = await renderAt('/account/security');

    const section = wrapper.get('section[aria-label="People who trust you"]');
    await section
      .findAll('button')
      .find((button) => button.text() === 'Leave')
      ?.trigger('click');
    await section
      .findAll('button')
      .find((button) => button.text() === 'Yes, leave')
      ?.trigger('click');
    await flushPromises();

    expect(received.at(-1)?.url.pathname).toBe('/api/account/manage/trusted-by/u3');
    expect(wrapper.find('section[aria-label="People who trust you"]').exists()).toBe(false);
  });

  it('adds a phone through the bot link when the account has none', async () => {
    const link = 'https://t.me/identity_bot?start=ap-1';
    vi.spyOn(window, 'open').mockReturnValue(null);
    const received = stubApi(
      account({
        'GET /api/account/manage/phone': phone({ isConfirmed: false, canChange: true }),
        'POST /api/account/manage/phone/telegram-link': () => Response.json({ data: { telegramLinkUrl: link } }),
      }),
    );
    const { wrapper } = await renderAt('/account/security');

    expect(wrapper.text()).toContain('You have no phone of your own yet.');
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Add a phone')
      ?.trigger('click');
    await flushPromises();

    expect(received.some((request) => request.url.pathname === '/api/account/manage/phone/telegram-link')).toBe(true);
    expect(window.open).toHaveBeenCalledWith(link, '_blank', 'noopener,noreferrer');
    expect(wrapper.get(`a[href="${link}"]`).text()).toBe('Open Telegram');
  });

  it('keeps the trusted blocks out while the instance has none, but not the contacts it still holds', async () => {
    stubApi(
      account({
        'GET /api/account/sign-in-options': () => Response.json({ data: instanceDeclaration }),
        'GET /api/account/manage/trusted-contacts': phone({ canRevoke: true, items: [] }),
        'GET /api/account/manage/trusted-by': () => Response.json({ data: [] }),
      }),
    );
    const { wrapper } = await renderAt('/account/security');

    expect(wrapper.findAll('h2').map((heading) => heading.text())).toEqual(['Password', 'Phone', 'Sessions']);
    wrapper.unmount();
    wrappers.pop();

    stubApi(
      account({
        'GET /api/account/sign-in-options': () => Response.json({ data: instanceDeclaration }),
        'GET /api/account/manage/trusted-by': () => Response.json({ data: [] }),
      }),
    );
    const held = await renderAt('/account/security');
    expect(held.wrapper.findAll('h2').map((heading) => heading.text())).toContain('Trusted contacts');
  });
});

describe('console trusted contacts', () => {
  const joined = '2026-10-20T08:00:00+00:00';
  const members = [
    {
      userId: 'u1',
      displayName: 'Lola Karimova',
      role: 'owner',
      joinedAt: joined,
      hasPendingTelegramChange: false,
      isPhoneConfirmed: true,
      trustedContacts: [],
    },
    {
      userId: 'u2',
      displayName: 'Bobur',
      role: 'editor',
      joinedAt: joined,
      hasPendingTelegramChange: false,
      isPhoneConfirmed: false,
      trustedContacts: [{ userId: 'u1', displayName: 'Lola Karimova' }],
    },
  ];
  const organizationApi = (path: string) => `/api/organizations/${organizationId}${path}`;

  it('shows each member’s phone state and their trusted contacts', async () => {
    stubApi({
      'GET /api/console/session': consoleAdmin,
      [`GET ${organizationApi('/members')}`]: () => Response.json({ data: members }),
    });
    const { wrapper } = await renderAt(membersPath);
    await flushPromises();

    const rows = wrapper.findAll('ul[aria-label="Members"] li');
    expect(rows).toHaveLength(2);
    expect(rows[0]?.text()).toContain('Phone confirmed');
    expect(rows[1]?.text()).toContain('No confirmed phone · Trusted contacts: Lola Karimova');
  });

  it('lets the signed-in person name themselves the invited person’s trusted contact', async () => {
    const created = {
      invitation: {
        id: 'i1',
        role: 'viewer',
        status: 'pending',
        phoneNumber: null,
        email: null,
        createdAt: joined,
        expiresAt: '2026-11-08T08:00:00+00:00',
        acceptedAt: null,
        acceptedByUserId: null,
      },
      token: 'tok-9',
      link: null,
    };
    const received = stubApi({
      'GET /api/console/session': consoleAdmin,
      [`GET ${organizationApi('/members')}`]: () => Response.json({ data: members }),
      [`GET ${organizationApi('/invitations')}`]: () => Response.json({ data: [] }),
      [`POST ${organizationApi('/invitations')}`]: () => Response.json({ data: created }),
    });
    const { wrapper } = await renderAt(invitationsPath);
    await flushPromises();

    const trigger = wrapper.get('button[aria-label="Trusted contact"]');
    await trigger.trigger('click');
    await flushPromises();
    const offered = [...document.querySelectorAll<HTMLElement>('[role="option"]')];
    expect(offered.map((option) => option.textContent?.trim())).toEqual(['No trusted contact', 'Me']);
    offered[1]?.click();
    await flushPromises();
    expect(wrapper.text()).toContain('full access to the account');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    const posted = received.find(
      (request) => request.method === 'POST' && request.url.pathname.endsWith('/invitations'),
    );
    expect(posted?.body).toEqual({ role: 'viewer', trustedContactUserId: 'u1' });
  });
});
