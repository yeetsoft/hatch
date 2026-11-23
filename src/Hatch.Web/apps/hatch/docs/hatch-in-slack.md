---
title: Hatch in Slack
section: Running agents
order: 60
summary: Connect a Slack workspace to Hatch from the Chat screen, by creating an app from Hatch's manifest and pasting its bot token.
---

Hatch can be connected to a Slack workspace. **Nothing is posted there yet.**
Connecting is the first step: it gives Hatch a workspace to speak in, so what
comes next has somewhere to go.

The **Chat** screen, at `/chat`, is where an admin does it. It is under
**Manage**, after **Users**, and only an admin sees it. A key never reads or
writes a workspace, because a bot token can speak for the install.

## Connect a workspace

1. Open **Manage → Chat**.
2. Press **Copy manifest**. The manifest is also shown in the box above the
   button, so it can be selected by hand where the browser will not allow a
   copy.
3. In Slack, create an app **from a manifest**, paste the manifest in, and pick
   the workspace the app is for.
4. Install the app to that workspace.
5. On the app's install page, copy the **bot token**. It is `xoxb-` followed by
   more characters, joined by dots.
6. Paste the token into **Bot token** on the Chat screen and press
   **Connect**.

Hatch asks Slack whose token it is and names the workspace from the answer, so
you type no name. The workspace then appears in the table with **Token set**
and when it was connected.

The box is a password field, and it is emptied after every attempt, whether or
not the token was accepted. Hatch never shows a token again. The table says
only that one is held.

## When Connect refuses

The sentence under the box is the reason, in Slack's or Hatch's own words.

- **Slack refuses the token.** It is wrong, was revoked, or was copied with part
  missing. Copy it again from the install page.
- **The workspace is already connected.** The sentence says to replace the
  token instead. Use **Replace token** on its row.

## Change a token

Use **Replace token** on the workspace's row: type the new token into the box in
that row and press the button. The token has to be for the same workspace. A
token for another one is refused, and the old token stays.

## Remove a workspace

**Remove** asks first, and names the workspace. Confirming removes the
workspace and everything Hatch holds for it, token included, in one step.
Nothing in Slack itself is touched. To connect it again, paste a token as
above.

## See also

- [People, roles and keys](people-roles-and-keys) says who counts as an admin.
