---
title: Hatch on your phone
section: Start here
order: 50
summary: Installing Hatch as an app on a phone, and what it can and cannot do there.
---

**Android, Chrome.** Open `<your-hatch-origin>/apps/hatch/`, sign in if the
wall is up, then menu (⋮) → **Install app**. An icon named Hatch lands on the
home screen; opening it shows a splash screen with the Hatch icon, then the
board, full-screen, with no address bar.

**iPhone, Safari.** Open the same address, then Share → **Add to Home
Screen**. On iOS 26 and later that's already a standalone app — every
home-screen site opens one from that version on. On an earlier iOS the icon
still appears, but opens inside Safari with the usual browser bar; there's
nothing further to turn on for an older device.

**Desktop, Chrome or Edge.** The address bar itself offers to install Hatch —
an icon at its right edge, or the browser's own menu.

## What to expect

Installed, Hatch opens in its own window, full-screen, with no browser bar.
Everything it does — a card's summary, a move, the key a confirmation shows —
stays in that one window.

A Hatch link tapped elsewhere on the phone — a pull request in the GitHub
app, a ticket key in a chat, a link in an email — opens in the
already-running app at that link, once Android has been told the app may
answer for it (next). On iOS, see *What iOS cannot do*, below.

A link that leaves Hatch — a pull request, a CI check, the Claude docs —
still opens outside it, in a tab that closes back to the app rather than
stacking up behind it. **Back** returns to exactly where you were.

## Letting it own links on Android

The icon alone isn't enough: until Android is told which installed app may
answer for this origin, a link tapped elsewhere still opens the browser. Two
values do that, and both belong to this one phone's install, so nobody can
ship them for you:

- **The package name.** On the phone, open `chrome://webapks` in Chrome and
  read it off Hatch's entry.
- **The certificate fingerprint.** Read it off the installed package: `adb
  shell pm path <package>` to find the APK, then `apksigner verify
  --print-certs` on it — or any on-device package inspector that shows a
  signing certificate's SHA-256.

Both are a deploy-time setting, not something set from inside the app:
`HATCH_ANDROID_PACKAGE` and `HATCH_ANDROID_CERT_FINGERPRINT`, in the same
`.env` file beside `compose.yaml` as *Putting a front door on it*, above.
Give the two values to whoever runs this Hatch.

Once they're set and the stack restarted, make Android re-check:
reinstalling the app is simplest, or `adb shell pm verify-app-links
--re-verify <package>` from a computer with the phone attached. Settings →
Apps → Hatch → **Open by default** then shows the origin as verified.

If verification never takes, the fallback always works: Settings → Apps →
Hatch → **Open by default** → **Add links**, and add the origin by hand.

## What iOS cannot do

A home-screen web app on iOS cannot register for a domain's links at all —
that needs a native app, which this isn't. A link tapped in another app
always opens Safari, installed or not; Hatch itself is only ever reached by
tapping its own icon.

Installing claims every path on Hatch's origin, not only `/apps/hatch/` —
right when Hatch is the only thing on that origin, as it is in the stack
this page starts from, and worth knowing if it ever isn't.
