# WoW2.Sdk.Beta.Be.Identity.Providers

Upstream social sign-in, kept out of Auth and Module for its 18 provider packages:

- OAuth wrappers for 17 providers (Amazon, Apple, Discord, Facebook, GitHub, GitLab, Google, LinkedIn, Microsoft, Notion,
  Reddit, Slack, Spotify, Twitch, Twitter, VK, Yandex)
- the Google ID-token authenticator
- the claim normalizer and `NormalizedClaimTypeConstants`

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Providers --prerelease
```

Namespaces are unchanged from the be sdk: `WoW.Two.Sdk.Beta.Be.Identity.{OAuth,Claims}`.
