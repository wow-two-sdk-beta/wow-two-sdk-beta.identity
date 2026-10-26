# development/

Repository-specific development guidance:

- [Frontend](frontend-guidelines.md): pnpm workspace, Vue app, verification, and host-copy commands.
- [Backend](backend-guidelines.md): .NET guidance.
- [Iterations](iteration-guide.md): planning and acceptance workflow.
- [Rules](rules.md): working rules for agents in this repo.

Rules: wow-two-ws/conventions/development/

## Repository

- created 2026-10-26 by the hub's `create-library` skill, from `wow-two-sdk-beta.domain-template`.
- GitHub settings: public; issues, wiki, projects and discussions off; workflow token read-only; the `main-guard`
  ruleset blocks deleting `main` and force-pushing to it.
- rollout state, run from the hub: `python3 -I scripts/release/library.py plan wow-two-sdk-beta.identity`
  (`wow-two-ws/scripts/release/library.py`).
- hand steps left: the first npm publish of `@wow-two-beta/fe-identity`, the NuGet trusted-publishing consent,
  Wheelhouse registration, the instance's secrets, DNS.
