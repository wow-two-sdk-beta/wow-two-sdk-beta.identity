# Identity.Core.Phones

Phone slice over the shipped `IOtpService`: set a number, confirm it by one-time code, sign in by code, and the
number rules (E.164, allowed countries). Codes are created here and delivered by the host's `IOtpDeliveryHandler`
(SMS, Telegram, …). Sign-in by phone over the instance's Telegram bot: `../PhoneSignIn/phone-sign-in.md`.

## Quick start

```csharp
identity.AddPhoneNumbers(o => o.AllowedCountries = ["UZ"]).AddSignIn();

await phones.SetPhoneNumberAsync(user, "+998901234567");            // unconfirmed; rotates the stamp
var created = await phones.CreateConfirmationCodeAsync(user);       // deliver created.Code
await phones.ConfirmPhoneNumberAsync(user, code);

var signInCode = await phones.CreateSignInCodeAsync(phone);          // null → deliver nothing, answer the same
var user = await phones.VerifySignInCodeAsync(phone, code);
if (user is not null) await signIn.SignInAsync(user);
```

## Notes

- Store numbers in E.164; lookups compare exactly and only confirmed numbers sign in.
- `PhoneNumberPolicy` reads a typed number as E.164 (`+` and 8–15 digits, spaces, dashes, dots and parentheses ignored,
  a leading `00` taken as `+`) and checks its country against `PhoneOptions.AllowedCountries`.
- `Identity:Phone:AllowedCountries` (ISO 3166-1 alpha-2, such as `UZ`) applies over the code and reloads live; empty allows
  every country `PhoneCountryConstants` knows, an unknown code refuses the start. The list matches the fe sdk's `PhoneInput`.
- Confirmation codes bind to the user id and the number, so a code cannot confirm another account or a changed number.
- Code length, lifetime, attempts and rate limits come from `OtpOptions`.
