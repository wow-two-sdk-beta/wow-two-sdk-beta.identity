import type { PhoneSignInAccountDto } from './PhoneSignInAccountDto';

/**
 * Mirrors the be contract's `accountChoice` of `POST phone-sign-in`: the code checked out for a trusted contact, who now
 * picks the account to sign in as. No session is set until `POST phone-sign-in/choose`.
 */
export interface PhoneSignInAccountChoiceDto {
  /** The token that `phone-sign-in/choose` spends. */
  readonly token: string;
  /** The accounts to pick from: the contact's own and each account trusting them. */
  readonly accounts: ReadonlyArray<PhoneSignInAccountDto>;
}
