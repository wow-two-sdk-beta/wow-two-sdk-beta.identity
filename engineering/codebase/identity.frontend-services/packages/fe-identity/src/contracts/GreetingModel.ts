/** Mirrors `GreetingModel` of the be contract: a greeting the capability served. */
export interface GreetingModel {
  /** The greeting text. */
  readonly message: string;
  /** When the greeting was served, as an ISO 8601 timestamp (UTC). */
  readonly greetedAtUtc: string;
}
