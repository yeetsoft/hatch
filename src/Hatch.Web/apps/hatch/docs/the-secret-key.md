---
title: The secret key
section: Deploying and operating
order: 45
summary: What the secret key protects, how to make and keep one, how to change it without losing anything, and what happens when it is lost.
---

Hatch stores a few secrets for you: the credentials saved on the Settings page,
the Claude token, the Codex API key and the Grok Build API key among them (the
ones [the container runner](the-container-runner#its-credential) hands to its
agent), and the bot token of a chat workspace. It seals each one before it is
written to the database, so that a dump of the database, or a copy of its volume,
does not carry them in a form anyone can read.

The **secret key** is what seals them. It is one value, `HATCH_SECRET_KEY`: the
base64 of exactly 32 random bytes. It is not an account and not a password, and
nobody signs in with it. Nothing about the board needs it except saving a
secret.

## Making one

macOS / Linux:

```
openssl rand -base64 32
```

PowerShell, 5.1 or 7:

```
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

Either prints one line of 44 characters ending in `=`. Put it in the `.env`
beside `compose.yaml`, which git ignores:

```
HATCH_SECRET_KEY=<your-secret-key>
```

Then `docker compose up -d`. `restart` is not enough: it starts the same
container again with the environment it already had, and does not read `.env`.
[Getting started](getting-started#set-the-secret-key) puts this at the right
point in a first install.

**Keep a copy somewhere that is not the database or its backups.** A key kept
only next to the data it protects protects nothing, and a key that is lost
cannot be made again. See [Losing the key](#losing-the-key).

## What setting it, and not setting it, does

- **No key:** the API starts and warns once in its log, `HATCH_SECRET_KEY is not
  set, so secrets cannot be saved until one is set.` The board works as it did.
  Only saving a secret is refused: see [A secret will not
  save](troubleshooting#a-secret-will-not-save).
- **A key that is not base64 of 32 bytes:** the API stops at start, and says
  which variable. It never prints the value.
- **A good key:** a secret you save is sealed under it. On the first start with
  a key, everything stored before is sealed too. [Upgrading](upgrading#secrets-are-sealed-from-this-version)
  has what that start looks like.

`src/Hatch.Api/.env.json`, where it exists, wins over the environment for
`Hatch:SecretKey`, as for every other setting
([where secrets go](deployment-overview#where-secrets-go)).

## Changing the key

Change it when it may have been seen, or on whatever schedule you keep for such
things. The change is five steps, and nothing is lost if it goes in this order. The new key seals; the old key is kept for a while only so it can
open what it sealed.

1. **Back up first,** the way [backup, restore and
   replication](backup-restore-and-replication) says.
2. **Move the current key to `HATCH_PREVIOUS_SECRET_KEY`,** and set a new
   `HATCH_SECRET_KEY`. Make the new one as above. In the `.env`:

   ```
   HATCH_SECRET_KEY=<your-new-secret-key>
   HATCH_PREVIOUS_SECRET_KEY=<your-old-secret-key>
   ```

3. **Apply them:** `docker compose up -d`, which starts the api again with the
   new environment.
4. **Read the log.**

   ```
   docker compose logs api
   ```

   Look for the line the start writes once. In a container the log is one JSON
   object a line, and the sentence is its `Message`:

   ```
   Resealed 3 stored secrets under key <id>; 0 could not be opened.
   ```

   The number of secrets is however many you had. `<id>` is eight characters
   that name the new key and are not the key: the same key always gives the same
   id, so you can match a key to an id in a log line or in a stored value (it is
   the text between `v2:` and the first `.`). **`0 could not be opened` is the
   line you want.** Settings now shows each secret as set.
5. **Then remove `HATCH_PREVIOUS_SECRET_KEY`** from the `.env`, and run
   `docker compose up -d` again. Keep the old key's copy until you have a
   backup taken after the change; a dump from before it is sealed under the
   old key.

Every start after that says `Resealed 0 stored secrets under key <id>; 0 could not
be opened.` There is nothing left to reseal, and that is the healthy line too.

If the line says anything but `0 could not be opened`, **do not remove the
previous key.** Each secret it could not open is left as it is, and the log
names its row and the key it was sealed under:

```
The stored secret on <row> could not be opened, so it is left as it is. It names key <id>.
```

Remove the previous key now and those values are unreadable under both. Find
the key it names, set that one as `HATCH_PREVIOUS_SECRET_KEY` and restart, or
give up on that value and enter it again on the Settings page. See [The
Settings page says a secret cannot be opened](troubleshooting#the-settings-page-says-a-secret-cannot-be-opened).

The reseal runs once on each start of the api, and does nothing without a key.
If it fails, the failure is logged and the API still starts; the next start
tries again.

### In PowerShell

The shell does not matter to the steps, only to how you set a variable if you
do not use a `.env`. A variable set in the shell wins over the same name in the
`.env`:

```
$env:HATCH_SECRET_KEY = '<your-new-secret-key>'
$env:HATCH_PREVIOUS_SECRET_KEY = '<your-old-secret-key>'
docker compose up -d
```

`Remove-Item Env:HATCH_PREVIOUS_SECRET_KEY` takes the previous key back out,
and `docker compose up -d` makes the change real. Use the `.env`, where you
can: a variable set in a shell goes when the shell does, and the next
`docker compose up -d` from another one starts with whatever the `.env` says,
which may be the old key.

### In a cluster

The same steps, with the Secret as the place the key lives. Put the new key in
the Secret as `Hatch__SecretKey`, the old one beside it as
`Hatch__PreviousSecretKey`, and roll the api Deployment so its pod reads them.
Read the pod's log for the same line, then remove `Hatch__PreviousSecretKey`
and roll it once more. See [Kubernetes](kubernetes#the-secret-key).

## Losing the key

Lose it and the secrets sealed under it cannot be opened. Nothing else is
affected: no issue, comment or event is sealed. The Settings page says *This is
saved, but it cannot be opened* beside each of them, and everything that uses a
secret treats it as not set. A start with the wrong key says so for each one, and
names the key it was sealed under (`It names key <id>`), which tells you which
of your copies to look for.

Nothing can be done to get one back, because that is what sealing is. Set a new
key, `docker compose up -d`, and enter each secret again on the Settings page.
Saving replaces what was stored.

## Where else it is written

- [Getting started](getting-started#set-the-secret-key), for a first install.
- [Upgrading](upgrading#secrets-are-sealed-from-this-version), for an install
  that already has secrets stored.
- [Kubernetes](kubernetes#the-secret-key), for where the key lives in a cluster.
- [Backup, restore and replication](backup-restore-and-replication#the-key-and-the-backups),
  for why the dumps need it.
- [Environment variables](environment-variables) and [what the API
  reads](deployment-overview#what-the-api-reads), for the two variables.
