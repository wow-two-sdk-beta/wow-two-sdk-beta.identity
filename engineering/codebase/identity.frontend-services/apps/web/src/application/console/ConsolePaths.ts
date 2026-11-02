import { definePath } from '@wow-two-beta/fe/router';

/**
 * Holds the console's own pages: the overview at the root, an instance admin's organization creation, and each managed
 * organization's members and invitations. Each builds its link (`ConsolePaths.members({ organizationId })`) and carries
 * its route template as `.pattern`.
 */
export const ConsolePaths = {
  overview: definePath('/'),
  createOrganization: definePath('/organizations/new'),
  members: definePath('/organizations/:organizationId/members'),
  invitations: definePath('/organizations/:organizationId/invitations'),
} as const;
