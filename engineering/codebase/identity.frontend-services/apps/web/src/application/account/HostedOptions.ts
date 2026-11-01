import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { inject, provide, type InjectionKey, type ShallowRef } from 'vue';
import type { SignInOptions } from '../../domain/account';

/** Holds the sign-in declaration the hosted layout loaded once, for the page in its card. */
export interface HostedOptions {
  /** The look, brand and methods to show; `null` while they load. */
  readonly options: Readonly<ShallowRef<SignInOptions | null>>;
  /** Why the declaration did not load, or `null`. */
  readonly failure: Readonly<ShallowRef<ApiFailure | null>>;
}

const hostedOptionsKey: InjectionKey<HostedOptions> = Symbol('hosted-options');

/** Offers the layout's declaration to every page below it. */
export function provideHostedOptions(hosted: HostedOptions): void {
  provide(hostedOptionsKey, hosted);
}

/** Reads the declaration the hosted layout above this page loaded. */
export function useHostedOptions(): HostedOptions {
  const hosted = inject(hostedOptionsKey, null);
  if (!hosted) throw new Error('A hosted page renders inside HostedLayout, which loads the sign-in declaration.');
  return hosted;
}
