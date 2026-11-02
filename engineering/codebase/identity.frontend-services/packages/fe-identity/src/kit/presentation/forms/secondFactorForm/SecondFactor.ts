/** Holds the second factor a person entered: a code from the authenticator or the delivered method, or a recovery code. */
export type SecondFactor = { readonly twoFactorCode: string } | { readonly recoveryCode: string };
