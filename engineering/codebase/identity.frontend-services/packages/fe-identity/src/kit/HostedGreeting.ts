import { GreetingRouteConstants } from '../contracts';

/** Describes a forward to the service's hosted greeting page. */
export interface HostedGreetingOptions {
  /** The running Identity service's origin. */
  readonly serviceUrl: string;
  /** The name the page starts with. */
  readonly name?: string;
  /** Where the page links back to when the user is done. */
  readonly returnUrl?: string;
}

/** Builds the URL of the service's hosted greeting page. */
export function hostedGreetingUrl(options: HostedGreetingOptions): string {
  const url = new URL(GreetingRouteConstants.hostedPage, options.serviceUrl);
  if (options.name) url.searchParams.set(GreetingRouteConstants.nameParameter, options.name);
  if (options.returnUrl) url.searchParams.set(GreetingRouteConstants.returnUrlParameter, options.returnUrl);
  return url.toString();
}

/** Forwards the browser to the hosted greeting page; the flow continues on the service and returns to `returnUrl`. */
export function forwardToHostedGreeting(
  options: HostedGreetingOptions,
  navigate: (url: string) => void = (url) => window.location.assign(url),
): void {
  navigate(hostedGreetingUrl(options));
}
