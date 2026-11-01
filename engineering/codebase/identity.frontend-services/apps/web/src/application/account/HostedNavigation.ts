import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';

/** Leaves the hosted pages for a server route such as `/connect/authorize`, which only a full load reaches. */
export const hostedNavigation = {
  assign(url: string) {
    window.location.assign(url);
  },
} as const;

/** The paths of the hosted pages, which the router serves without a page load. */
const hostedPagePaths: ReadonlySet<string> = new Set([
  HostedPageRouteConstants.signIn,
  HostedPageRouteConstants.signUp,
  HostedPageRouteConstants.confirmEmail,
  HostedPageRouteConstants.forgotPassword,
  HostedPageRouteConstants.resetPassword,
  HostedPageRouteConstants.changePassword,
  HostedPageRouteConstants.accountSecurity,
]);

/**
 * Reads a link as a hosted page of `origin`: its path, query and fragment. Anything else, another origin or a server
 * route such as `/connect/authorize`, is `null` and stays a full load.
 */
export function readHostedPageLink(href: string, origin: string): string | null {
  let url: URL;
  try {
    url = new URL(href, origin);
  } catch {
    return null;
  }
  return url.origin === origin && hostedPagePaths.has(url.pathname) ? `${url.pathname}${url.search}${url.hash}` : null;
}
