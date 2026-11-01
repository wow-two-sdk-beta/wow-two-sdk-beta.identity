import { ApiFailureFactory, createApiClient, type ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions, type Result } from '@wow-two-beta/fe/foundation/results';
import {
  AccountRouteConstants,
  PasswordChangeConfirmationType,
  type AccountInfoDto,
  type ConfirmEmailApiRequest,
  type EmailAddressApiRequest,
  type PasswordChangeApiRequest,
  type PasswordChangeConfirmationApiRequest,
  type PasswordChangeConfirmationDto,
  type PasswordChangeLinkApiRequest,
  type PasswordChangeRequiredDto,
  type PhoneSignInApiRequest,
  type PhoneSignInSendApiRequest,
  type PhoneSignInSentDto,
  type RegisterAccountApiRequest,
  type RegisteredAccountDto,
  type ResetPasswordApiRequest,
  type SignInApiRequest,
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

/** Calls the account API over HTTP, signing in with cookie sessions. */
export interface AccountClient {
  /** Creates an account while sign-up is on; `403` while it is closed. */
  register(request: RegisterAccountApiRequest, signal?: AbortSignal): Promise<Result<RegisteredAccountDto, ApiFailure>>;
  /**
   * Signs in with a login and password: `null` once the session cookie is set, or the ticket of a required password
   * change, which sets no session.
   */
  signIn(
    request: SignInApiRequest,
    options?: AccountSignInOptions,
  ): Promise<Result<PasswordChangeRequiredDto | null, ApiFailure>>;
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
  /** Signs in with a phone and its code and sets the session cookie. */
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

function decodeRegistered(value: unknown): Decoded<RegisteredAccountDto> {
  const body = record(value);
  if (body && typeof body.userId === 'string') {
    return ResultExtensions.ok({ userId: body.userId, isConfirmationSent: body.isConfirmationSent === true });
  }
  return protocolFailure();
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

  return {
    register: (request, signal) =>
      client.post(AccountRouteConstants.register, { body: request, signal, decode: decodeRegistered }),
    async signIn(request, { clientId, signal } = {}) {
      const answered = await client.post(AccountRouteConstants.login, {
        query: signInQuery(clientId),
        body: request,
        response: 'text',
        signal,
      });
      return answered.ok ? decodeSignedIn(answered.value) : answered;
    },
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
    signOut: (signal) => client.post(AccountRouteConstants.logout, { response: 'empty', signal }),
  };
}
