/** Mirrors `ProfilePictureRulesDto` of the be contract: what a profile picture upload accepts (`Identity:ProfilePictures`). */
export interface ProfilePictureRulesDto {
  /** The media types a picture may have, such as `image/jpeg`, `image/png` and `image/webp`. */
  readonly acceptedTypes: ReadonlyArray<string>;
  /** The largest upload in bytes; `5242880` by default. */
  readonly maxBytes: number;
  /** The shortest side a picture may have, in pixels; `256` by default. */
  readonly minDimension: number;
  /** The longest side a picture may have, in pixels; `4096` by default. */
  readonly maxDimension: number;
}
