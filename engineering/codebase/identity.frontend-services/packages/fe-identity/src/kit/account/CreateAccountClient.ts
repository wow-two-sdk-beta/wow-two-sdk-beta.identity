import { ApiFailureFactory, createApiClient, type ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions, type Result } from '@wow-two-beta/fe/foundation/results';
import {
  AccountRouteConstants,
  PasswordChangeConfirmationType,
  type AccountInfoDto,
  type ConfirmEmailApiRequest,
  type EmailAddressApiRequest,
  type InvitationPreviewDto,
  type InvitationStatus,
  type InviteSignUpApiRequest,
  type InviteSignUpStartedDto,
  InviteSignUpStatus,
  type InviteSignUpStatusDto,
  type PasswordChangeApiRequest,
  type PasswordChangeConfirmationApiRequest,
  type PasswordChangeConfirmationDto,
  type PasswordChangeLinkApiRequest,
  type PasswordChangeRequiredDto,
  type PhoneSignInApiRequest,
  type PhoneSignInSendApiRequest,
  type PhoneSignInSentDto,
  ProfileFieldMode,
  type ProfilePictureRulesDto,
  type ResetPasswordApiRequest,
  type SignInApiRequest,
  type SignInFieldDto,
  type SignInMethodDto,
  type SignInOptionsDto,
  type SignInRuleDto,
  type SignUpApiRequest,
  type SignedUpAccountDto,
  SignUpMode,
  type SignUpProfileDto,
  type TwoFactorSignInApiRequest,
} from '../../contracts';

/** Configures where the account API is reached. */
export interface AccountClientOptions {
  /** The origin serving the API: empty for the consumer's own backend (Form 1), the service's URL otherwise. */
  readonly baseUrl?: string;
  /**
   * Whether the browser sends the session cookie. Default `same-origin`, or `include` when `baseUrl` is set, which
   * the service's CORS policy must allow.
   */
  readonly credentials?: RequestCredentials;
  /** Replaces the global fetch, for tests. */
  readonly fetch?: typeof globalThis.fetch;
}

/** Describes a sign-in call beyond its body. */
export interface AccountSignInOptions {
  /** The OIDC client the sign-in serves, recorded by the sign-in log. */
  readonly clientId?: string | null;
  /** Cancels the request. */
  readonly signal?: AbortSignal;
}

/** Describes a sign-in declaration request. */
export interface SignInOptionsQuery {
  /** The OIDC client the sign-in serves, recorded by the sign-in log; the look stays the instance's. */
  readonly clientId?: string | null;
  /** The locale of the rule messages (`uz`, `ru`, `en`); the browser's `Accept-Language` when omitted. */
  readonly locale?: string | null;
  /** Cancels the request. */
  readonly signal?: AbortSignal;
}

