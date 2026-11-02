import { describe, expect, it } from 'vitest';
import type { SignInOptionsDto } from '../src/contracts';
import { fakeAccountApi, problem, serviceUrl } from './FakeAccountApi';

const declaration: SignInOptionsDto = {
  brandName: 'Ocharo',
  theme: 'ocharo',
  backgroundUrl: '/backgrounds/ocharo.jpg',
  signUpMode: 'open',
  isPersistenceOffered: true,
  isEmailEnabled: false,
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
  signUpProfile: {
    displayName: 'required',
    picture: 'optional',
    pictureRules: {
      acceptedTypes: ['image/jpeg', 'image/png'],
      maxBytes: 2_097_152,
      minDimension: 128,
      maxDimension: 2048,
    },
  },
};

/** What an invitation sign-up asks for when the declaration says nothing: no field, the service's default rules. */
const noProfile = {
  displayName: 'off',
  picture: 'off',
  pictureRules: {
    acceptedTypes: ['image/jpeg', 'image/png', 'image/webp'],
    maxBytes: 5_242_880,
    minDimension: 256,
    maxDimension: 4096,
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
        signUpMode: 'invite',
        isPersistenceOffered: false,
        isEmailEnabled: true,
        locale: 'en',
        locales: [],
        methods: [{ id: 'password', channels: [], fields: [], passwordRules: null }],
        requirements: {
          isConfirmedEmailRequired: false,
          isConfirmedPhoneNumberRequired: false,
          isSecondFactorRequired: false,
        },
        signUpProfile: noProfile,
      },
    });
  });

  it.each([
    ['unknown modes and no rules', { displayName: 'sometimes', picture: 3 }],
    ['rules out of range', { pictureRules: { acceptedTypes: [], maxBytes: -1, minDimension: '256', maxDimension: 0 } }],
    ['no profile object', 'required'],
  ])('reads a sign-up profile with %s as asking for nothing, with the default rules', async (_case, signUpProfile) => {
    const { client } = fakeAccountApi({
      'GET /sign-in-options': () => Response.json({ data: { ...declaration, signUpProfile } }),
    });

    const result = await client.getSignInOptions();

    expect(result.ok && result.value.signUpProfile).toEqual(noProfile);
  });

  it('reads the profile modes whatever their case, keeping each declared rule', async () => {
    const { client } = fakeAccountApi({
      'GET /sign-in-options': () =>
        Response.json({
          data: {
            ...declaration,
            signUpProfile: { displayName: 'Optional', picture: 'REQUIRED', pictureRules: { maxBytes: 1024 } },
          },
        }),
    });

    const result = await client.getSignInOptions();

    expect(result.ok && result.value.signUpProfile).toEqual({
      displayName: 'optional',
      picture: 'required',
      pictureRules: { ...noProfile.pictureRules, maxBytes: 1024 },
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
