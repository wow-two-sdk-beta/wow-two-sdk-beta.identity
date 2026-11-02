namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>Defines the seam that turns an uploaded profile picture into the one square WebP the instance keeps.</summary>
public interface IProfilePictureProcessor
{
    /// <summary>
    /// Checks <paramref name="upload"/> against <paramref name="rules"/> — its size, its type by leading bytes, its sides read
    /// before decoding — then decodes it, crops the centre square, scales it to <see cref="ProfilePictureOptions.OutputSize"/>
    /// and encodes WebP at <see cref="ProfilePictureOptions.Quality"/>, keeping no metadata. Never throws on bad input.
    /// </summary>
    /// <param name="upload">The uploaded bytes.</param>
    /// <param name="rules">The rules in effect.</param>
    ProfilePictureResult Process(ReadOnlySpan<byte> upload, ProfilePictureOptions rules);
}
