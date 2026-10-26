/** Mirrors `GreetingRouteConstants` of the be contract, plus the hosted page the service serves. */
export const GreetingRouteConstants = {
  /** The greetings resource, relative to the service or consumer origin. */
  greetings: '/api/greetings',
  /** The query parameter that carries the name. */
  nameParameter: 'name',
  /** The service's hosted greeting page, the target of the forward mode. */
  hostedPage: '/greet',
  /** The hosted page's query parameter that carries the URL to return to. */
  returnUrlParameter: 'return_url',
} as const;
