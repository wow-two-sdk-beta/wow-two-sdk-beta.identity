/** Represents what sent a person to a hosted page: where to continue, and the app the sign-in serves. */
export interface SignInRequest {
  /** The local path to continue to once done; `/` when none usable was given. */
  readonly returnUrl: string;
  /** The OIDC client the return URL authorizes, when it is an authorization request. */
  readonly clientId: string | null;
}
