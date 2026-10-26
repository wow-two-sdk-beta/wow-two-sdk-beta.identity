/** Represents what a forwarding app asked the hosted greeting page for. */
export interface HostedGreetingRequest {
  /** The name the page starts with. */
  readonly name: string;
  /** Where the page links back to, when the forwarding app gave a usable URL. */
  readonly returnUrl: string | null;
}
