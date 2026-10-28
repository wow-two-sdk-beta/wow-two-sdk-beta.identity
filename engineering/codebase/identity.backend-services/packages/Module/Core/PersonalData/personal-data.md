# Identity.Core.PersonalData

The data-subject rights every account needs (GDPR access, portability, erasure): export what the SDK and the product
hold for a user, and erase the account for good. Products plug in their own sections and erasure steps.

## Quick start

```csharp
identity.AddRoles().AddPersonalData();
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IPersonalDataExporter, OrdersExporter>());
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountDeletionHandler, OrdersEraser>());
```

| Route | Does |
|---|---|
| `GET manage/personal-data` | downloads `personal-data.json` |
| `POST manage/delete-account` | `{ password }` when the account has one; `204`, then the cookie is cleared |

## Notes

- Built-in sections: `account`, `claims`, `logins`, `passkeys`, `sessions` (bearer refresh tokens),
  `sign_in_sessions` and `sign_in_log` (masked, as stored), and `roles` with the roles slice.
- Password hashes, token hashes, security stamps and passkey keys are never exported.
- Deletion runs the product's handlers first, then removes roles, claims, logins, stored tokens, refresh tokens,
  sign-in sessions, passkeys and memberships, and scrubs the user row in place (names, email, phone and password hash
  cleared, `deleted_at` set) in one transaction. The row stays, so a product's key into it never blocks. Handlers must
  tolerate running again after a failure.
- Sign-in log rows stay until their retention ends (`Identity:SignInLog:RetentionDays`); they hold the masked login
  and address only.
- Issued access tokens stay valid until they expire; keep their lifetime short.
