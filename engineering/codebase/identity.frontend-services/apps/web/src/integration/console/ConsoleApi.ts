import { ApiFailureFactory, createApiClient } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import type { ConsoleOrganization, ConsoleSession, NewConsoleOrganization } from '../../domain/console/ConsoleSession';

const client = createApiClient();

function text(value: unknown, key: string): string | null {
  return value !== null &&
    typeof value === 'object' &&
    key in value &&
    typeof (value as Record<string, unknown>)[key] === 'string'
    ? ((value as Record<string, unknown>)[key] as string)
    : null;
}

function decodeOrganization(value: unknown): ConsoleOrganization | null {
  const [id, name, slug, role] = [text(value, 'id'), text(value, 'name'), text(value, 'slug'), text(value, 'role')];
  return id && name && slug && role ? { id, name, slug, role } : null;
}

/**
 * Provides the console's same-origin requests and wire validation: the session (`401` signed out, `403` for a person
 * with neither an instance role nor an organization they own or administer) and an instance admin's organization
 * creation (`403` without `admin`; `400` or `409` coded in the problem's `identityCode`).
 */
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
          const listed = 'organizations' in value && Array.isArray(value.organizations) ? value.organizations : [];
          const organizations = listed.map(decodeOrganization);
          if (organizations.every((organization) => organization !== null)) {
            return ResultExtensions.ok({
              userId: value.userId,
              name: value.name,
              roles: value.roles as string[],
              organizations: organizations as ConsoleOrganization[],
            });
          }
        }
        return ResultExtensions.fail(ApiFailureFactory.create('protocol'));
      },
    });
  },

  createOrganization(request: NewConsoleOrganization, signal?: AbortSignal) {
    return client.post<ConsoleOrganization>('/api/console/organizations', {
      body: request,
      signal,
      decode: (value) => {
        const organization = decodeOrganization(value);
        return organization
          ? ResultExtensions.ok(organization)
          : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
      },
    });
  },
} as const;
