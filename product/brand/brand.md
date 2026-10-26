# brand/

*Last updated: 2026-10-26*

> The brand of Identity's hosted pages: sign-in, account and admin console. Rules:
> `wow-two-ws/conventions/development/capability-forms.md` § Theming.

| Part | Value |
|---|---|
| Name | Identity; an instance shows its owner's brand name on the hosted pages |
| Icon | none yet; icon files go beside this doc |
| Default theme | the SDK theme `platform`, the shared look of the platform apps (proposed) |
| Theme source | an SDK theme picked by name in the instance's configuration |
| Per-app theme | a registered app may name its own SDK theme; Ocharo's apps use `ocharo` |
| Languages | `uz`, `ru`, `en` |

- the hosted pages are built on the fe sdk, so every SDK theme ships in the image.
- embedded parts from `@wow-two-beta/fe-identity` take the consuming app's theme and have no theme setting.
- hosted pages load no third-party assets: fonts, icons and scripts ship in the image (proposed, Q19).
