import { StringValidator } from '@wow-two-beta/fe/foundation/validators';
import type { SignInRuleDto } from '../../../contracts';
import { declaredRule } from './PasswordRules';

/**
 * Maps the rules a declaration lists for a one-time code field, such as the phone code's `required` and `length`, onto
 * fe sdk validators the way `passwordRule` maps a password's, checking the code as it is sent: without the spaces
 * around it. A form that blocks a code of the wrong length spends none of its attempts; pass `passwordRuleMessages` to
 * the form so each failure keeps the declaration's message.
 *
 * @param rules The declared field's `rules`; with none, only presence is checked.
 */
export function codeRule(rules: ReadonlyArray<SignInRuleDto> | null | undefined): StringValidator {
  const inner = declaredRule(rules, {}, 'code');
  return new StringValidator('code', (value, path) =>
    inner.parseAt(typeof value === 'string' ? value.trim() : value, path),
  );
}
