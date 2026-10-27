# Identity.Keys

The OIDC server's signing and encryption certificates: where they come from, the boot check that fails closed, the
pick of the active one by date, and the owner's generator. Design: T6 § 3
(`engineering/research/identity-analysis-2026-10/t6-hosting-security.md`), slice I17.

## Settings

```jsonc
"Identity": {
  "Server": {
    "Keys": {
      "Signing":    [ "/data/keys/signing-2026-10.pem", "/data/keys/signing-2027-01.pem" ],  // several during a rotation
      "Encryption": [ "/data/keys/encryption-2026-10.pem" ],
      "Ephemeral": false                 // true only on a local run: throwaway keys, every restart voids each token
    }
  }
}
```

- Each file is one PEM bundle: the certificate, then its unencrypted PKCS#8 private key, as `keys generate` prints it.
  A relative path resolves against the host's content root.
- `appsettings.Development.json` turns `Ephemeral` on for a local run (`Development` without `DEPLOY_ENVIRONMENT`); a
  deployed target sets it to `false` (the descriptor's default) and lists its files.

## Types

| Type | Role |
|---|---|
| `ISigningKeySource` | the seam: returns a `SigningKeySet` (certificates by role), read once when the server's options build at boot; a database-held source can replace it |
| `HostFileSigningKeySource` | the default: reads the listed files, naming every missing, unreadable or keyless one |
| `SigningKeyValidator` | per role: at least one certificate; each RSA ≥ 2048 bits with its private key and its role's key usage; one valid now |
| `SigningKeySet.SelectActive` | of the certificates valid at an instant, the one expiring last (the latest start breaks a tie); null when none is |
| `ServerKeyConfiguration` (internal) | hands the certificates to OpenIddict at boot, or makes ephemeral RSA keys on a local run, and logs the active one per role |
| `ActiveKeySelectionHandler` (internal) | an OpenIddict token handler that re-picks the signing and encryption certificate per token by `SelectActive` on the server's clock |
| `SigningCertificateGenerator` | a self-signed RSA 3072 certificate per role: digital signature or key encipherment, whole-second dates; signing 120 days, encryption 210 days by default |
| `SigningKeyCommand` | `keys generate`, below |

## Boot check

A boot refuses to start, naming each problem, when:

- a listed file is missing, unreadable, or holds no certificate with its private key
- a role lists no certificate, or none of its certificates is valid now
- a certificate is not RSA, is under 2048 bits, or lacks its role's key usage
- `Ephemeral` is on outside a local run, or beside listed files

An expired or staged certificate beside a valid one passes: the old one still verifies or decrypts what it sealed,
the staged one takes over on its date.

## Algorithms

- Signing: RS256 with the active certificate; the JWKS publishes every listed signing certificate.
- Encryption: RSA-OAEP with A256CBC-HS512 for codes and refresh tokens; every listed encryption certificate decrypts.
- Access and ID tokens are signed only; access tokens stay unencrypted so resource servers verify them offline.

## `keys generate`

```bash
docker run --rm <image> keys generate --role signing|encryption [--days N] [--not-before <yyyy-MM-dd[THH:mm:ssZ]>]
```

- Prints one PEM bundle to standard output and the thumbprint and dates to standard error; exits `0`, or `2` with the
  usage on a mistake. No host boots, so it needs no settings or database.
- `--days` takes 1–3650 and defaults to the role's lifetime; `--not-before` defaults to now (UTC) and stages the
  certificate when it lies ahead.
- The owner runs it and stores the output as the target's key file; CI and agents never run it outside tests.

## Rotation spikes (T6), answered by `Identity.Tests.E2E/Oidc/OidcKeyRotationTests`

- Does the JWKS publish a certificate whose `NotBefore` lies in the future? Yes: OpenIddict publishes every
  configured signing certificate, staged and expired alike, so relying parties cache the next key before it signs.
- Does a staged second certificate take over by its date? Yes, without a restart, through `ActiveKeySelectionHandler`.
  OpenIddict alone orders its keys once, at boot (valid before staged, then the furthest expiry), and signs with the
  first, so without the handler a staged certificate would take over only at the next restart; the control test shows
  both.

## Notes

- The thumbprint is the `kid` in tokens and the JWKS.
- A key file change (add or remove) reaches the server with the next deploy; activation by date needs none.
- Certificates in tests are throwaway, made in memory or in temp files; no real key material lives in the repo.
