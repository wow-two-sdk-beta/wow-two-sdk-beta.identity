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
  /** `404`: the person is not a member of the organization. */
  userNotMember: 'UserNotMember',
  /** `400`: the role is not one of the organization roles. */
  unknownOrganizationRole: 'UnknownOrganizationRole',
  /** `404`: no invitation of the organization has the id, or it was already used or withdrawn. */
  invitationNotFound: 'InvitationNotFound',
  /** `400`: the invitation names neither a valid phone number nor a valid email where one was given. */
  invalidInvitationIdentifier: 'InvalidInvitationIdentifier',
  /** `409`, or an invitation sign-up's `failureCode`: the invitation is past its end. */
  invitationExpired: 'InvitationExpired',
  /** `409`, or an invitation sign-up's `failureCode`: the invitation was already redeemed. */
  invitationAccepted: 'InvitationAccepted',
  /** `409`, or an invitation sign-up's `failureCode`: an owner or admin withdrew the invitation. */
  invitationRevoked: 'InvitationRevoked',
  /** An invitation sign-up's `failureCode`: the invitation names another phone number than the one shared. */
  invitationIdentifierMismatch: 'InvitationIdentifierMismatch',
  /** `400`: the sign-up asks for a name and none was given. */
  displayNameRequired: 'DisplayNameRequired',
  /** `400`: the name is longer than the service keeps. */
  displayNameTooLong: 'DisplayNameTooLong',
  /** `400`: the sign-up asks for a picture and none was given. */
  pictureRequired: 'PictureRequired',
  /** `400`: the picture is empty or larger than `pictureRules.maxBytes`. */
  pictureTooLarge: 'PictureTooLarge',
  /** `400`: the picture is not one of `pictureRules.acceptedTypes`, judged by its leading bytes. */
  pictureUnsupportedType: 'PictureUnsupportedType',
  /** `400`: a side of the picture is shorter than `pictureRules.minDimension`. */
  pictureTooSmall: 'PictureTooSmall',
  /** `400`: a side of the picture is longer than `pictureRules.maxDimension`. */
  pictureTooBig: 'PictureTooBig',
  /** `400`: the picture starts like an accepted type but does not decode. */
  pictureUnreadable: 'PictureUnreadable',
  /** `404`: the instance has no phone sign-in, so an invitation sign-up cannot verify a phone. */
  phoneSignInUnavailable: 'PhoneSignInUnavailable',
  /** `429`: too many phone verifications started in a short time. */
  phoneCodeRateLimited: 'PhoneCodeRateLimited',
  /** `404`: no invitation sign-up is bound to this browser, or it is gone. */
  signUpVerificationNotFound: 'SignUpVerificationNotFound',
  /** An invitation sign-up's `failureCode`: the shared phone number is outside the instance's allowed countries. */
  phoneCountryNotAllowed: 'PhoneCountryNotAllowed',
  /** `404`: no Telegram account change of the member's confirmed phone number waits for approval. */
  telegramChangeNotFound: 'TelegramChangeNotFound',
  /**
   * `403`: revoking a trusted contact, or changing the own phone, needs a session signed in with the account's own phone.
   * The slice 1 API table writes it `main_phone_required`; `isIdentityCode` reads either spelling.
   */
  mainPhoneRequired: 'MainPhoneRequired',
  /**
   * `409`: the person is the last trusted contact of an account with no phone of its own, so they cannot leave. The slice 1
   * API table writes it `last_trusted_contact`; `isIdentityCode` reads either spelling.
   */
  lastTrustedContact: 'LastTrustedContact',
} as const;
