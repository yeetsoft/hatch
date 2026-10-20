---
title: Hatch on your phone
section: Start here
order: 30
summary: Installing Hatch as an app on a phone, and what it can and cannot do there.
---

**Android, Chrome.** Open `<your-hatch-origin>/apps/hatch/`, sign in if the
wall is up ([putting a front door on it](putting-a-front-door-on-it)), then
menu (⋮) → **Install app**. An icon named Hatch lands on the home screen;
opening it shows a splash screen, then the board, full-screen, with no
address bar.

**iPhone, Safari.** Open the same address, then Share → **Add to Home
Screen**. On iOS 26 and later that's already a standalone app. On an earlier
iOS the icon still appears, but opens inside Safari with the usual browser
bar.

**Desktop, Chrome or Edge.** The address bar itself offers to install Hatch —
an icon at its right edge, or the browser's own menu.

## What to expect

Installed, Hatch opens full-screen, with no browser bar. A Hatch link tapped
elsewhere — a pull request in the GitHub app, a ticket key in a chat — opens
in the already-running app, once Android has been told the app may answer
for it (next); on iOS it cannot be, see *What iOS cannot do* below. A link
that leaves Hatch still opens outside it, in a tab that closes back to the
app rather than stacking up behind it.

## Letting it own links on Android

Until Android is told which installed app may answer for this origin, a
link tapped elsewhere still opens the browser. Two values do that, set once
per phone:

- **The package name** — `chrome://webapks` in Chrome, off Hatch's entry.
- **The certificate fingerprint** — off the installed package, with
  `apksigner verify --print-certs` or any on-device package inspector that
  shows a signing certificate's SHA-256.

Both are a deploy-time setting: `HATCH_ANDROID_PACKAGE` and
`HATCH_ANDROID_CERT_FINGERPRINT`, in the same `.env` file beside
`compose.yaml` as
[putting a front door on it](putting-a-front-door-on-it). Give the two
values to whoever runs this Hatch.

Once they're set and the stack restarted, reinstalling the app makes Android
re-check, or `adb shell pm verify-app-links --re-verify <package>` from a
computer with the phone attached. If verification never takes, Settings →
Apps → Hatch → **Open by default** → **Add links** always works instead.

## What iOS cannot do

A home-screen web app on iOS cannot register for a domain's links at all —
that needs a native app, which this isn't. A link tapped in another app
always opens Safari, installed or not; Hatch itself is only ever reached by
tapping its own icon.

Installing claims every path on Hatch's origin, not only `/apps/hatch/` —
right when Hatch is the only thing on that origin, as it is in the stack
this page starts from, and worth knowing if it ever isn't.
