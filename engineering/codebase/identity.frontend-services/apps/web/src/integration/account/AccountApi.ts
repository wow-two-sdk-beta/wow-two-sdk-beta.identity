import { createAccountClient } from '@wow-two-beta/fe-identity/kit';

/** Provides the same-origin account API the hosted pages reach beyond the kit's own forms. */
export const accountApi = createAccountClient();
