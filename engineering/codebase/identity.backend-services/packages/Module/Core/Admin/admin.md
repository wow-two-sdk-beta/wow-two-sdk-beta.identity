# Identity.Core.Admin

The host CLI's `admin create`: the instance's first organization and its owner, the one person a host creates.
Members are managed elsewhere (the console and the members panel).

## Usage

```bash
app admin create --slug ocharo --name Ocharo [--organization-id <uuid>] \
  --display-name "Lola Karimova" [--phone +998901234567] [--email owner@example.com] [--user-id <uuid>]
```

- The owner needs a phone, an email or both; a preset id keeps a product's existing key.
- Idempotent: a second run answers `present` and changes nothing; a run that stopped halfway finishes on the next.
- Never takes a password: the owner signs in by phone, or sets a password through the reset link.
- An existing person found by email alone is adopted only when that email is confirmed: anyone can sign up with an
  address they do not hold, so an unconfirmed match is refused until it is confirmed or the run names the person with
  `--user-id`.
- Exit `0` created or present · `1` refused (the handle, an id, the email or the phone belongs to someone else) · `2`
  failed (usage, an invalid value, an unreachable database). The last stdout line is
  `admin-result {"verb":"create","exit":0,"status":"created","organizationId":"…","userId":"…","isOrganizationCreated":true,"isOwnerCreated":true,"durationMs":42}`;
  the human report goes to stderr.

## Wiring

```csharp
verbs.AddHostVerb(AdminCommand.Name, (services, verb, ct) => AdminCommand.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct));
```

`FirstOwnerService` (registered by `AddOrganizations`) holds the logic; the person keeps the given phone and email
confirmed and the `owner` role of the catalog (`OrganizationRoleConstants.Owner`).
