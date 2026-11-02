import { invalid, object, valid, Validator, type ValidationIssue } from '@wow-two-beta/fe/foundation/validators';
import { IdentityErrorCodeConstants, ProfileFieldMode, type SignUpProfileDto } from '../../../../contracts';
import { displayNameMaxLength, pictureFileIssue } from '../../../account/validation';

/** Holds what the invitation sign-up form collects. */
export interface InviteSignUpValues {
  readonly displayName: string;
  readonly picture: File | null;
}

/** The field of the form each profile refusal belongs under, by its identity code. */
const fieldOfCode: Readonly<Record<string, keyof InviteSignUpValues>> = {
  [IdentityErrorCodeConstants.displayNameRequired]: 'displayName',
  [IdentityErrorCodeConstants.displayNameTooLong]: 'displayName',
  [IdentityErrorCodeConstants.pictureRequired]: 'picture',
  [IdentityErrorCodeConstants.pictureTooLarge]: 'picture',
  [IdentityErrorCodeConstants.pictureUnsupportedType]: 'picture',
  [IdentityErrorCodeConstants.pictureTooSmall]: 'picture',
  [IdentityErrorCodeConstants.pictureTooBig]: 'picture',
  [IdentityErrorCodeConstants.pictureUnreadable]: 'picture',
};

/** Names the field a refusal's identity code belongs under; `null` for a code no field shows. */
export function inviteSignUpFieldOf(code: string | null): keyof InviteSignUpValues | null {
  return code !== null && Object.hasOwn(fieldOfCode, code) ? (fieldOfCode[code] ?? null) : null;
}

type Message = (code: string) => string;

function issue(path: ReadonlyArray<string | number>, code: string, message: Message): ValidationIssue[] {
  return [{ path, code, message: message(code) }];
}

/** Accepts any name while it is off; a blank one while optional; else one of at most `displayNameMaxLength` characters. */
function displayNameRule(mode: ProfileFieldMode, message: Message): Validator<string> {
  return new Validator<string>('displayName', (value, path) => {
    const name = typeof value === 'string' ? value.trim() : '';
    if (mode === ProfileFieldMode.Off) return valid(typeof value === 'string' ? value : '');
    if (name === '') {
      return mode === ProfileFieldMode.Required
        ? invalid(issue(path, IdentityErrorCodeConstants.displayNameRequired, message))
        : valid('');
    }
    return name.length > displayNameMaxLength
      ? invalid(issue(path, IdentityErrorCodeConstants.displayNameTooLong, message))
      : valid(value as string);
  });
}

/** Accepts no picture unless it is required, and a picture whose type and size fit the declared rules. */
function pictureRule(profile: SignUpProfileDto, message: Message): Validator<File | null> {
  return new Validator<File | null>('picture', (value, path) => {
    if (!(value instanceof Blob) || profile.picture === ProfileFieldMode.Off) {
      return profile.picture === ProfileFieldMode.Required
        ? invalid(issue(path, IdentityErrorCodeConstants.pictureRequired, message))
        : valid(null);
    }
    const fileIssue = pictureFileIssue(value, profile.pictureRules);
    return fileIssue ? invalid(issue(path, fileIssue, message)) : valid(value as File);
  });
}

/**
 * Builds the invitation sign-up form's validator from the declaration's `signUpProfile`: each field off, optional or
 * required as declared, the name within the service's length and the picture within `pictureRules`. Each failure carries
 * its identity code, the one the service answers for the same failure, so both render the same message.
 */
export function inviteSignUpSchema(profile: SignUpProfileDto, message: Message): Validator<InviteSignUpValues> {
  return object({ displayName: displayNameRule(profile.displayName, message), picture: pictureRule(profile, message) });
}
