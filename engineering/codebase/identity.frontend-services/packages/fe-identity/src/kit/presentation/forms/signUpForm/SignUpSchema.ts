import { object, Validator } from '@wow-two-beta/fe/foundation/validators';
import type { SignInRuleDto } from '../../../../contracts';
import { emailRule, passwordRule } from '../../../account/validation';

/** Holds what the sign-up form collects. */
interface SignUpValues {
  readonly email: string;
  readonly password: string;
}

function emailOf(value: unknown): string | null {
  const email = typeof value === 'object' && value !== null ? (value as { email?: unknown }).email : undefined;
  return typeof email === 'string' ? email : null;
}

/**
 * Builds the sign-up form's validator: a required, email-shaped email and a password that meets `passwordRules`. The
 * user name is the email, since the form sends none, so a `notEqual` rule compares the password with the email typed.
 */
export function signUpSchema(passwordRules: ReadonlyArray<SignInRuleDto> | undefined): Validator<SignUpValues> {
  return new Validator<SignUpValues>('sign-up', (value, path) => {
    const email = emailOf(value);
    return object({
      email: emailRule(),
      password: passwordRule(passwordRules, { userName: email, email }),
    }).parseAt(value, path);
  });
}
