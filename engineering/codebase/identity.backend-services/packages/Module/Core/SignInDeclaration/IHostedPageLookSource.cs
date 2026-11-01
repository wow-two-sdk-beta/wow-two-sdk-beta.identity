namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>Finds the look a registered app names for its hosted pages; the OIDC server's client registry provides one.</summary>
public interface IHostedPageLookSource
{
    /// <summary>The look <paramref name="clientId"/> names, or null for an app that names none or is not registered.</summary>
    /// <param name="clientId">The OIDC client id, compared exactly.</param>
    HostedPageLook? Find(string clientId);
}
