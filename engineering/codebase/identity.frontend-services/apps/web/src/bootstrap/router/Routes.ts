import { createAppRouter, lazyPage, lazyRoute, RouterHistoryMode, type RouteConfig } from '@wow-two-beta/fe/router';
import { GreetingRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { readHostedGreetingRequest } from '../../application/greetings/ReadHostedGreetingRequest';

/** Holds the route table: the console at the root, then one route per hosted page a consumer forwards to. */
export const routes: RouteConfig = [
  {
    path: '/',
    id: 'console',
    handle: { title: 'Console' },
    lazy: lazyRoute(() => import('../../presentation/console/pages/ConsolePage.vue')),
  },
  {
    path: GreetingRouteConstants.hostedPage,
    id: 'hosted-greeting',
    handle: { title: 'Greetings' },
    lazy: lazyPage(
      () => import('../../presentation/greetings/pages/HostedGreetingPage.vue'),
      (route) => ({ request: readHostedGreetingRequest(route.query) }),
    ),
  },
];

/** Creates the app's router over `history`: the browser's in the app, memory in tests. */
export function createWebRouter(history: RouterHistoryMode = RouterHistoryMode.Browser) {
  return createAppRouter(routes, { titleSuffix: 'Identity', routePersistence: false, history });
}
