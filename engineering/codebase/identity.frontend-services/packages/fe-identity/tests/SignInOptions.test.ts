import { describe, expect, it } from 'vitest';
import type { SignInOptionsDto } from '../src/contracts';
import { fakeAccountApi, problem, serviceUrl } from './FakeAccountApi';

const declaration: SignInOptionsDto = {
  brandName: 'Ocharo',
  theme: 'ocharo',
  backgroundUrl: '/backgrounds/ocharo.jpg',
  isSignUpEnabled: true,
  isPersistenceOffered: true,
  locale: 'ru',
  locales: ['uz', 'ru', 'en'],
  methods: [
    {
      id: 'phone-code',
      channels: ['telegram'],
      fields: [
        {
          name: 'phone',
          kind: 'phone',
          countries: ['UZ'],
          rules: [{ code: 'required', params: null, message: 'Enter your phone number.' }],
        },
        {
          name: 'code',
          kind: 'pin',
          countries: null,
          rules: [{ code: 'length', params: { length: 6, unit: 'characters' }, message: 'The code has 6 characters.' }],
        },
      ],
      passwordRules: null,
    },
    {
      id: 'password',
      channels: [],
      fields: [
        {
          name: 'identifier',
          kind: 'text',
          countries: null,
          rules: [{ code: 'required', params: null, message: 'Enter your user name or email.' }],
        },
      ],
      passwordRules: [
        {
          code: 'min',
          params: { min: 15, unit: 'characters' },
          message: 'Пароль должен содержать не менее 15 символов.',
        },
      ],
    },
  ],
  requirements: {
    isConfirmedEmailRequired: false,
    isConfirmedPhoneNumberRequired: true,
    isSecondFactorRequired: false,
  },
};

describe('sign-in declaration', () => {
  it('reads the declaration for the app and locale it names', async () => {
    const { client, received } = fakeAccountApi({ 'GET /sign-in-options': () => Response.json({ data: declaration }) });

    const result = await client.getSignInOptions({ clientId: 'ocharo-web', locale: 'ru' });

    expect(result).toEqual({ ok: true, value: declaration });
    expect(received[0]?.url.toString()).toBe(`${serviceUrl}/api/account/sign-in-options?clientId=ocharo-web&locale=ru`);
  });

  it("asks for the instance's own declaration with no query, and reads omitted parts as empty", async () => {
    const { client, received } = fakeAccountApi({
      'GET /sign-in-options': () =>
        Response.json({
          data: {
            brandName: 'Identity',
            theme: 'platform',
            methods: [{ id: 'password', fields: [] }],
          },
        }),
    });

    const result = await client.getSignInOptions();

    expect(received[0]?.url.search).toBe('');
    expect(result).toEqual({
      ok: true,
      value: {
        brandName: 'Identity',
        theme: 'platform',
        backgroundUrl: null,
        isSignUpEnabled: false,
        isPersistenceOffered: false,
        locale: 'en',
        locales: [],
        methods: [{ id: 'password', channels: [], fields: [], passwordRules: null }],
        requirements: {
          isConfirmedEmailRequired: false,
          isConfirmedPhoneNumberRequired: false,
          isSecondFactorRequired: false,
        },
      },
    });
  });

  it.each([
    ['no methods', { brandName: 'Identity', theme: 'platform' }],
    ['a method without fields', { brandName: 'Identity', theme: 'platform', methods: [{ id: 'password' }] }],
    [
      'a rule without its message',
      {
        brandName: 'Identity',
        theme: 'platform',
        methods: [{ id: 'password', fields: [{ name: 'identifier', kind: 'text', rules: [{ code: 'required' }] }] }],
      },
    ],
  ])('refuses a declaration with %s', async (_case, data) => {
    const { client } = fakeAccountApi({ 'GET /sign-in-options': () => Response.json({ data }) });

    const result = await client.getSignInOptions();

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.failure.code).toBe('protocol');
  });

  it('keeps the problem of a refused request', async () => {
    const { client } = fakeAccountApi({ 'GET /sign-in-options': () => problem(503, 'Unavailable') });

    const result = await client.getSignInOptions();

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.failure.problem?.detail).toBe('Unavailable');
  });
});
