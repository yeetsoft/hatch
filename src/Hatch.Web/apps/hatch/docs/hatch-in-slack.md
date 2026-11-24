---
title: Hatch in Slack
section: Running agents
order: 60
summary: A start-to-finish guide to Slack in Hatch. Make the app from a manifest, connect the workspace, bind projects, set channels up as rooms and send a test message, then see what each message looks like and what to do when something goes wrong.
---

This page takes you from no Slack app to a room that got a message from Hatch.
Follow [the steps](#set-up-slack-start-to-finish) in order. The rest of the page
is for after: [what a message looks like](#what-a-message-looks-like),
[what to do when something is wrong](#when-something-is-wrong), and
[what is not here yet](#not-here-yet).

Connecting gives Hatch a workspace to speak in. An admin binds projects to the
workspace and sets channels up as **rooms**. Each room takes the kinds of
message and the projects it should carry. The Chat screen itself posts only a
**test message**, to show that a room can be reached. The other messages come
from the board.

The **Chat** screen, at `/chat`, is where an admin does all of it. It is under
**Manage**, after **Users**, and only an admin sees it. A key never reads or
writes a workspace, because a bot token can speak for the install.

You need:

- A Hatch you sign in to as an admin.
- A Slack workspace where you may install an app.
- For messages that link back to tickets, `HATCH_PUBLIC_URL` set to the address
  people open Hatch at, for example `https://hatch.<domain>`. Without it,
  messages still go out, but name the ticket with no link. See
  [Links are missing](#links-are-missing).

## Set up Slack, start to finish

1. **Open the Chat screen.** Open **Manage → Chat**. With no workspace
   connected, it says **No workspace is connected.**
2. **Make the app from the manifest.** Press **Copy manifest**. The manifest is
   also shown in the box under **App manifest**, so it can be selected by hand
   where the browser will not allow a copy. In Slack, create an app **from a
   manifest**, paste the manifest in, and pick the workspace the app is for.
3. **Install the app to the workspace.** Use the install button on the app's
   page in Slack.
4. **Copy the bot token.** On the app's install page, copy the **bot token**. It
   is `xoxb-` followed by more characters, joined by dots.
5. **Paste it into Hatch.** Paste the token into **Bot token** on the Chat screen
   and press **Connect**. Hatch asks Slack whose token it is and names the
   workspace from the answer, so you type no name. It worked when the workspace
   appears in the table with **Token set** and when it was connected. The box is
   a password field and is emptied after every attempt, whether or not the token
   was accepted. Hatch never shows a token again: the table says only that one
   is held. If Connect refuses, see [Connect refuses](#connect-refuses).
6. **Bind a project.** The workspace's row has a **Projects** cell with a
   checkbox for every project you can see. Tick a project to bind it to the
   workspace. Each tick saves at once, and the list is disabled while it saves.
   Untick to unbind. Binding touches nothing in Slack. A room can only carry
   projects bound to its own workspace, and unbinding a project takes it out of
   every room's chosen list on that workspace.
7. **Set up a room.** Under the table, each workspace has a **Rooms on** card,
   named for it. Press **Add a room** to see the channels the app's token can
   see. Hatch asks Slack every time you press it, so a channel made a minute
   ago is there. While it asks, the card says it is reading. In the list:
   - A private channel is marked **Private**.
   - A channel the app has not joined says **Not invited yet. In Slack, open the
     channel and add Hatch.** Step 8 mends that.
   - A channel that is already a room says **Already a room**, and its **Add**
     button is off. A channel is one room only.

   Press **Add** beside a channel. A new room starts with every switch off and
   **Every bound project** chosen, so nothing is posted until you tick
   something. Each room has:
   - Four switches: **Changelog**, **Questions**, **Problems**, and **Problems
     that belong to no project**. A change saves at once.
   - A choice of projects. **Every bound project** carries whatever the
     workspace is bound to. **Chosen projects** shows a checkbox for each
     project bound to this workspace, and the room carries only the ones
     ticked. If **Chosen projects** is picked and none is ticked, the room says
     **This room carries no project.** It posts nothing for any project. It does
     not fall back to every project.

   **Remove room** asks first, and names the channel. Nothing in Slack is
   touched, and the channel can be added again.
8. **Invite the app to the channel.** Slack only lets the app post where it is a
   member. In Slack, open the channel and add Hatch to it. Then press **Add a
   room** again: the channel is no longer marked **Not invited yet**. You can
   do this before step 7 as well.
9. **Send a test message.** Press **Send a test message** on the room. It posts
   one short message to the channel, and the button is disabled while Hatch
   waits for Slack. Under it a line says **Sent** when the channel got it. Open
   the channel in Slack and read it: that is the room working. If the line says
   something else, see [The test message is refused](#the-test-message-is-refused).

A room posts nothing else until a switch is on and a project is carried. After
that, the board does the posting. [The next section](#what-a-message-looks-like)
shows what it posts.

## What a message looks like

Every message has a bold title, then a short body or facts, then a line of
links. The examples show the text, with `<domain>` standing for your Hatch's
address, and keys and titles made up. A message is posted once and then
**edited** as its subject changes, never posted again, so a channel reads as one
line per thing.

### A changelog message

When an issue with no parent closes, a room with **Changelog** on and the issue's
project hears of it. At first it has the title and the key:

```
story shipped: Tickets in a human voice
DEMO-12
```

The key is a link to `https://hatch.<domain>/issues/DEMO-12` when links are
on. When someone writes the release note, the same message is edited to carry it:

```
story shipped: Tickets in a human voice
A ticket now opens with one sentence saying who gets what.
DEMO-12
```

### A question

Asking a question posts a message to each room with **Questions** on and the
issue's project. It names the ticket, gives the question, lists the options with
the recommended one marked, and the level:

```
DEMO-12 Tickets in a human voice
Which of these should the title follow?
Five words (recommended)
One sentence
Level: hold
DEMO-12 · Answer it
```

When somebody answers, the same message is edited. It gains the latest answer
and who gave it:

```
DEMO-12 Tickets in a human voice
Which of these should the title follow?
Five words (recommended)
One sentence
Level: hold
Sam answered: Five words
DEMO-12 · Answer it
```

If every answer so far came from a key, the line says **A program answered**
rather than naming one. **Answer it** links to the Ballot. The detail is in
[Questions in a room](#questions-in-a-room).

### A problem

When [the goose](the-goose) opens a finding, a room with **Problems** on and the
finding's project hears of it. The finding's sentence is the title:

```
Messages to workspace Acme have not got through since 2026-01-05 09:30 UTC.
Since 2026-01-05 09:30 UTC
```

When the condition ends, the same message is edited to start with **Ended:** and
to say when it began and when it ended:

```
Ended: Messages to workspace Acme have not got through since 2026-01-05 09:30 UTC.
Began 2026-01-05 09:30 UTC
Ended 2026-01-05 11:10 UTC
```

The sentence is the goose's own, so it differs from finding to finding. A failing trunk build, a stopped goose and backups that need a person
look the same way, and are described under [Goose findings](#goose-findings),
[Goose and backup messages](#goose-and-backup-messages) and
[Trunk build messages](#trunk-build-messages). So is a runner that cannot do
something, under [Runner failure messages](#runner-failure-messages).

## When something is wrong

Each of these is what you see, then what to do.

### Connect refuses

The sentence under the box is the reason, in Slack's or Hatch's own words.

- **Slack refuses the token.** It is wrong, was revoked, or was copied with part
  missing. Copy it again from the app's install page.
- **The workspace is already connected.** The sentence says to replace the token
  instead. Use **Replace token** on its row, as in
  [Change a token](#change-a-token).

### Add a room shows a sentence

If Slack refuses when you press **Add a room**, its sentence shows in place of
the list of channels. Read it. A revoked token is mended by installing the app
again and using **Replace token**.

### The test message is refused

The line under **Send a test message** says why, in these words:

- **the app has not been invited to that channel; in Slack, add Hatch to it.**
  Invite the app, as in step 8.
- **that channel could not be found.** The channel was deleted or renamed away.
  Remove the room and add the channel again.
- **that channel is archived.** Unarchive it in Slack, or remove the room.
- **That token has been revoked in Slack; install the app again for a new
  one.** Install the app again, then use **Replace token**.
- **the app is missing a permission it needs; install it again.** Install the
  app again from the manifest.
- Anything else is shown as Slack gave it.

### Links are missing

Messages name their ticket. They link to the ticket only when Hatch knows the
address people open it at, which is `HATCH_PUBLIC_URL`, and compose passes it to
the API as `Hatch__PublicBaseUrl`. With it unset, the Chat screen says
**Messages name the ticket with no link.** Messages still go out and name the
ticket. To make them link, set `HATCH_PUBLIC_URL` and restart the API. Hatch does
not guess the address from the runner's, as it does for a pull request.

### Messages waiting or dropped

A workspace row's **Sending** cell shows how many messages are **waiting** to be
sent, how many were **dropped**, and the last sentence Slack refused one with,
as **Slack said:**, and how long ago. A workspace with none of the three shows
nothing there. The numbers refresh every half minute while the page is open. An
admin who may see only some projects counts only the messages about those
projects.

- **Waiting** means Hatch is retrying. Read **Slack said:**: it is nearly always
  a channel the app is not in, or a token that was revoked. Mend that, and the
  message goes out on the next try.
- **Dropped** means a message was unsent for a day and Hatch gave up on it. It
  is not sent later. Mend the cause, and the next change posts as usual.

### When Slack is down

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

### Sends that keep failing

When sends to a workspace or room keep failing, [the goose](the-goose) opens a
finding after `Hatch__GooseChatSendMinutes` of failing: 60 by default, and `0`
turns it off. See [the goose](the-goose#chat-sends-failing). The finding is
not posted to the room or workspace it is about, since that room cannot hear it.

## Not here yet

- **Direct messages** to a person (HA-2719). Today every message goes to a room.
- **Answering from Slack** (HA-2720). Today a question in a room links to the
  Ballot, and it is answered there.

## Change a token

Use **Replace token** on the workspace's row: type the new token into the box in
that row and press the button. The token has to be for the same workspace. A
token for another one is refused, and the old token stays.

## What each message says

The sections below are the detail behind [the examples](#what-a-message-looks-like).

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

## Goose and backup messages

Two problems belong to no project: [the goose](the-goose) having stopped, and
the backups needing a person. Both go to rooms with **Problems that belong to no
project** on, and to no other. A room with only **Problems** on hears findings
about its projects and none of these. A [runner failure](#runner-failure-messages)
goes to the same rooms.

The goose has stopped when its last sweep is older than three intervals, or,
with no sweep stored, when the server has been up that long. It is the same rule
the attention page uses. The message says **The goose has stopped** and **Since**
the time of the last sweep, or the time Hatch first saw it stopped when there was
never one. It links to the goose page. When the goose sweeps again, the message
is edited to start with **Ended:** and to say when it began and when it ended.

The backups need a person under the attention page's rules too: the last backup
failed, the folder cannot be read, or the newest copy is more than two days old.
The message says **The backups need a person** and why, and since when. If the
reason changes, say from a stale folder to a failed dump, the message is edited
with the new one. A backup problem ends only when a backup newer than the moment
the problem began exists. A failed attempt is remembered by one running copy of
Hatch alone, so another copy can look at the same folder and judge it healthy;
waiting for a newer backup keeps the message from ending and beginning again as
they disagree. When one exists, the message is edited to **Ended:**, with when it
began and when it ended.

Backups with no `Hatch__BackupPath` set are never posted. If the path is taken
away while a backup problem is standing, the problem is ended and the message is
left as it was, since there is nothing left to say about backups nobody asked for.

A goose that stops again, or backups that fail again, after one ended are a new
message, not an edit of the old one. Only one of each can stand at a time, even
with several copies of Hatch running.

Hatch checks both once every few seconds, as it sends messages. The limit is
plain: if the whole scheduler stops, the sender stops with it, and nothing inside
Hatch can say so. Watch the goose page, or something outside Hatch, for that.

## Runner failure messages

When a runner begins to fail at something, a room with **Problems that belong to
no project** on hears of it, in every workspace. A runner belongs to no project,
so a room with only **Problems** on hears of none. The switch says it also
carries the goose stopping, backups and runner failures.

Five kinds of failure post, each in the words `hatch runners` and the runner's
page use:

- **Cannot reach its forge**: the forge refused the runner's sign-in.
- **Cannot sign its commits**: commits are meant to be signed and git could not
  sign one.
- **Its agent is signed out**: the agent needs a person to sign it back in.
- **Its agent will not start.**
- **Paused by the board**: the board stood the runner down after several
  increments in a row ended in error without spending a token.

Running out of usage and the hourly token cap are not failures, and post nothing.

The message names the runner, says what it cannot do, and says since when. It
links to the runner's page, at `/runners/<name>`; without `HATCH_PUBLIC_URL` it
names the runner with no link:

```
mike-laptop: Cannot reach its forge
Since 2026-01-05 09:30 UTC
```

When a beat finds the failure gone, or the pause ends, the same message is
edited to start with **Ended:** and to say when it began and when it ended:

```
Ended: mike-laptop: Cannot reach its forge
Began 2026-01-05 09:30 UTC
Ended 2026-01-05 11:10 UTC
```

Each failure is one message, from when it begins to when it ends. A failure that
comes back is a new message. A runner that keeps reporting the same failure
changes nothing, even when what the tool said changes.

The message leaves out the checkout, and it leaves out what the tool said. The
checkout's path can name a repository, and what `gh` said can name a host or an
account, and everyone in the channel reads what is posted there. The runner's
page shows both to whoever may read that runner.

A runner that goes quiet does not edit its message. Nothing in Hatch ends a
failure for a runner that has stopped reporting, so the message keeps saying the
failure stands until the runner's next beat ends it. Hatch does not yet say that
a runner has gone quiet. A failure that was already open before Hatch could post
these is never posted, and its end is not either.

## Trunk build messages

When the build on the tip of a repository's trunk begins failing, a room that
has **Problems** on and carries any project that binds the repository gets a
message. It names the repository and the trunk, the first seven characters of
the commit, when the failure began, and each failed check as a link to its
build page. A check the forge gave no address for is named without a link. A
room hears of a failure once, however many of its projects bind the repository.

The message is edited in place while the failure changes: a new commit, or a
different set of failed checks, edits it. A trunk that is checked again with the
same commit and the same failures changes nothing. When the trunk passes, the
message is edited to **Trunk build passing again**, with when it began failing
and when it passed. A pass ends the message: a failure after that is a new
message, not an edit of the old one.

A trunk whose build is still running with a check already failed counts as
failing. A trunk that is still running with no check failed leaves the message
as it is, so a re-run does not end it early: the failure stands until the trunk
passes or fails again. A repository that no project binds is not posted
anywhere. A trunk that was already failing when Hatch learned to post these is
posted as a new failure the next time it is checked.

## Changelog messages in a room

A room with the **Changelog** switch on, and a project it carries, hears about
each issue of that project as it ships. The message says the issue's type and
title, for example **story shipped: Tickets in a human voice**. It shows the
release note under it when the issue has one, and names the issue's key, as a
link when [links are on](#links-in-messages).

- Only an issue with no parent is news, as in the changelog. A task or story
  closing under a parent posts nothing.
- A task Hatch files to bring a changelog file current posts nothing when it
  closes.
- An import posts nothing. Issues that arrive already closed are history.
- An issue posts once. Reopening it and closing it again adds no second post,
  and edits nothing if its words are the same.
- When the release note changes after the post, the message is edited to carry
  it. A note changed on an issue that was never announced posts nothing, so
  writing a note on an old issue does not announce it.
- A room added later hears nothing about issues that closed before it was
  added, and gets no edit to them either.

Every way of closing an issue counts: a move on the board, an edit, a bulk
edit, a merge, a landing on the trunk, and making a column a done column. The
message is queued in the same save as the close, and sent after, as
[the sending section](#when-slack-is-down) describes.

## Links in messages

A message names its ticket. It links to the ticket only when Hatch knows the
address people open it at, which is `HATCH_PUBLIC_URL`. Without it, the Chat
screen says messages name the ticket with no link, and [what to do](#links-are-missing) is above.

## Questions in a room

Asking a question - from a comment, or from a check that has failed twice in a
row - posts one message to each **Questions** room bound to the issue's
project. It names the ticket, the question itself, its options with the
recommended one marked (a question asked in prose shows no options), and its
level, and links to the ticket and to the Ballot.

Answering, overturning or changing the level edits that same message rather
than posting a new one. An answered message shows the latest answer alone, not
a history of earlier ones, and who gave it - unless every answer so far is a
key's, in which case it says a program answered rather than naming one. A
level change updates the message's level line alone.

A long question is cut to fit Slack's own limit, the same as any message, and
its links are always kept.

## Link your Slack account

Any signed-in person can tell Hatch which Slack member they are, in each
workspace. This is on **Settings**, at `/settings`, in the **Slack accounts**
section after **Home screen**. It is drawn only where a workspace is connected,
and a key never reads or writes it.

Each workspace is one row, with its name, where your link stands, and a button:

- **Not linked** offers **Link**.
- **Waiting** says which Slack member a message went to, and offers **Link**
  again, which sends a new message. A link that was not opened in a day reads
  **ran out** and offers **Link** the same way.
- **Linked as** a name offers **Unlink**. Unlinking asks first, and removes
  only what Hatch holds. Nothing in Slack is touched.

An admin can remove anybody's link on **Manage → Users**, in the **Slack**
column of that person's row. Removing a person removes their links too.
[People, roles and keys](people-roles-and-keys) has the page.

### How a member is offered

**Link** first asks which Slack member has an email you have signed in to Hatch
with. If one does, it is shown by name, with **Link as** that member, or **Pick
someone else**. If none does, or you pick someone else, a list of the
workspace's members opens. It is read once, when the list opens, and typing
filters it as you go, on name and display name, whatever the case. A row shows a
name and display name only, never an email. Where there is no sign-in at all,
Hatch has no email to match, so nobody is offered and the list opens at once.

### What the message says

Choosing a member sends that member one direct message from Hatch, titled
**Link your Slack account to Hatch**. It says who asked to link the account,
tells anyone who did not ask to ignore it, and carries a **Confirm the link**
link back to Settings. The link is good for a day. Open it while signed in to
Hatch, and the page confirms it, says **Linked as** the member above the section,
and takes the secret out of the address, so going back does not use it again.
Open it signed out and Hatch asks you to sign in first, then brings you back to
it.

### When it is refused

- **Hatch has no address to send.** `HATCH_PUBLIC_URL` is not set, so a message
  could not carry a link. The section says so and shows no **Link** button. See
  [Links are missing](#links-are-missing).
- **Five an hour.** Hatch sends at most five of these messages an hour for one
  person. Try again later.
- **Linked already, or held by somebody else.** If you are already linked in
  that workspace, unlink first. If that member is linked to somebody else, an
  admin can remove their link. The sentence on screen says which.
- **Slack refused.** The sentence is Slack's own, for instance that the app may
  not message that member. Nothing is stored, so nothing is left waiting.
- **The link was used, replaced or ran out.** A newer **Link** replaces an older
  message's link, and each works once. Press **Link** again for a new message.
- **The link belongs to somebody else's request.** It was sent for another
  person's request, so it did nothing here and your own link is as it was.
- **That workspace is not one you can see.** Reload the list.

## Remove a workspace

**Remove** asks first, and names the workspace. Confirming removes the
workspace and everything Hatch holds for it, token included, in one step.
Nothing in Slack itself is touched. To connect it again, paste a token as
above.

## See also

- [People, roles and keys](people-roles-and-keys) says who counts as an admin.
