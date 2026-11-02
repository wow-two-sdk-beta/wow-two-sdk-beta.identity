import { createMessageResolver, type StringValidator } from '@wow-two-beta/fe/foundation/validators';
import { describe, expect, it } from 'vitest';
import type { SignInRuleDto } from '../src/contracts';
import { codeRule, passwordRule, passwordRuleMessages } from '../src/kit';
import { emailRule } from '../src/kit/account/validation';

/** The issues a validator reports for `value`, as `code: message` lines. */
function issues(validator: StringValidator, value: unknown) {
  const result = validator.validate(value);
  return result.ok ? [] : result.failure.issues.map((issue) => `${issue.code}: ${issue.message}`);
}

const rule = (code: string, params: Record<string, unknown> | null, message: string): SignInRuleDto => ({
  code,
  params,
  message,
});

const minimum = rule('min', { min: 15, unit: 'characters' }, 'Passwords must be at least 15 characters.');
const maximum = rule('max', { max: 20, unit: 'characters' }, 'Passwords must be at most 20 characters.');
const exact = rule('length', { length: 6, unit: 'characters' }, 'Use exactly 6 characters.');
const digit = rule('pattern', { pattern: '[0-9]' }, 'Passwords must contain a digit.');
const lowercase = rule('pattern', { pattern: '\\p{Ll}' }, 'Passwords must contain a lowercase letter.');
const uppercase = rule('pattern', { pattern: '\\p{Lu}' }, 'Passwords must contain an uppercase letter.');
const symbol = rule('pattern', { pattern: '[^\\p{L}\\p{Nd}]' }, 'Passwords must contain a symbol.');
const distinct = rule(
  'custom',
  { rule: 'distinctCharacters', min: 5 },
  'Passwords must use at least 5 different characters.',
);
const differs = rule(
  'notEqual',
  { fields: ['userName', 'email'], isCaseIgnored: true },
  'Passwords must differ from the user name and email.',
);

describe('password rule from the declaration', () => {
  it('maps min and max to the size of the password in characters', () => {
    const validator = passwordRule([minimum, maximum]);

    expect(issues(validator, 'a'.repeat(15))).toEqual([]);
    expect(issues(validator, 'a'.repeat(14))).toEqual(['min: Passwords must be at least 15 characters.']);
    expect(issues(validator, 'a'.repeat(20))).toEqual([]);
    expect(issues(validator, 'a'.repeat(21))).toEqual(['max: Passwords must be at most 20 characters.']);
  });

  it('maps length to an exact size', () => {
    const validator = passwordRule([exact]);

    expect(issues(validator, 'abcdef')).toEqual([]);
    expect(issues(validator, 'abcde')).toEqual(['length: Use exactly 6 characters.']);
    expect(issues(validator, 'abcdefg')).toEqual(['length: Use exactly 6 characters.']);
  });

  it('maps pattern to the unicode regular expression the server declares', () => {
    const validator = passwordRule([digit, lowercase, uppercase, symbol]);

    expect(issues(validator, 'Пароль-7')).toEqual([]);
    expect(issues(validator, 'ПАРОЛЬ-7')).toEqual(['pattern: Passwords must contain a lowercase letter.']);
    expect(issues(validator, 'пароль-7')).toEqual(['pattern: Passwords must contain an uppercase letter.']);
    expect(issues(validator, 'Пароль-')).toEqual(['pattern: Passwords must contain a digit.']);
    expect(issues(validator, 'Пароль7')).toEqual(['pattern: Passwords must contain a symbol.']);
  });

  it('maps the distinctCharacters custom rule to the count of different characters', () => {
    const validator = passwordRule([distinct]);

    expect(issues(validator, 'abcde')).toEqual([]);
    expect(issues(validator, 'aabbaabb')).toEqual(['custom: Passwords must use at least 5 different characters.']);
    expect(issues(validator, 'aAbBc')).toEqual([]);
  });

  it('maps notEqual to the account values at hand, ignoring case when the rule does', () => {
    const account = { userName: 'ann', email: 'Ann@Example.test' };

    expect(issues(passwordRule([differs], account), 'ann@example.TEST')).toEqual([
      'notEqual: Passwords must differ from the user name and email.',
    ]);
    expect(issues(passwordRule([differs], account), 'ANN')).toEqual([
      'notEqual: Passwords must differ from the user name and email.',
    ]);
    expect(issues(passwordRule([differs], account), 'another-secret')).toEqual([]);
    expect(issues(passwordRule([{ ...differs, params: { fields: ['email'] } }], account), 'ann')).toEqual([]);
    expect(
      issues(
        passwordRule([{ ...differs, params: { fields: ['email'], isCaseIgnored: false } }], account),
        'ann@example.test',
      ),
    ).toEqual([]);
  });

  it('skips notEqual when the account has no value to compare with', () => {
    expect(issues(passwordRule([differs]), 'ann')).toEqual([]);
    expect(issues(passwordRule([differs], { email: null, userName: '' }), 'ann')).toEqual([]);
  });

  it('reports every broken rule at once, in the declared order', () => {
    const validator = passwordRule([minimum, digit, uppercase, distinct]);

    expect(issues(validator, 'aaa')).toEqual([
      'min: Passwords must be at least 15 characters.',
      'pattern: Passwords must contain a digit.',
      'pattern: Passwords must contain an uppercase letter.',
      'custom: Passwords must use at least 5 different characters.',
    ]);
  });

  it('skips a code or operands it does not know, since the server still checks them', () => {
    const unknown = [
      rule('creditCard', null, 'No card.'),
      rule('min', null, 'No operand.'),
      rule('min', { min: 15, unit: 'items' }, 'Items, not characters.'),
      rule('pattern', { pattern: '(' }, 'Not a regular expression.'),
      rule('pattern', null, 'No pattern.'),
      rule('custom', { rule: 'breachedPassword' }, 'A rule only the server knows.'),
      rule('custom', { rule: 'distinctCharacters' }, 'No count.'),
      rule('notEqual', { fields: 'email' }, 'Fields not a list.'),
    ];

    expect(issues(passwordRule(unknown, { email: 'x' }), 'x')).toEqual([]);
  });

  it('fails an empty value as required alone, and checks only presence without rules', () => {
    expect(issues(passwordRule([minimum, digit]), '')).toEqual(['required: is required']);
    expect(issues(passwordRule([]), '')).toEqual(['required: is required']);
    expect(issues(passwordRule(undefined), '')).toEqual(['required: is required']);
    expect(issues(passwordRule(null), 'x')).toEqual([]);
  });

  it('takes the message of a declared required rule', () => {
    const validator = passwordRule([rule('required', null, 'Enter a password.'), minimum]);

    expect(issues(validator, '')).toEqual(['required: Enter a password.']);
  });

  it('falls back to the fe sdk message when the declaration carries none', () => {
    const validator = passwordRule(
      [
        { ...minimum, message: '' },
        { ...maximum, message: '' },
        { ...exact, message: '' },
        { ...differs, message: '' },
      ],
      { email: 'abc' },
    );

    expect(issues(validator, 'abc')).toEqual([
      'min: must be at least 15 characters',
      'length: must be exactly 6 characters',
      'notEqual: must differ from the user name and email',
    ]);
  });

  it('rejects a value that is no string', () => {
    expect(issues(passwordRule([minimum]), 42)).toEqual(['type: expected string, received number']);
  });
});

