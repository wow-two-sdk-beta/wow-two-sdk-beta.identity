# Identity.Members

Organization members for services, in process: who belongs to an organization, in which role, and whether the instance's
Telegram bot reaches them. The contract, the HTTP route and the answers:
[contract](../../../../../architecture/contract.md#organization-members).

## Quick start

```csharp
identity.AddOrganizations();                                  // registers IMemberBroker (IdentityCoreMemberBroker), scoped

var result = await members.ListAsync(organizationId);         // every member, earliest joined first
var editors = await members.ListAsync(organizationId, "editor");
// MemberModel { Subject = "<uuid>", Role = "editor", HasTelegramLink = true }
```

## Notes

- A member reads only their `sub`, role (the catalog's spelling) and Telegram flag; names, phones, emails and chats stay in
  the management API (`../Core/Organizations/organizations.md`), which only the organization's owners and admins reach.
- The role filter takes a catalog role in any case; another one fails `UnknownOrganizationRole`, an organization the
  instance lacks `OrganizationNotFound`, each in the error's `identityCode` metadata.
- `HasTelegramLink` is the check app notices make before sending (`../Notices/notices.md`): a confirmed phone with a link to
  the bot's chat, read in one query (`ITelegramLinkRepository.FindLinkedNumbersAsync`). Without the bot registered it reads
  false. A muted person still reads true: a mute is the person's choice per app, answered by the notice itself.
- The list is not paged, as the management API's isn't: one organization's members fit one answer.
