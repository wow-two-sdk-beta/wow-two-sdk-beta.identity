/**
 * Mirrors `SignInRuleCodeConstants` of the be contract: the rule codes the declaration uses, the fe sdk
 * `ValidationCodes` names, and the fixed operand values some of them carry.
 */
export const SignInRuleCodeConstants = {
  /** A value must be present. */
  required: 'required',
  /** At least `min` of `unit`. */
  min: 'min',
  /** At most `max` of `unit`. */
  max: 'max',
  /** Exactly `length` of `unit`. */
  length: 'length',
  /** The value holds a match of `pattern`, a JavaScript regular expression with the `u` flag. */
  pattern: 'pattern',
  /** The value differs from each field of `fields`, ignoring case when `isCaseIgnored`. */
  notEqual: 'notEqual',
  /** A rule the vocabulary has no code for, named by `rule`, such as `distinctCharacters`. */
  custom: 'custom',
  /** The `unit` operand of a size rule counted in characters. */
  charactersUnit: 'characters',
  /** The `rule` operand of a `custom` rule needing at least `min` different characters. */
  distinctCharactersRule: 'distinctCharacters',
} as const;
