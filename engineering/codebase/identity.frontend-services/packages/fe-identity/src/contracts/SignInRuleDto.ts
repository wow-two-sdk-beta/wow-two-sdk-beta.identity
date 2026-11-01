/**
 * Mirrors `SignInRuleDto` of the be contract: one rule a declared field meets, a fe sdk `ValidationCodes` code with its
 * operands and its message in the request's locale; the server's answer for the same rule says the same.
 */
export interface SignInRuleDto {
  /** The rule code, such as `required`, `min`, `length`, `pattern`, `notEqual` or `custom`. */
  readonly code: string;
  /** The rule's operands under the vocabulary's names, such as `{ min: 15, unit: 'characters' }`; `null` when none. */
  readonly params: Readonly<Record<string, unknown>> | null;
  /** The text a person reads when the value breaks the rule. */
  readonly message: string;
}
