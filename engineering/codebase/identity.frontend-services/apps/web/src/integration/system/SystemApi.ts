import { ApiFailureFactory, createApiClient } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import type { SystemStatus } from '../../domain/system/SystemStatus';

const client = createApiClient();

/** Provides the same-origin status request and wire validation. */
export const systemApi = {
  getStatus(signal: AbortSignal) {
    return client.get<SystemStatus>('/api/system/status', {
      signal,
      decode: (value) => {
        if (
          value !== null &&
          typeof value === 'object' &&
          'service' in value &&
          typeof value.service === 'string' &&
          'version' in value &&
          typeof value.version === 'string'
        ) {
          return ResultExtensions.ok({ service: value.service, version: value.version });
        }
        return ResultExtensions.fail(ApiFailureFactory.create('protocol'));
      },
    });
  },
} as const;
