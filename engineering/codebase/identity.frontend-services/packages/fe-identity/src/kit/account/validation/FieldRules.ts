import { email, invalid, string, StringValidator } from '@wow-two-beta/fe/foundation/validators';
import { SignInRuleCodeConstants } from '../../../contracts';

/**
 * Wraps `inner` so the empty string fails as `required`, with the fe sdk's own message unless `message` replaces it; any
 * other value goes to `inner`. A form checks presence first, so the format or size rules never complain about nothing.
 */
export function requiredText(inner: StringValidator = string(), message?: string): StringValidator {
  return new StringValidator(inner.typeName, (value, path) =>
    value === ''
      ? invalid([{ path, message: message ?? 'is required', code: SignInRuleCodeConstants.required }])
      : inner.parseAt(value, path),
  );
}

/** Accepts an email-shaped value: required, then the fe sdk's pragmatic format check. */
export function emailRule(): StringValidator {
  return requiredText(email());
}
