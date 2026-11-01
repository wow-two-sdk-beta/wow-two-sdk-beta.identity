import type { SignInRuleDto } from './SignInRuleDto';

/** Mirrors `SignInFieldDto` of the be contract: one field of a method's form, named as its request names it. */
export interface SignInFieldDto {
  /** The request property the field fills, such as `identifier`, `password`, `phone` or `code`. */
  readonly name: string;
  /** The input the field takes: `text`, `password`, `phone` or `pin`. */
  readonly kind: string;
  /** A phone field's allowed ISO country codes, every country when empty; `null` for other kinds. */
  readonly countries: ReadonlyArray<string> | null;
  /** The rules in the order a form checks them. */
  readonly rules: ReadonlyArray<SignInRuleDto>;
}
