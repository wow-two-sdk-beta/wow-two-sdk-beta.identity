import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import type { Result } from '@wow-two-beta/fe/foundation/results';
import { inject, provide, type InjectionKey, type ShallowRef } from 'vue';
import type { SignInOptions } from '../../domain/account';
import type { ConsoleOrganization, ConsoleSession, NewConsoleOrganization } from '../../domain/console/ConsoleSession';
import type { SystemStatus } from '../../domain/system/SystemStatus';

/** Holds what the console layout loaded once, for the page in its main region. */
export interface ConsoleContext {
  /** The signed-in person; the layout renders a page only once it resolved. */
  readonly session: Readonly<ShallowRef<ConsoleSession | null>>;
  /** The instance's sign-in declaration, for its brand and the countries an invited phone may come from; `null` while it loads. */
  readonly options: Readonly<ShallowRef<SignInOptions | null>>;
  /** The service's status, for its running version; `null` until it answers. */
  readonly status: Readonly<ShallowRef<SystemStatus | null>>;
  /** Creates an instance admin's organization, which the session lists once created. */
  readonly createOrganization: (
    request: NewConsoleOrganization,
    signal: AbortSignal,
  ) => Promise<Result<ConsoleOrganization, ApiFailure>>;
}

const consoleContextKey: InjectionKey<ConsoleContext> = Symbol('console-context');

/** Offers the layout's session and declaration to every console page below it. */
export function provideConsoleContext(context: ConsoleContext): void {
  provide(consoleContextKey, context);
}

/** Reads the session and declaration the console layout above this page loaded. */
export function useConsoleContext(): ConsoleContext {
  const context = inject(consoleContextKey, null);
  if (!context) throw new Error('A console page renders inside ConsoleLayout, which loads the session.');
  return context;
}
