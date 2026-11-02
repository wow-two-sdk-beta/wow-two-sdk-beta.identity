# Identity.Core.ProfilePictures

One profile picture per person: the processed square WebP.

## Notes

- `AddProfilePictures()` registers the rules (`Identity:ProfilePictures`), `SkiaProfilePictureProcessor` (SkiaSharp,
  MIT) and `EfProfilePictureStore` over `identity_profile_pictures`; a registered `IProfilePictureStore` wins, the seam
  a File Storage adapter takes.
- An upload is JPEG, PNG or WebP by its leading bytes only, at most `MaxUploadBytes`, its sides between `MinDimension`
  and `MaxDimension` read before decoding; it is decoded upright (EXIF orientation), cropped to its centre square,
  scaled to `OutputSize` and encoded WebP at `Quality`, so no metadata survives.
- `GET /api/account/pictures/{userId}` serves it anonymously; `?v={version}` caches it for a year as immutable.
- The OIDC server issues `picture` under the `profile` scope from `LinkTemplate`; the BFF's session person carries it.
- Erasure deletes the picture (`ProfilePictureDeletionHandler`).
