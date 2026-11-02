import { IdentityErrorCodeConstants, type ProfilePictureRulesDto } from '../../../contracts';

/** The longest name the service keeps; a longer one answers `DisplayNameTooLong`. */
export const displayNameMaxLength = 120;

/** Names what is wrong with a picture, as the identity code the service answers for it. */
export type PictureIssue =
  | typeof IdentityErrorCodeConstants.pictureTooLarge
  | typeof IdentityErrorCodeConstants.pictureUnsupportedType
  | typeof IdentityErrorCodeConstants.pictureTooSmall
  | typeof IdentityErrorCodeConstants.pictureTooBig
  | typeof IdentityErrorCodeConstants.pictureUnreadable;

/** The width and height of a decoded picture, in pixels. */
export interface PictureSize {
  readonly width: number;
  readonly height: number;
}

/** Reads a picture's size; `null` when the browser cannot decode pictures here, and a rejection when it is no picture. */
export type PictureSizeReader = (picture: Blob) => Promise<PictureSize | null>;

/** Decodes the picture with `createImageBitmap` where the browser has it, and lets the bitmap go at once. */
export const readPictureSize: PictureSizeReader = async (picture) => {
  if (typeof globalThis.createImageBitmap !== 'function') return null;
  const bitmap = await globalThis.createImageBitmap(picture);
  try {
    return { width: bitmap.width, height: bitmap.height };
  } finally {
    bitmap.close?.();
  }
};

/**
 * Checks what a picture says about itself against `rules`, as the service would: its type among `acceptedTypes` and its
 * size up to `maxBytes`. A file with no type is left to the decoding and the service. `null` when both fit.
 */
export function pictureFileIssue(picture: Blob, rules: ProfilePictureRulesDto): PictureIssue | null {
  const type = picture.type.toLowerCase();
  if (type !== '' && !rules.acceptedTypes.some((accepted) => accepted.toLowerCase() === type)) {
    return IdentityErrorCodeConstants.pictureUnsupportedType;
  }
  if (picture.size === 0) return IdentityErrorCodeConstants.pictureUnreadable;
  return picture.size > rules.maxBytes ? IdentityErrorCodeConstants.pictureTooLarge : null;
}

/**
 * Checks a picture against `rules` before it is sent: its type and size, then, where the browser decodes pictures, the
 * shortest side against `minDimension` and the longest against `maxDimension`. A picture that does not decode is
 * `PictureUnreadable`. `null` when it fits; the service checks everything again.
 */
export async function inspectPicture(
  picture: Blob,
  rules: ProfilePictureRulesDto,
  readSize: PictureSizeReader = readPictureSize,
): Promise<PictureIssue | null> {
  const fileIssue = pictureFileIssue(picture, rules);
  if (fileIssue) return fileIssue;
  let size: PictureSize | null;
  try {
    size = await readSize(picture);
  } catch {
    return IdentityErrorCodeConstants.pictureUnreadable;
  }
  if (!size) return null;
  if (Math.min(size.width, size.height) < rules.minDimension) return IdentityErrorCodeConstants.pictureTooSmall;
  return Math.max(size.width, size.height) > rules.maxDimension ? IdentityErrorCodeConstants.pictureTooBig : null;
}
