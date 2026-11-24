---
title: Hatch in Slack
section: Running agents
order: 60
summary: Connect a Slack workspace to Hatch from the Chat screen, bind projects to it, set channels up as rooms, and send a test message.
---

Hatch can be connected to a Slack workspace. Connecting gives Hatch a workspace
to speak in. An admin then binds projects to the workspace and sets channels up
as **rooms**, each room taking the kinds of message and the projects it should
carry. The only thing the Chat screen posts is a **test message**, to show that
a room can be reached.

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

## Bind a project

A workspace row has a **Projects** cell with a checkbox for every project you
can see. Tick a project to bind it to the workspace, and untick it to unbind it.
Each tick saves at once, and the list is disabled while it saves. Binding
touches nothing in Slack. If the save is refused, the sentence shows under the
list.

A room can only carry projects bound to its own workspace. Unbinding a project
takes it out of every room's chosen list on that workspace.

## Set up a room

Under the table, each workspace has a **Rooms on** card, named for it. Press **Add a room** to
see the channels the app's token can see. Hatch asks Slack every time you press
it, so a channel made a minute ago is there. While it asks, the card says it is
reading. If Slack refuses, the sentence shows instead of a list.

- A channel the app has not joined says **Not invited yet. In Slack, open the
  channel and add Hatch.** See the next section.
- A private channel is marked **Private**.
- A channel that is already a room says **Already a room**, and its **Add**
  button is off. A channel is one room only.

Press **Add** beside a channel to set it up. A new room starts with every switch
off and **Every bound project** chosen, so nothing is posted until you tick
something.

Each room has:

- Four switches: **Changelog**, **Questions**, **Problems**, and **Problems
  that belong to no project**. A change saves at once.
- A choice of projects. **Every bound project** carries whatever the workspace
  is bound to. **Chosen projects** shows a checkbox for each project bound to
  this workspace, and the room carries only the ones ticked.

If **Chosen projects** is picked and none is ticked, the room says **This room
carries no project.** It posts nothing for any project. It does not fall back
to every project.

**Remove room** asks first, and names the channel. Nothing in Slack is touched,
and the channel can be added again.

## Invite the app to the channel

Slack only lets the app post where it is a member. In Slack, open the channel
and add Hatch to it. Then press **Add a room** again: the channel is no longer
marked **Not invited yet**.

## Send a test message

**Send a test message** on a room posts one short message to its channel. The
button is disabled while Hatch waits for Slack. Under it a line says **Sent**,
or says why not, in these words:

- **the app has not been invited to that channel; in Slack, add Hatch to it.**
  Invite the app, as above.
- **that channel could not be found.** The channel was deleted or renamed away.
  Remove the room and add the channel again.
- **that channel is archived.** Unarchive it in Slack, or remove the room.
- **That token has been revoked in Slack; install the app again for a new
  one.** Install the app again, then use **Replace token**.
- **the app is missing a permission it needs; install it again.** Install the
  app again from the manifest.
- Anything else is shown as Slack gave it.

## When Slack is down

Hatch writes a message into a queue in the same step as the change that causes
it, so a change on the board never fails because Slack is slow or away. A
sender then delivers the queue, and what it does when Slack refuses is this:

- A message that fails waits and is tried again after 10 seconds, then after
  twice the wait each time, up to 10 minutes.
- A `429` from Slack holds the whole workspace for the time Slack names, since
  every room in it is throttled together.
- An app that is not in the channel is retried too, because adding the app to
  the channel mends it; the message goes out on the next try.
- A message still unsent after a day is dropped, and counted on `/chat` as
  **dropped**.
- A finding opens after `Hatch__GooseChatSendMinutes` of failing (60 by
  default, and `0` turns it off). See [the goose](the-goose#chat-sends-failing).

## What the counts on Chat mean

Each workspace row has a **Sending** cell. It shows how many messages are
**waiting** to be sent, how many were **dropped**, and the last sentence Slack
refused one with, and how long ago. A workspace with none of the three shows
nothing there. The numbers refresh every half minute while the page is open.
An admin who may see only some projects counts only the messages about those
projects.

## Goose findings

When [the goose](the-goose) opens a finding, a room that has **Problems** on
and carries the finding's project gets a message with the finding's sentence
and the time the condition began. The message links to the issue the finding is
about, when it names one, and to the goose page. Without `HATCH_PUBLIC_URL` there
are no links.

The message is edited in place, never posted again. It changes only when the
finding's sentence changes, so a finding that stands unchanged for a day is one
message. When the condition ends and the finding closes, the message is edited
to start with **Ended:** and to say when it began and when it ended. A finding
that was open before a room was set up never gets an **Ended** edit in that
room, and neither does one that opened before Hatch could post findings.

A finding that names no project goes to rooms with **Problems that belong to no
project** on. A finding about failing sends is not posted to the room or the
workspace it is about, since that room cannot hear it.

## Links in messages

A message names its ticket. It links to the ticket only when Hatch knows the
address people open it at, which is `HATCH_PUBLIC_URL`. Without it, the Chat
screen says messages name the ticket with no link.

## Remove a workspace

**Remove** asks first, and names the workspace. Confirming removes the
workspace and everything Hatch holds for it, token included, in one step.
Nothing in Slack itself is touched. To connect it again, paste a token as
above.

## See also

- [People, roles and keys](people-roles-and-keys) says who counts as an admin.
