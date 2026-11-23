# Secrets Architecture

## Summary

Hatch keeps a few secrets in its own database: the agent credentials and the
other secret settings on the Settings page, and each chat workspace's bot
token. They are rows in Postgres, sealed with AES-256-GCM under a key the
operator holds **outside** the database.

So a reader of this document can say three things:

- **Where a secret is stored.** In a text column of a row, in the form
  `v2:<key id>.<nonce>.<ciphertext+tag>`. The list of columns is one file,
  `src/Hatch.Api/Common/SecretStores.cs`.
- **What protects it.** The key in `HATCH_SECRET_KEY`, and nothing else. A
  database dump holds sealed text and no key.
- **What the operator has to keep.** That key, apart from the dump. Lose it and
  every sealed secret is unreadable; the cure is to type each one again.

Nothing here is committed. The key is an operator secret in the sense of
[`ethos.md`](ethos.md#parameterization-in-practice): it is supplied by the
installation, and this document writes `<key>` where a value would go.

This document says what was built. Where code makes a claim, the file is named.

## The schemes

`src/Hatch.Api/Common/SecretProtector.cs` is the one way a secret is written to
the database and the one way it is read back. A stored value is
`<scheme>:<payload>`. Neither payload alphabet (base64, and base64url joined by
dots) has a colon, so the first colon is always the scheme's.

| Stored as | Scheme | Read | Written |
|---|---|---|---|
| `v2:<payload>` | `v2`, sealed under the key | with the key | yes, the only one (`CurrentScheme`) |
| `v1:<payload>` | `v1`, XOR | without a key | no |
| no colon | read as `v1`: written before schemes existed | without a key | no |
| any other tag | unknown, such as a newer Hatch's | never | no |

An unknown scheme reads as "cannot be opened", the same as a damaged value. The
protector answers null for it rather than throw, so a hand-edited row costs the
one setting a re-entry and does not break every call that touches the row.

## v1

`src/Hatch.Api/Common/SecretObfuscator.cs`. A fixed key, XORed over the bytes,
then base64. That is **obfuscation, not encryption**: the key is in the source.

`v1` is still read, because rows written before sealing are `v1` or untagged,
and the reseal (below) needs to open them to seal them again. Reading it needs
no key. Nothing stores a `v1` value any more.

The one place `v1` is still written is the wire to the container runner, which
has its own copy of the XOR (see "The container handoff").

## v2

`src/Hatch.Api/Common/SecretCipher.cs`.

- **Cipher.** AES-256-GCM, with a 32-byte key.
- **Nonce.** 12 random bytes, new for every seal.
- **Tag.** 16 bytes, appended to the ciphertext.
- **Associated data.** The name of the row (`SecretStores`, below). A sealed
  value copied onto another row fails the tag check and does not open there.
- **Stored form.** `v2:<key id>.<nonce>.<ciphertext+tag>`. The last two are
  base64url without padding.
- **Key id.** The first 8 lower-case hex characters of the SHA-256 of the key
  (`src/Hatch.Api/Common/SecretKeys.cs`). It says which key sealed a value
  without giving any of it away, and it is what the logs print.

Only the current key seals. The previous key opens what it sealed and seals
nothing. Opening never throws: a value that is damaged, cut short or sealed
under a key Hatch does not hold is "cannot be opened".

## The key

Two variables, each base64 of exactly 32 bytes. Make one with
`openssl rand -base64 32`.

| Variable | Configuration name | Meaning |
|---|---|---|
| `HATCH_SECRET_KEY` | `Hatch__SecretKey` | The key that seals, and opens. |
| `HATCH_PREVIOUS_SECRET_KEY` | `Hatch__PreviousSecretKey` | The key before it, kept during a change. Opens only. |

Both are options in `src/Hatch.Api/Modules/Hatch/HatchOptions.cs`, and
[the environment variables page](../src/Hatch.Web/apps/hatch/docs/environment-variables.md)
lists them for an operator.

**At startup.** `src/Hatch.Api/Modules/SecretKeyValidation.cs` is registered
with `ValidateOnStart` (`src/Hatch.Api/Modules/Hatch/HatchModule.cs`). A blank
value is "not set" and passes. A set value that is not base64 of 32 bytes stops the API, and the
message names the variable and never prints the value.

**With no key.** The API starts, and:

- `src/Hatch.Api/Modules/Hatch/SecretKeyWarning.cs` warns once at start.
- `TrySeal` returns false. A save that carries a secret is a 400 naming
  `HATCH_SECRET_KEY`, and writes nothing
  (`src/Hatch.Api/Modules/Hatch/SettingsController.cs`). Nothing is stored
  weakly in its place.
- Sending `""` still clears a secret, since clearing needs no key.
- `v1` and untagged values still read. `v2` values cannot be opened, and the
  Settings page says so.

[Troubleshooting](../src/Hatch.Web/apps/hatch/docs/troubleshooting.md#the-settings-page-says-a-secret-cannot-be-opened)
has the operator's side of that.

## Rotation and the reseal

`src/Hatch.Api/Modules/Hatch/SecretReseal.cs`.

To change the key: set `HATCH_PREVIOUS_SECRET_KEY` to the old key, set
`HATCH_SECRET_KEY` to the new one, and restart. The log line `Resealed N stored
secrets under key <key id>; M could not be opened.` says the work is done. When
`M` is 0, drop the previous key.

What happens at start:

1. With no current key, or with `HATCH_MIGRATE=1` (migrate mode), it does
   nothing.
2. It takes a Postgres advisory lock (`LockKey`, through
   `PostgresAdvisoryLock`, in `src/Hatch.Api/Modules/Hatch/Goose.cs`). If
   another instance holds it, this one logs that and goes on; the other reseals.
3. For each store, each value that is not already `v2` under the current key is
   opened and sealed again for the same row. That covers `v1`, untagged, and
   `v2` under the previous key.
4. The update matches the old text as well as the row, so a secret saved while
   this runs is not overwritten with one sealed from the older value.

A value that cannot be opened is left **byte for byte** as it was. The log names
its row and the key id it names, never the value. A failed reseal logs the error
and the API starts anyway; values stay readable under both keys, and the next
start tries again.

## The stores

`src/Hatch.Api/Common/SecretStores.cs` lists every place a sealed value lives,
once. A save seals through it and the reseal walks it. Row names are built there
and nowhere else.

| Store | Column | Row name |
|---|---|---|
| `SiteSettings` | `SiteSettings.Value` | `site-setting/<key>` |
| `ChatWorkspaces` | `hatch.ChatWorkspaces.Token` | `chat-workspace/<provider>/<external id>` |

To add one:

1. Add a `SecretStore` to `SecretStores.cs` and put it in `All`.
2. Seal with `TrySeal(plaintext, SecretStores.X(...))`, and read with
   `Unprotect` or `TryUnprotect`, with the same row name.
3. Add a case to `SecretStoresTests` and to `SecretResealTests`
   (`src/Hatch.Api.Tests`).

The reseal then finds the new store with no other change.

## The container handoff

A container runner fetches an agent's credential from
`src/Hatch.Api/Modules/Hatch/SettingsController.cs` and reads it with the CLI
(`src/Hatch.Cli/CredentialCommand.cs`). On the wire the credential is
`CredentialWire.Wrap`'s `v1:<base64>` (`src/Hatch.Api/Common/CredentialWire.cs`).

It stays `v1` for two reasons. Every container image already built reads `v1`,
so a new scheme would break them. And the API and the CLI ship as separate
binaries, so `CredentialWire` carries its own copy of the fixed key and calls
neither `SecretProtector` nor `SecretObfuscator`. Storage can change freely
because it does not share code with the wire.

Two things keep that safe. The route answers only with the wall off, and a 403
otherwise. And what keeps the credential from the network in clear is TLS, which
is the operator's to put in front of Hatch. The wire is obfuscation, and says so.

## OpenedSecret

`src/Hatch.Api/Common/OpenedSecret.cs`. `Unprotect` returns the opened value in
this type, which prints and serialises as `[secret]`. A snapshot that is logged,
returned or interpolated by mistake gives nothing away.

`Reveal()` is the one way the plaintext comes out. Search for it and every use
of a secret turns up. `OpenedSecret.From` gives null for null or blank, so "is
it set" is `secret is not null`. Equality is by reference, because comparing two
secrets by value is a way to read one.

## Backup and rollback

A `pg_dump` holds `v2` text and, for values not yet resealed, `v1` text. It
never holds the key. A restore therefore needs the key kept separately, or every
`v2` value is unreadable;
[backup, restore and replication](../src/Hatch.Web/apps/hatch/docs/backup-restore-and-replication.md)
has the rest of a restore.

A build from before sealing reads `v1` and untagged values, and answers null
for a `v2` one: its `SecretProtector` treats an unknown scheme as unreadable, and
says so in its own comment. So once a reseal has run, every secret reads there
as "cannot be opened". That is read from that build's source
(`git show 6f30a590^:src/Hatch.Api/Common/SecretProtector.cs`); the build itself
was not run against a `v2` value.

To roll back, restore a dump from before the upgrade, or type the secrets again
once the older build is running.
