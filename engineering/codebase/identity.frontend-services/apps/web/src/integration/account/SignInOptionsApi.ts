import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions, type Result } from '@wow-two-beta/fe/foundation/results';
import { SignInMethodId, type SignInOptions } from '../../domain/account';

/** Holds what an app overrides of the instance's look. */
interface AppSignInOptions {
  readonly theme?: string;
  readonly backgroundImageUrl?: string | null;
}

/**
 * The instance's declaration until `GET /api/account/sign-in-options` exists: password first, then a phone code
 * from Uzbekistan, on the `platform` theme. Swapping in the endpoint changes this file only.
 */
const InstanceOptions: SignInOptions = {
  brandName: 'Identity',
  theme: 'platform',
  backgroundImageUrl: null,
  isSignUpEnabled: true,
  methods: [{ id: SignInMethodId.Password }, { id: SignInMethodId.PhoneCode, countries: ['UZ'] }],
};

/** The registered apps that name their own look, by OIDC client id; Ocharo's apps take the `ocharo` theme. */
const AppOptions: Readonly<Record<string, AppSignInOptions>> = {};

/** Provides the sign-in declaration for the instance, narrowed to the app a sign-in serves. */
export const signInOptionsApi = {
  async get(clientId: string | null, signal?: AbortSignal): Promise<Result<SignInOptions, ApiFailure>> {
    void signal;
    const app = clientId && Object.hasOwn(AppOptions, clientId) ? AppOptions[clientId] : undefined;
    return ResultExtensions.ok({ ...InstanceOptions, ...app });
  },
} as const;
