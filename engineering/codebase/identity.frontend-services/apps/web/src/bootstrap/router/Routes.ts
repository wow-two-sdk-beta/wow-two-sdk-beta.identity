import { createAppRouter, lazyPage, RouterHistoryMode, type RouteConfig } from '@wow-two-beta/fe/router';
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import {
  hostedHref,
  hostedNavigation,
  readChangePasswordToken,
  readConfirmEmailRequest,
  readInvitationToken,
  readResetPasswordRequest,
  readSignInRequest,
} from '../../application/account';

const pages = HostedPageRouteConstants;

/**
 * Holds the route table: the console at the root, then the hosted pages as children of one layout route, which keeps
 * the instance's background, brand and card mounted while people move between them.
 */
export const routes: RouteConfig = [
  {
    path: '/',
    id: 'console',
    handle: { title: 'Console' },
    lazy: lazyPage(
      () => import('../../presentation/console/pages/ConsolePage.vue'),
      (route, router) => ({
        onSignInRequired: () => void router.replace(hostedHref(pages.signIn, route.fullPath)),
      }),
    ),
  },
  {
    id: 'hosted',
    lazy: lazyPage(
      () => import('../../presentation/common/layout/HostedLayout.vue'),
      (route, router) => ({
        request: readSignInRequest(route.query),
        onNavigate: (path: string) => void router.push(path),
      }),
    ),
    children: [
      {
        path: pages.signIn,
        id: 'sign-in',
        handle: { title: 'Sign in' },
        lazy: lazyPage(
          () => import('../../presentation/account/pages/SignInPage.vue'),
          (route) => ({
            request: readSignInRequest(route.query),
            onContinue: (url: string) => hostedNavigation.assign(url),
          }),
        ),
      },
      {
        path: pages.signUp,
        id: 'sign-up',
        handle: { title: 'Sign up' },
        lazy: lazyPage(
          () => import('../../presentation/account/pages/SignUpPage.vue'),
          (route) => ({
            request: readSignInRequest(route.query),
            invitationToken: readInvitationToken(route.query),
            selfHref: route.fullPath,
            onContinue: (url: string) => hostedNavigation.assign(url),
          }),
        ),
      },
      {
        path: pages.confirmEmail,
        id: 'confirm-email',
        handle: { title: 'Confirm your email' },
        lazy: lazyPage(
          () => import('../../presentation/account/pages/ConfirmEmailPage.vue'),
          (route) => ({
            request: readConfirmEmailRequest(route.query),
            returnUrl: readSignInRequest(route.query).returnUrl,
          }),
        ),
      },
      {
        path: pages.forgotPassword,
        id: 'forgot-password',
        handle: { title: 'Reset your password' },
        lazy: lazyPage(
          () => import('../../presentation/account/pages/ForgotPasswordPage.vue'),
          (route) => ({ request: readSignInRequest(route.query) }),
        ),
      },
      {
        path: pages.resetPassword,
        id: 'reset-password',
        handle: { title: 'Choose a new password' },
        lazy: lazyPage(
          () => import('../../presentation/account/pages/ResetPasswordPage.vue'),
          (route) => ({
            request: readResetPasswordRequest(route.query),
            returnUrl: readSignInRequest(route.query).returnUrl,
          }),
        ),
      },
      {
        path: pages.changePassword,
        id: 'change-password',
        handle: { title: 'Change your password' },
        lazy: lazyPage(
          () => import('../../presentation/account/pages/ChangePasswordPage.vue'),
          (route, router) => ({
            token: readChangePasswordToken(route.hash),
            returnUrl: readSignInRequest(route.query).returnUrl,
            onTokenRead: () => void router.replace({ path: route.path, query: route.query, hash: '' }),
          }),
        ),
      },
      {
        path: pages.accountSecurity,
        id: 'account-security',
        handle: { title: 'Security' },
        lazy: lazyPage(
          () => import('../../presentation/account/pages/AccountSecurityPage.vue'),
          (route, router) => ({
            onSignInRequired: () => void router.replace(hostedHref(pages.signIn, route.fullPath)),
          }),
        ),
      },
    ],
  },
];

/** Creates the app's router over `history`: the browser's in the app, memory in tests. */
export function createWebRouter(history: RouterHistoryMode = RouterHistoryMode.Browser) {
  return createAppRouter(routes, { titleSuffix: 'Identity', routePersistence: false, history });
}
