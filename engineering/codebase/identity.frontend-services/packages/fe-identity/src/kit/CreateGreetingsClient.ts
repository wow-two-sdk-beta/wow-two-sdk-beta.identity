import { ApiFailureFactory, createApiClient, type ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions, type Result } from '@wow-two-beta/fe/foundation/results';
import { GreetingRouteConstants, type GreetingModel } from '../contracts';

/** Configures where the greetings contract is reached. */
export interface GreetingsClientOptions {
  /** The origin serving the contract: empty for the consumer's own backend (Form 1), the service's URL otherwise. */
  readonly baseUrl?: string;
  /** Whether the browser sends cookies cross-origin; the service's CORS policy must allow it. */
  readonly credentials?: RequestCredentials;
  /** Replaces the global fetch, for tests. */
  readonly fetch?: typeof globalThis.fetch;
}

/** Calls the greetings contract over HTTP. */
export interface GreetingsClient {
  /** Greets `name`, or the world when it is blank. */
  greet(name: string, signal?: AbortSignal): Promise<Result<GreetingModel, ApiFailure>>;
}

function decodeGreeting(value: unknown) {
  if (
    value !== null &&
    typeof value === 'object' &&
    'message' in value &&
    typeof value.message === 'string' &&
    'greetedAtUtc' in value &&
    typeof value.greetedAtUtc === 'string'
  ) {
    return ResultExtensions.ok<GreetingModel>({ message: value.message, greetedAtUtc: value.greetedAtUtc });
  }
  return ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

/** Creates a client for the greetings contract at `baseUrl` (same origin by default). */
export function createGreetingsClient(options: GreetingsClientOptions = {}): GreetingsClient {
  const client = createApiClient({
    baseUrl: options.baseUrl?.replace(/\/+$/, '') ?? '',
    ...(options.credentials !== undefined ? { credentials: options.credentials } : {}),
    ...(options.fetch !== undefined ? { fetch: options.fetch } : {}),
  });

  return {
    greet(name, signal) {
      return client.get<GreetingModel>(GreetingRouteConstants.greetings, {
        query: { [GreetingRouteConstants.nameParameter]: name },
        ...(signal !== undefined ? { signal } : {}),
        decode: decodeGreeting,
      });
    },
  };
}
