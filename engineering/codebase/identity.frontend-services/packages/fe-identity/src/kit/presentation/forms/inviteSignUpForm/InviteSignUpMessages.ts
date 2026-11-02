import { IdentityErrorCodeConstants, InviteSignUpStatus, type ProfilePictureRulesDto } from '../../../../contracts';
import type { AccountText } from '../../../account/hooks';
import { displayNameMaxLength } from '../../../account/validation';

const codes = IdentityErrorCodeConstants;

/** The names people know the accepted picture types by. */
const typeNames: Readonly<Record<string, string>> = {
  'image/jpeg': 'JPEG',
  'image/png': 'PNG',
  'image/webp': 'WebP',
  'image/gif': 'GIF',
  'image/avif': 'AVIF',
  'image/heic': 'HEIC',
};

/** Writes a byte count as people read file sizes: `5 MB`, `512 KB`. */
export function byteSizeText(bytes: number): string {
  const megabytes = bytes / 1_048_576;
  return megabytes >= 1 ? `${Number(megabytes.toFixed(1))} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

/** Holds the invitation sign-up's texts, in the consumer's locale where it gives them. */
export interface InviteSignUpMessages {
  /** The message of a profile failure, the client's or the service's, by its identity code. */
  readonly field: (code: string) => string;
  /** Why an attempt ended: a poll's `failureCode`, `SignUpVerificationNotFound`, or `expired` for a timed-out one. */
  readonly outcome: (code: string | null) => string;
  /** What a picture may be, under its field. */
  readonly pictureHint: () => string;
}

/** Builds the invitation sign-up's texts over the kit's resolver and the declared picture rules, read on each call. */
export function inviteSignUpMessages(t: AccountText, rules: () => ProfilePictureRulesDto): InviteSignUpMessages {
  const types = () => {
    const names = [
      ...new Set(rules().acceptedTypes.map((type) => typeNames[type.toLowerCase()] ?? type.replace(/^image\//i, ''))),
    ];
    const last = names.pop() ?? '';
    return names.length > 0 ? `${names.join(', ')} ${t('InviteSignUpForm.or', 'or')} ${last}` : last;
  };

  const field = (code: string): string => {
    switch (code) {
      case codes.displayNameRequired:
        return t('InviteSignUpForm.displayNameRequired', 'Enter your name.');
      case codes.displayNameTooLong:
        return t('InviteSignUpForm.displayNameTooLong', 'Use at most {max} characters.', { max: displayNameMaxLength });
      case codes.pictureRequired:
        return t('InviteSignUpForm.pictureRequired', 'Choose a picture.');
      case codes.pictureTooLarge:
        return t('InviteSignUpForm.pictureTooLarge', 'Choose a picture of at most {size}.', {
          size: byteSizeText(rules().maxBytes),
        });
      case codes.pictureUnsupportedType:
        return t('InviteSignUpForm.pictureUnsupportedType', 'Choose a {types} picture.', { types: types() });
      case codes.pictureTooSmall:
        return t('InviteSignUpForm.pictureTooSmall', 'Choose a picture at least {min} pixels on each side.', {
          min: rules().minDimension,
        });
      case codes.pictureTooBig:
        return t('InviteSignUpForm.pictureTooBig', 'Choose a picture at most {max} pixels on each side.', {
          max: rules().maxDimension,
        });
      case codes.pictureUnreadable:
        return t('InviteSignUpForm.pictureUnreadable', 'We could not read this picture. Choose another one.');
      default:
        return t('InviteSignUpForm.fieldFailed', 'Check this field.');
    }
  };

  const outcome = (code: string | null): string => {
    switch (code) {
      case codes.invitationExpired:
        return t('InviteSignUpForm.invitationExpired', 'This invitation has expired. Ask for a new link.');
      case codes.invitationAccepted:
        return t('InviteSignUpForm.invitationAccepted', 'This invitation was already used. Ask for a new link.');
      case codes.invitationRevoked:
        return t('InviteSignUpForm.invitationRevoked', 'This invitation was withdrawn.');
      case codes.invitationIdentifierMismatch:
        return t(
          'InviteSignUpForm.invitationIdentifierMismatch',
          'This invitation is for another phone number. Share the contact of the number it was sent to.',
        );
      case codes.phoneCountryNotAllowed:
        return t('InviteSignUpForm.phoneCountryNotAllowed', 'Phone numbers from your country cannot sign up here.');
      case codes.signUpVerificationNotFound:
        return t('InviteSignUpForm.verificationNotFound', 'This phone check is no longer running. Start again.');
      case InviteSignUpStatus.Expired:
        return t('InviteSignUpForm.expired', 'The time to share your contact ran out. Start again.');
      default:
        return t('InviteSignUpForm.failed', 'We could not finish signing you up. Start again.');
    }
  };

  const pictureHint = () =>
    t('InviteSignUpForm.pictureHint', '{types}, up to {size}, at least {min} pixels on each side.', {
      types: types(),
      size: byteSizeText(rules().maxBytes),
      min: rules().minDimension,
    });

  return { field, outcome, pictureHint };
}