/** Calls the account API over HTTP, signing in with cookie sessions. */
export interface AccountClient {
  /**
   * Reads the sign-in declaration: the hosted pages' look, whether sign-up is open, and each enabled method in layout
   * order with its fields and rules. The look is the instance's, whatever `clientId` names.
   */
  getSignInOptions(query?: SignInOptionsQuery): Promise<Result<SignInOptionsDto, ApiFailure>>;
  /**
   * Signs an account up while sign-up is open, or with an invitation's `invitationToken`; `403` while it is by invitation
   * only and no token came.
   */
  signUp(request: SignUpApiRequest, signal?: AbortSignal): Promise<Result<SignedUpAccountDto, ApiFailure>>;
  /** Reads what an invitation link offers: its organization, role and where it stands; `404` for a link naming none. */
  getInvitation(token: string, signal?: AbortSignal): Promise<Result<InvitationPreviewDto, ApiFailure>>;
  /** Adds the signed-in person to the invitation's organization with its role. */
  acceptInvitation(token: string, signal?: AbortSignal): Promise<Result<void, ApiFailure>>;
  /**
   * Starts an invitation sign-up with the profile the declaration's `signUpProfile` asks for, sent as
   * `multipart/form-data` with each part only when given. Answers the bot's start link, where sharing the contact proves
   * the phone, and binds the attempt to this browser by an HttpOnly cookie; `getInviteSignUpStatus` follows it. A refusal
   * carries its `identityCode`: `400` for a missing or unfit name or picture, `404` `InvitationNotFound` or
   * `PhoneSignInUnavailable`, `409` for an expired, used or withdrawn invitation, `429` `PhoneCodeRateLimited`.
   */
  startInviteSignUp(
    token: string,
    request: InviteSignUpApiRequest,
    options?: AccountSignInOptions,
  ): Promise<Result<InviteSignUpStartedDto, ApiFailure>>;
  /**
   * Reads where the invitation sign-up this browser started stands. The first `completed` answer with `isSignedIn` set
   * the session cookie; `404` `SignUpVerificationNotFound` when no attempt is bound to the browser.
   */
  getInviteSignUpStatus(options?: AccountSignInOptions): Promise<Result<InviteSignUpStatusDto, ApiFailure>>;
  /**
   * Signs in with an identifier and password: `null` once the session cookie is set, or the ticket of a required
   * password change, which sets no session. An account with a second factor answers `401` `TwoFactorRequired`
   * (`readTwoFactorChallenge`); the request then repeats with `twoFactorCode` or `recoveryCode`.
   */
  signIn(
    request: SignInApiRequest,
    options?: AccountSignInOptions,
  ): Promise<Result<PasswordChangeRequiredDto | null, ApiFailure>>;
  /** Completes a sign-in whose `TwoFactorRequired` challenge carried a ticket, and sets the session cookie. */
  signInWithSecondFactor(
    request: TwoFactorSignInApiRequest,
    options?: AccountSignInOptions,
  ): Promise<Result<void, ApiFailure>>;
  /** Confirms an email with the emailed token. */
  confirmEmail(request: ConfirmEmailApiRequest, signal?: AbortSignal): Promise<Result<void, ApiFailure>>;
  /** Sends the confirmation email again; succeeds whether or not the account exists. */
  resendConfirmationEmail(request: EmailAddressApiRequest, signal?: AbortSignal): Promise<Result<void, ApiFailure>>;
  /** Sends a password reset email; succeeds whether or not the account exists. */
  forgotPassword(request: EmailAddressApiRequest, signal?: AbortSignal): Promise<Result<void, ApiFailure>>;
  /** Sets a new password with the emailed reset token. */
  resetPassword(request: ResetPasswordApiRequest, signal?: AbortSignal): Promise<Result<void, ApiFailure>>;
  /** Reads the signed-in account; `401` when nobody is signed in. */
  getAccountInfo(signal?: AbortSignal): Promise<Result<AccountInfoDto, ApiFailure>>;
  /**
   * Starts a password change, signed in or with a required change's ticket: answers how it is confirmed, and sends the
   * code or link the setting asks for.
   */
  startPasswordChange(
    request?: PasswordChangeConfirmationApiRequest,
    signal?: AbortSignal,
  ): Promise<Result<PasswordChangeConfirmationDto, ApiFailure>>;
  /** Changes the password of the signed-in person, or of the one a required change's ticket names. */
  changePassword(request: PasswordChangeApiRequest, signal?: AbortSignal): Promise<Result<void, ApiFailure>>;
  /** Changes a password with the one-time token a change link carries. */
  changePasswordWithLink(
    request: PasswordChangeLinkApiRequest,
    signal?: AbortSignal,
  ): Promise<Result<void, ApiFailure>>;
  /** Sends a phone sign-in code, or answers the Telegram link that sends one once the contact is shared. */
  sendPhoneSignInCode(
    request: PhoneSignInSendApiRequest,
    signal?: AbortSignal,
  ): Promise<Result<PhoneSignInSentDto, ApiFailure>>;
  /**
   * Signs in with a phone and its code and sets the session cookie; a second factor answers `401` `TwoFactorRequired`
   * with the ticket `signInWithSecondFactor` takes.
   */
  signInWithPhone(request: PhoneSignInApiRequest, options?: AccountSignInOptions): Promise<Result<void, ApiFailure>>;
  /** Ends the cookie's session. */
  signOut(signal?: AbortSignal): Promise<Result<void, ApiFailure>>;
}

type Decoded<T> = Result<T, ApiFailure>;

const protocolFailure = () => ResultExtensions.fail(ApiFailureFactory.create('protocol'));

function record(value: unknown): Readonly<Record<string, unknown>> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function decodeSignedUp(value: unknown): Decoded<SignedUpAccountDto> {
  const body = record(value);
  if (body && typeof body.userId === 'string') {
    return ResultExtensions.ok({ userId: body.userId, isConfirmationSent: body.isConfirmationSent === true });
  }
  return protocolFailure();
}

const invitationStatuses: ReadonlyArray<InvitationStatus> = ['pending', 'expired', 'accepted', 'revoked'];

