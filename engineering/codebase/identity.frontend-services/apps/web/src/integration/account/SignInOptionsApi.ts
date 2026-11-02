import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions, type Result } from '@wow-two-beta/fe/foundation/results';
import {
  SignInMethodIdConstants,
  type SignInMethodDto,
  type SignInOptionsDto,
} from '@wow-two-beta/fe-identity/contracts';
import { SignInMethodId, type SignInMethod, type SignInOptions } from '../../domain/account';
import { accountApi } from './AccountApi';

/** Reads a declared method the pages can lay out; `null` for an id they do not know. */
function toMethod(method: SignInMethodDto): SignInMethod | null {
  switch (method.id) {
    case SignInMethodIdConstants.password:
      return { id: SignInMethodId.Password };
    case SignInMethodIdConstants.phoneCode:
      return {
        id: SignInMethodId.PhoneCode,
        countries: method.fields.find((field) => field.kind === 'phone')?.countries ?? [],
      };
    default:
      return null;
  }
}

/** Maps the service's declaration onto what the pages show. */
export function toSignInOptions(declaration: SignInOptionsDto): SignInOptions {
  return {
    brandName: declaration.brandName,
    theme: declaration.theme,
    backgroundImageUrl: declaration.backgroundUrl,
    isSignUpEnabled: declaration.isSignUpEnabled,
    isPersistenceOffered: declaration.isPersistenceOffered,
    methods: declaration.methods.map(toMethod).filter((method): method is SignInMethod => method !== null),
    passwordRules:
      declaration.methods.find((method) => method.id === SignInMethodIdConstants.password)?.passwordRules ?? [],
  };
}

/**
 * Provides the sign-in declaration from `GET /api/account/sign-in-options`: the instance's look, sign-up, and the enabled
 * methods in their declared order. `clientId` goes to the sign-in log, not to the look. The browser's language picks the locale.
 */
export const signInOptionsApi = {
  async get(clientId: string | null, signal?: AbortSignal): Promise<Result<SignInOptions, ApiFailure>> {
    const result = await accountApi.getSignInOptions({ clientId, signal });
    return result.ok ? ResultExtensions.ok(toSignInOptions(result.value)) : result;
  },
} as const;
