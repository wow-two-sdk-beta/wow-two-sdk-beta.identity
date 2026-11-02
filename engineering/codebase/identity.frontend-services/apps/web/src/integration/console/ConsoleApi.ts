import { ApiFailureFactory, createApiClient } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import type { ConsoleSession } from '../../domain/console/ConsoleSession';

const client = createApiClient();

/** Provides the console's same-origin session request and wire validation: `401` signed out, `403` without an instance role. */
export const consoleApi = {
  getSession(signal: AbortSignal) {
    return client.get<ConsoleSession>('/api/console/session', {
      signal,
      decode: (value) => {
        if (
          value !== null &&
          typeof value === 'object' &&
          'userId' in value &&
          typeof value.userId === 'string' &&
          'name' in value &&
          typeof value.name === 'string' &&
          'roles' in value &&
          Array.isArray(value.roles) &&
          value.roles.every((role) => typeof role === 'string')
        ) {
          return ResultExtensions.ok({ userId: value.userId, name: value.name, roles: value.roles as string[] });
        }
        return ResultExtensions.fail(ApiFailureFactory.create('protocol'));
      },
    });
  },
} as const;