function decodeInvitation(value: unknown): Decoded<InvitationPreviewDto> {
  const body = record(value);
  const status = invitationStatuses.find((known) => known === body?.status);
  if (!body || typeof body.organizationName !== 'string' || typeof body.role !== 'string' || !status) {
    return protocolFailure();
  }
  return ResultExtensions.ok({
    organizationName: body.organizationName,
    role: body.role,
    status,
    phoneNumberHint: typeof body.phoneNumberHint === 'string' ? body.phoneNumberHint : null,
    emailHint: typeof body.emailHint === 'string' ? body.emailHint : null,
    expiresAt: typeof body.expiresAt === 'string' ? body.expiresAt : '',
  });
}

/** Whether `value` is a link the page may open: the bot's `https:` start link, or a `tg:` deep link. */
function isTelegramLink(value: unknown): value is string {
  if (typeof value !== 'string' || value === '') return false;
  try {
    return ['https:', 'tg:'].includes(new URL(value).protocol);
  } catch {
    return false;
  }
}

function decodeInviteSignUpStarted(value: unknown): Decoded<InviteSignUpStartedDto> {
  const body = record(value);
  if (!body || !isTelegramLink(body.telegramLinkUrl)) return protocolFailure();
  return ResultExtensions.ok({
    telegramLinkUrl: body.telegramLinkUrl,
    expiresAt: typeof body.expiresAt === 'string' ? body.expiresAt : '',
  });
}

const inviteSignUpStatuses = Object.values(InviteSignUpStatus);

function decodeInviteSignUpStatus(value: unknown): Decoded<InviteSignUpStatusDto> {
  const body = record(value);
  const status = inviteSignUpStatuses.find((known) => known === body?.status);
  if (!body || !status) return protocolFailure();
  return ResultExtensions.ok({
    status,
    failureCode: typeof body.failureCode === 'string' && body.failureCode !== '' ? body.failureCode : null,
    isSignedIn: body.isSignedIn === true,
  });
}

function decodeAccountInfo(value: unknown): Decoded<AccountInfoDto> {
  const body = record(value);
  if (body && typeof body.userId === 'string') {
    return ResultExtensions.ok({
      userId: body.userId,
      userName: typeof body.userName === 'string' ? body.userName : null,
      email: typeof body.email === 'string' ? body.email : null,
      isEmailConfirmed: body.isEmailConfirmed === true,
      isTwoFactorEnabled: body.isTwoFactorEnabled === true,
    });
  }
  return protocolFailure();
}

const confirmationTypes = Object.values(PasswordChangeConfirmationType);

/** Reads a confirmation type whatever its case; `undefined` for anything else. */
function confirmationOf(value: unknown): PasswordChangeConfirmationType | undefined {
  return typeof value === 'string'
    ? confirmationTypes.find((type) => type.toLowerCase() === value.toLowerCase())
    : undefined;
}

function decodeConfirmation(value: unknown): Decoded<PasswordChangeConfirmationDto> {
  const body = record(value);
  const confirmation = confirmationOf(body?.confirmation);
  if (body && confirmation) return ResultExtensions.ok({ confirmation, isSent: body.isSent === true });
  return protocolFailure();
}

function decodeCodeSent(value: unknown): Decoded<PhoneSignInSentDto> {
  const body = record(value);
  if (!body) return protocolFailure();
  const link =
    typeof body.telegramLinkUrl === 'string' && body.telegramLinkUrl.length > 0 ? body.telegramLinkUrl : null;
  return ResultExtensions.ok({ isCodeSent: body.isCodeSent === true, telegramLinkUrl: link });
}

