# Identity.Core.Admin

The host CLI's `admin create`: the instance's first admin, the one person a host creates, holding the instance role
`admin`. Only once per instance. That admin creates organizations in the console (`POST /api/console/organizations`,
becoming each one's owner), and organizations' owners and admins invite everyone else there.

## Usage

```bash
app admin create --display-name "Lola Karimova" [--phone +998901234567] [--email admin@example.com] [--user-id <uuid>]
```

- The admin needs a phone, an email or both; a preset id keeps a product's existing key.
- Only once: while another person holds `admin`, a run is refused (exit `1`) and writes nothing.
- Idempotent: a rerun naming the person who holds `admin` answers `present` and changes nothing, so a deploy may run it
  every time; a run that stopped halfway (the person created, the role not given) finishes on the next.
- Never takes a password: the admin signs in by phone, or sets a password through the reset link.
- An existing person is found by the id, the email or a confirmed phone and given `admin`; one found by email alone is
  adopted only when that email is confirmed: anyone can sign up with an address they do not hold, so an unconfirmed
  match is refused until it is confirmed or the run names the person with `--user-id`.
- Exit `0` created or present · `1` refused (another admin, or an id, the email or the phone belongs to someone else) ·
  `2` failed (usage, an invalid value, an unreachable database). The last stdout line is
  `admin-result {"verb":"create","exit":0,"status":"created","userId":"…","isUserCreated":true,"isRoleGranted":true,"durationMs":42}`;
  the human report goes to stderr.

## Wiring

```csharp
verbs.AddHostVerb(AdminCommand.Name, (services, verb, ct) => AdminCommand.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct));
```

`InstanceAdminService` holds the logic; the command builds it from the host's services, so it needs the roles slice
(`AddRoles`) and no registration of its own. The person keeps the given phone and email confirmed and the instance role
`admin` (`InstanceRoleConstants.Admin`), which opens the console from their next sign-in.
