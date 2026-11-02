namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

/// <summary>How a sign-in session's end is set.</summary>
public enum SessionExpiration
{
    /// <summary>
    /// An absolute end from sign-in: <see cref="SessionOptions.Lifetime"/>, or <see cref="SessionOptions.ExtendedLifetime"/>
    /// when the person asks to stay signed in.
    /// </summary>
    Fixed,

    /// <summary>An end that moves with use: <see cref="SessionOptions.SlidingLifetime"/> after the last request.</summary>
    Sliding,
}
