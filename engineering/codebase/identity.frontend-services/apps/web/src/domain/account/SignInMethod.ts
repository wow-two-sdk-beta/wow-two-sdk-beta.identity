/** Names a sign-in method the hosted pages can lay out. */
export const SignInMethodId = {
  /** Refers to an identifier and password. */
  Password: 'password',
  /** Refers to a code sent to a phone, by SMS or the instance's Telegram bot. */
  PhoneCode: 'phone-code',
} as const;
export type SignInMethodId = (typeof SignInMethodId)[keyof typeof SignInMethodId];

/** Represents one enabled sign-in method and the settings its form needs. */
export type SignInMethod =
  | { readonly id: typeof SignInMethodId.Password }
  | {
      readonly id: typeof SignInMethodId.PhoneCode;
      /** The ISO codes of the countries a phone may come from, in display order. */
      readonly countries: ReadonlyArray<string>;
    };