function text(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

function strings(value: unknown): ReadonlyArray<string> | null {
  return Array.isArray(value) && value.every((item): item is string => typeof item === 'string') ? value : null;
}

/** Decodes each item of a list, or `null` when it is no list or any item fails. */
function list<T>(value: unknown, decode: (item: unknown) => T | null): ReadonlyArray<T> | null {
  if (!Array.isArray(value)) return null;
  const items = value.map(decode);
  return items.every((item): item is T => item !== null) ? items : null;
}

function decodeRule(value: unknown): SignInRuleDto | null {
  const body = record(value);
  const code = text(body?.code);
  const message = text(body?.message);
  return body && code && message !== null ? { code, params: record(body.params), message } : null;
}

function decodeField(value: unknown): SignInFieldDto | null {
  const body = record(value);
  const name = text(body?.name);
  const kind = text(body?.kind);
  const rules = list(body?.rules, decodeRule);
  return body && name && kind && rules ? { name, kind, countries: strings(body.countries), rules } : null;
}

function decodeMethod(value: unknown): SignInMethodDto | null {
  const body = record(value);
  const id = text(body?.id);
  const fields = list(body?.fields, decodeField);
  if (!body || !id || !fields) return null;
  const passwordRules = body.passwordRules == null ? null : list(body.passwordRules, decodeRule);
  if (body.passwordRules != null && passwordRules === null) return null;
  return { id, channels: strings(body.channels) ?? [], fields, passwordRules };
}

/** What a picture upload accepts when the declaration names nothing: the service's defaults. */
const defaultPictureRules: ProfilePictureRulesDto = {
  acceptedTypes: ['image/jpeg', 'image/png', 'image/webp'],
  maxBytes: 5_242_880,
  minDimension: 256,
  maxDimension: 4096,
};

const profileFieldModes = Object.values(ProfileFieldMode);

/** Reads a profile field mode whatever its case; `off` for anything else. */
function fieldMode(value: unknown): ProfileFieldMode {
  const mode = typeof value === 'string' ? value.toLowerCase() : null;
  return profileFieldModes.find((known) => known === mode) ?? ProfileFieldMode.Off;
}

function positive(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : fallback;
}

function decodePictureRules(value: unknown): ProfilePictureRulesDto {
  const body = record(value);
  const types = strings(body?.acceptedTypes)?.filter((type) => type !== '') ?? [];
  return {
    acceptedTypes: types.length > 0 ? types : [...defaultPictureRules.acceptedTypes],
    maxBytes: positive(body?.maxBytes, defaultPictureRules.maxBytes),
    minDimension: positive(body?.minDimension, defaultPictureRules.minDimension),
    maxDimension: positive(body?.maxDimension, defaultPictureRules.maxDimension),
  };
}

/** Reads what an invitation sign-up asks for; an absent or unknown part asks for nothing, with the default rules. */
function decodeSignUpProfile(value: unknown): SignUpProfileDto {
  const body = record(value);
  return {
    displayName: fieldMode(body?.displayName),
    picture: fieldMode(body?.picture),
    pictureRules: decodePictureRules(body?.pictureRules),
  };
}

function decodeSignInOptions(value: unknown): Decoded<SignInOptionsDto> {
  const body = record(value);
  const brandName = text(body?.brandName);
  const theme = text(body?.theme);
  const methods = list(body?.methods, decodeMethod);
  if (!body || !brandName || !theme || !methods) return protocolFailure();
  const requirements = record(body.requirements);
  return ResultExtensions.ok({
    brandName,
    theme,
    backgroundUrl: text(body.backgroundUrl) || null,
    signUpMode: body.signUpMode === SignUpMode.Open ? SignUpMode.Open : SignUpMode.Invite,
    isPersistenceOffered: body.isPersistenceOffered === true,
    isEmailEnabled: body.isEmailEnabled !== false,
    locale: text(body.locale) ?? 'en',
    locales: strings(body.locales) ?? [],
    methods,
    requirements: {
      isConfirmedEmailRequired: requirements?.isConfirmedEmailRequired === true,
      isConfirmedPhoneNumberRequired: requirements?.isConfirmedPhoneNumberRequired === true,
      isSecondFactorRequired: requirements?.isSecondFactorRequired === true,
    },
    signUpProfile: decodeSignUpProfile(body.signUpProfile),
  });
}

/** Builds an invitation sign-up's multipart body: each profile part only when given. */
function inviteSignUpBody(request: InviteSignUpApiRequest): FormData {
  const body = new FormData();
  if (request.displayName != null) body.append('displayName', request.displayName);
  if (request.picture != null) {
    const name = request.picture instanceof File && request.picture.name ? request.picture.name : 'picture';
    body.append('picture', request.picture, name);
  }
  return body;
}

/**
 * Reads a cookie sign-in's answer: `204` set the session; a body flagged `isPasswordChangeRequired`, enveloped or
 * bare, holds the change's ticket instead.
 */
function decodeSignedIn(text: string): Decoded<PasswordChangeRequiredDto | null> {
  if (text.trim() === '') return ResultExtensions.ok(null);
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return protocolFailure();
  }
  const outer = record(parsed);
  const body = record(outer?.data) ?? outer;
  if (body?.isPasswordChangeRequired !== true) return body ? ResultExtensions.ok(null) : protocolFailure();
  if (typeof body.ticket !== 'string' || body.ticket === '') return protocolFailure();
  return ResultExtensions.ok({
    isPasswordChangeRequired: true,
    confirmation: confirmationOf(body.confirmation) ?? PasswordChangeConfirmationType.None,
    ticket: body.ticket,
  });
}

