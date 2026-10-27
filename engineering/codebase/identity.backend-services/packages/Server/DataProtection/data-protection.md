# Identity.DataProtection

The instance's ASP.NET Core Data Protection ring, kept in its identity database and wrapped by a certificate from its
settings (Q26). The ring seals the sign-in cookie, antiforgery tokens and passkey ceremony state; every replica of an
instance reads the same rows, so a container replacement or a second replica keeps every cookie. Design: T6 § 3 and
T10 row 31 (`engineering/research/identity-analysis-2026-10/`).

## Quick start

```csharp
services.AddUserAccounts().AddDataProtectionRing();      // Identity:DataProtection, applied last
```

```jsonc
"Identity": {
  "DataProtection": {
    "ApplicationName": "identity",                       // isolates this instance's payloads; changing it voids every cookie
    "Certificates": [ "-----BEGIN CERTIFICATE-----\n…" ] // a vault secret, or a file path; several during a rotation
  }
}
```

## How it works

| Piece | Role |
|---|---|
| `DatabaseXmlRepository` (internal) | the key manager's store: `identity_data_protection_keys` through `IdentityDbContext`, a scope per call |
| `CertificateRingXmlEncryptor` (internal) | wraps each new key's secret to the active certificate (XML encryption: RSA-OAEP over an AES-256 key) |
| `CertificateRingXmlDecryptor` | unwraps a key with the listed certificate the key names; the key manager creates it by the type name each key records |
| `DataProtectionRingOptionsValidator` (internal) | the boot check, below |

- Active certificate: of the listed ones valid now, the one expiring last; every listed one unwraps.
- No certificate store is consulted: a key wrapped to a certificate no longer listed stays unreadable, and the key
  manager skips it, so every cookie it sealed dies.

## Boot check

A boot refuses to start, naming each setting and never its key material, when:

- a listed value is unreadable, or holds no certificate with its private key
- a certificate is not RSA, is under 2048 bits, or lacks the key-encipherment usage
- none of the listed certificates is valid now
- the list is empty outside a local run (`DEPLOY_ENVIRONMENT` set, or not `Development`)

A local run with an empty list stores the ring unwrapped and logs a warning.

## Certificates

```bash
docker run --rm <image> keys generate --role data-protection [--days N]   # two years by default
```

- The output is one PEM bundle; paste it as is into the vault secret `Identity:DataProtection:Certificates:0`.
- Rotation: add the next certificate as `:1` and deploy; expiring after the old one, it wraps every new key from its
  `NotBefore` on. Keep the old one until the keys it wrapped retire (the ring rolls a key every 90 days and keeps the
  old ones to read what they sealed; cookies live 12 hours), then remove it.
- A leaked certificate or ring: T6 § 3 *Compromise runbook*; revoking every key ends every cookie.

## Notes

- Replaces a file-system ring (`AddPersistentDataProtection`, `DataProtection:KeyDirectory`): no volume holds keys.
- Core gap: the be sdk's `Web/DataProtection` offers only the file-system ring; this package carries the database
  ring and the in-settings certificate wrapping until core grows them.
- `XmlKeyDecryptionOptions` is internal to ASP.NET Core and resolved at registration only, which a late-bound setting
  cannot reach; hence the own decryptor over the same XML-encryption format.
