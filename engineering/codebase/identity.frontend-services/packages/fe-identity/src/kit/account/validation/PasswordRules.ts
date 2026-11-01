import {
  invalid,
  string,
  StringValidator,
  type ValidationIssue,
  type ValidationMessageCatalogue,
} from '@wow-two-beta/fe/foundation/validators';
import { SignInRuleCodeConstants, type SignInRuleDto } from '../../../contracts';
import { requiredText } from './FieldRules';

type RuleParams = Readonly<Record<string, unknown>> | null;

/** Holds the account values a `notEqual` rule compares a password with, under the field names the declaration uses. */
export type PasswordAccount = Readonly<Partial<Record<'userName' | 'email', string | null>>>;

/** Renders every rule the declaration carries from its own message, which is in the request's locale. */
export const passwordRuleMessages: ValidationMessageCatalogue = Object.freeze({
  required: (context) => context.message,
  min: (context) => context.message,
  max: (context) => context.message,
  length: (context) => context.message,
  notEqual: (context) => context.message,
});

const operand = (params: RuleParams, name: string): number | null => {
  const value = params?.[name];
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
};

/** Reads whether a size rule counts characters, as the server does; an operand-less unit counts them too. */
const isCounted = (params: RuleParams): boolean =>
  params?.unit === undefined || params.unit === SignInRuleCodeConstants.charactersUnit;

/** Leaves the fe sdk's own message in place for a rule whose message came empty. */
const declaredMessage = (rule: SignInRuleDto): string | undefined => (rule.message === '' ? undefined : rule.message);

function accountValue(account: PasswordAccount, field: string): string | null {
  if (!Object.hasOwn(account, field)) return null;
  const value = (account as Readonly<Record<string, string | null | undefined>>)[field];
  return value ? value : null;
}

/** Maps a `notEqual` rule onto the account values at hand; `null` when it names none of them. */
function toAccountCheck(rule: SignInRuleDto, account: PasswordAccount): StringValidator | null {
  const fields = rule.params?.fields;
  if (!Array.isArray(fields)) return null;
  const names = fields.filter((field): field is string => typeof field === 'string');
  const values = names.flatMap((name) => {
    const value = accountValue(account, name);
    return value === null ? [] : [value];
  });
  if (values.length === 0) return null;
  const fold = rule.params?.isCaseIgnored === true ? (text: string) => text.toLowerCase() : (text: string) => text;
  return string().refine(
    (password) => values.every((value) => fold(value) !== fold(password)),
    declaredMessage(rule) ?? 'must differ from the user name and email',
    SignInRuleCodeConstants.notEqual,
    { fields: names },
  );
}

/** Maps one declared rule onto an fe sdk validator; `null` for a code or operands it does not know. */
function toCheck(rule: SignInRuleDto, account: PasswordAccount): StringValidator | null {
  const { code, params } = rule;
  const message = declaredMessage(rule);
  switch (code) {
    case SignInRuleCodeConstants.min: {
      const limit = operand(params, 'min');
      return limit !== null && isCounted(params) ? string().min(limit, message) : null;
    }
    case SignInRuleCodeConstants.max: {
      const limit = operand(params, 'max');
      return limit !== null && isCounted(params) ? string().max(limit, message) : null;
    }
    case SignInRuleCodeConstants.length: {
      const exact = operand(params, 'length');
      return exact !== null && isCounted(params) ? string().length(exact, message) : null;
    }
    case SignInRuleCodeConstants.pattern: {
      const source = params?.pattern;
      if (typeof source !== 'string') return null;
      try {
        return string().pattern(new RegExp(source, 'u'), message);
      } catch {
        return null;
      }
    }
    case SignInRuleCodeConstants.custom: {
      const limit = operand(params, 'min');
      if (params?.rule !== SignInRuleCodeConstants.distinctCharactersRule || limit === null) return null;
      // Counted by UTF-16 code unit, as the server counts, so the browser never refuses what the server accepts.
      return string().refine(
        (password) => new Set(password.split('')).size >= limit,
        message ?? `must use at least ${limit} different characters`,
        SignInRuleCodeConstants.custom,
        { rule: SignInRuleCodeConstants.distinctCharactersRule, min: limit },
      );
    }
    case SignInRuleCodeConstants.notEqual:
      return toAccountCheck(rule, account);
    default:
      return null;
  }
}

/**
 * Maps the rules a declaration lists for a new password onto fe sdk validators: `min`, `max`, `length`, `pattern`,
 * `custom` (`distinctCharacters`) and `notEqual`, plus `required` for the message of the presence check. A code or
 * operand it does not know is skipped, since the server still checks everything. An empty value fails as `required`
 * alone, anything else reports every rule it breaks. Each failure carries the declaration's message, the fe sdk's own
 * when that is empty; pass {@link passwordRuleMessages} to the form so the catalogue does not replace it.
 *
 * @param rules The declaration's `passwordRules`; with none, only presence is checked.
 * @param account The account values a `notEqual` rule compares with; a rule naming none of them is skipped.
 */
export function passwordRule(
  rules: ReadonlyArray<SignInRuleDto> | null | undefined,
  account: PasswordAccount = {},
): StringValidator {
  const declared = rules ?? [];
  const checks = declared.flatMap((rule) => {
    const check = toCheck(rule, account);
    return check ? [check] : [];
  });
  const required = declared.find((rule) => rule.code === SignInRuleCodeConstants.required);
  const text = string();
  const all = new StringValidator('password', (value, path) => {
    const parsed = text.parseAt(value, path);
    if (!parsed.ok) return parsed;
    const issues: ValidationIssue[] = [];
    for (const check of checks) {
      const result = check.parseAt(parsed.value, path);
      if (!result.ok) issues.push(...result.failure.issues);
    }
    return issues.length === 0 ? parsed : invalid(issues);
  });
  return requiredText(all, required ? declaredMessage(required) : undefined);
}