/** Creates a client for the account API at `baseUrl` (same origin by default). */
export function createAccountClient(options: AccountClientOptions = {}): AccountClient {
  const baseUrl = options.baseUrl?.replace(/\/+$/, '') ?? '';
  const credentials = options.credentials ?? (baseUrl ? 'include' : undefined);
  const client = createApiClient({
    baseUrl: `${baseUrl}${AccountRouteConstants.base}`,
    ...(credentials !== undefined ? { credentials } : {}),
    ...(options.fetch !== undefined ? { fetch: options.fetch } : {}),
  });
  const signInQuery = (clientId?: string | null) => ({
    [AccountRouteConstants.useCookiesParameter]: true,
    ...(clientId ? { [AccountRouteConstants.clientIdParameter]: clientId } : {}),
  });
  const send = (path: string, body: unknown, signal?: AbortSignal) =>
    client.post(path, { body, response: 'empty', signal });
  const clientQuery = (clientId?: string | null) =>
    clientId ? { [AccountRouteConstants.clientIdParameter]: clientId } : {};

  return {
    getSignInOptions: ({ clientId, locale, signal } = {}) =>
      client.get(AccountRouteConstants.signInOptions, {
        query: {
          ...(clientId ? { [AccountRouteConstants.clientIdParameter]: clientId } : {}),
          ...(locale ? { [AccountRouteConstants.localeParameter]: locale } : {}),
        },
        signal,
        decode: decodeSignInOptions,
      }),
    signUp: (request, signal) =>
      client.post(AccountRouteConstants.signUp, { body: request, signal, decode: decodeSignedUp }),
    getInvitation: (token, signal) =>
      client.get(`${AccountRouteConstants.invitations}/${encodeURIComponent(token)}`, {
        signal,
        decode: decodeInvitation,
      }),
    acceptInvitation: (token, signal) =>
      client.post(`${AccountRouteConstants.invitations}/${encodeURIComponent(token)}/accept`, {
        response: 'empty',
        signal,
      }),
    startInviteSignUp: (token, request, { clientId, signal } = {}) =>
      client.post(
        `${AccountRouteConstants.invitations}/${encodeURIComponent(token)}${AccountRouteConstants.invitationSignUp}`,
        { query: clientQuery(clientId), body: inviteSignUpBody(request), signal, decode: decodeInviteSignUpStarted },
      ),
    getInviteSignUpStatus: ({ clientId, signal } = {}) =>
      client.get(AccountRouteConstants.invitationSignUpStatus, {
        query: clientQuery(clientId),
        signal,
        decode: decodeInviteSignUpStatus,
      }),
    async signIn(request, { clientId, signal } = {}) {
      const answered = await client.post(AccountRouteConstants.signIn, {
        query: signInQuery(clientId),
        body: request,
        response: 'text',
        signal,
      });
      return answered.ok ? decodeSignedIn(answered.value) : answered;
    },
    signInWithSecondFactor: (request, { clientId, signal } = {}) =>
      client.post(AccountRouteConstants.signInTwoFactor, {
        query: signInQuery(clientId),
        body: request,
        response: 'empty',
        signal,
      }),
    confirmEmail: (request, signal) =>
      client.get(AccountRouteConstants.confirmEmail, { query: { ...request }, response: 'empty', signal }),
    resendConfirmationEmail: (request, signal) => send(AccountRouteConstants.resendConfirmationEmail, request, signal),
    forgotPassword: (request, signal) => send(AccountRouteConstants.forgotPassword, request, signal),
    resetPassword: (request, signal) => send(AccountRouteConstants.resetPassword, request, signal),
    getAccountInfo: (signal) => client.get(AccountRouteConstants.manageInfo, { signal, decode: decodeAccountInfo }),
    startPasswordChange: (request, signal) =>
      client.post(AccountRouteConstants.managePasswordConfirmation, {
        body: request ?? {},
        signal,
        decode: decodeConfirmation,
      }),
    changePassword: (request, signal) => send(AccountRouteConstants.managePassword, request, signal),
    changePasswordWithLink: (request, signal) => send(AccountRouteConstants.passwordChange, request, signal),
    sendPhoneSignInCode: (request, signal) =>
      client.post(AccountRouteConstants.phoneSignInSend, { body: request, signal, decode: decodeCodeSent }),
    signInWithPhone: (request, { clientId, signal } = {}) =>
      client.post(AccountRouteConstants.phoneSignIn, {
        query: signInQuery(clientId),
        body: request,
        response: 'empty',
        signal,
      }),
    signOut: (signal) => client.post(AccountRouteConstants.signOut, { response: 'empty', signal }),
  };
}
