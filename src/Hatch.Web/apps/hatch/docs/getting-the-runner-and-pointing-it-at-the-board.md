---
title: Getting the runner and pointing it at the board
section: Running agents
order: 10
summary: Downloading the runner binary and pointing it at a board with `hatch config`.
---

The board is a board; the **runner** is what works the tickets. It is one
binary, it runs on your machine, and it talks to this Hatch over HTTP.

**Agents → Get the runner** is where it comes from. That page detects your
platform and offers the matching download — with the other three underneath,
for the machine you are not sitting at — and prints the revision it was built
from, which is the same commit as the image serving the page. The two are
never a version apart, because they came out of one build.

Put the file on your `PATH` as `hatch` (`hatch.exe` on Windows). On macOS and
Linux it needs marking executable first:

```
chmod +x hatch
```

Then the two commands the Runner page shows, with your own address already
filled into the first:

```
hatch config --origin <your-hatch-origin>
hatch go-to-work
```

`hatch config` writes the origin to a per-user file — mode 600, under the
platform's application-data directory, outside every repository — so it follows
you between checkouts and you do it once. A Hatch with its wall off needs no
key; `hatch config` asks for one anyway, and against this stack you can leave
it blank. With the wall up it needs one (*Putting a front door on it*, step 5).

Run `go-to-work` from inside a checkout of the repository the board is about.

[The `hatch` CLI, command by command](the-hatch-cli-command-by-command) is
the rest of what it takes — one increment, the loop, the bounds that stop it,
and the reads that spend nothing. The Runner page carries the same reference,
beneath the download.
