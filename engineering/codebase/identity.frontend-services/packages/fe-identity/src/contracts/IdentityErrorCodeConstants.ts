/**
 * Mirrors the `IdentityErrorCodeConstants` the kit and the admin panels act on: the code a refused account or
 * organization call carries in its problem's `identityCode` extension, beside the message.
 */
export const IdentityErrorCodeConstants = {
  /** The credentials were right but the email is not confirmed yet; a confirmation email can go out again. */
  emailConfirmationRequired: 'EmailConfirmationRequired',
  /** The credentials were right but the phone number is not confirmed yet. */
  phoneConfirmationRequired: 'PhoneConfirmationRequired',
  /** The credentials were right but the account may not sign in yet, for a reason other than a confirmation. */
  signInNotAllowed: 'SignInNotAllowed',
  /** The first factor was right; a second-factor code or a recovery code completes the sign-in. */
  twoFactorRequired: 'TwoFactorRequired',
  /** `404`: no organization has the id, or the signed-in person is outside it. */
  organizationNotFound: 'OrganizationNotFound',
  /** `403`: only an owner or admin of the organization manages its members and invitations. */
  organizationManagerRequired: 'OrganizationManagerRequired',
  /** `403`: only an owner invites, promotes, demotes or removes an owner. */
  organizationOwnerRequired: 'OrganizationOwnerRequired',
  /** `409`: the change would leave the organization without an owner. */
  lastOrganizationOwner: 'LastOrganizationOwner',
  /** `404`: the person is not a member of the organization. */
  userNotMember: 'UserNotMember',
  /** `400`: the role is not one of the organization roles. */
  unknownOrganizationRole: 'UnknownOrganizationRole',
  /** `404`: no invitation of the organization has the id, or it was already used or withdrawn. */
  invitationNotFound: 'InvitationNotFound',
  /** `400`: the invitation names neither a valid phone number nor a valid email where one was given. */
  invalidInvitationIdentifier: 'InvalidInvitationIdentifier',
} as const;