describe('password rule messages', () => {
  const resolve = createMessageResolver(passwordRuleMessages);
  const fallback = createMessageResolver();

  it('render the declaration message in its locale, not the fe sdk English one', () => {
    const russian = rule('min', { min: 15, unit: 'characters' }, 'Пароль должен содержать не менее 15 символов.');
    const result = passwordRule([russian]).validate('short');
    const issue = result.ok ? null : result.failure.issues[0];
    if (!issue) throw new Error('Expected a failure.');

    expect(resolve(issue, 'password')).toBe('Пароль должен содержать не менее 15 символов.');
    expect(fallback(issue, 'password')).toBe('must be at least 15 characters');
  });

  it('render the fe sdk message for the rules the declaration does not carry', () => {
    const result = emailRule().validate('sdfsdf');
    const issue = result.ok ? null : result.failure.issues[0];
    if (!issue) throw new Error('Expected a failure.');

    expect(resolve(issue, 'email')).toBe('must be a valid email address');
  });
});

describe('email rule', () => {
  it('accepts an email address', () => {
    expect(issues(emailRule(), 'ann@example.test')).toEqual([]);
  });

  it.each(['sdfsdf', 'ann@', '@example.test', 'ann@example', 'ann @example.test'])('refuses %s', (value) => {
    expect(issues(emailRule(), value)).toEqual(['email: must be a valid email address']);
  });

  it('fails an empty value as required', () => {
    expect(issues(emailRule(), '')).toEqual(['required: is required']);
  });
});

describe('code rule from the declaration', () => {
  const required = rule('required', null, 'Enter the code.');
  const sixDigits = rule('length', { length: 6, unit: 'characters' }, 'The code has 6 characters.');

  it('checks the declared length of the code as it is sent, without the spaces around it', () => {
    const validator = codeRule([required, sixDigits]);

    expect(issues(validator, ' 123456 ')).toEqual([]);
    expect(issues(validator, '12345')).toEqual(['length: The code has 6 characters.']);
    expect(issues(validator, '   ')).toEqual(['required: Enter the code.']);
  });

  it('checks only presence without rules', () => {
    expect(issues(codeRule(undefined), '1')).toEqual([]);
    expect(issues(codeRule(undefined), '')).toEqual(['required: is required']);
  });
});
