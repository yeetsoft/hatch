---
title: Opening the board and setting your name
section: Start here
order: 40
summary: What a fresh board looks like, and setting the name your work gets signed with.
---

Open that address. That is the board, and it is empty in a particular way: the
columns are there — Draft through Done, the ones every Hatch ships with — and
there is no project yet, so there is nothing for an issue key to be made of.

On a phone, the board opens on what is waiting on you and a New issue button
first, then the columns stacked one under another instead of side by side —
a Full board toggle, remembered per browser, brings the usual columns back.

A card that reaches Done does not sit there forever: by default it leaves the
board two days after it closes, so the column's count stays a true read of
what just shipped rather than a growing pile of everything that ever has. The
card is not gone — it closed, and can still be found by searching: press `/`
on the board and take the trailing **Search everything** row, or follow the
column's own `last 2 days` note, which opens Search already filtered to what
closed there.

**Manage → Projects → New project** is therefore the first thing to do. A
project is a key namespace rather than a container: give it a short key and
every issue filed under it is numbered from that key onwards (`HOME` gives you
`HOME-1`, `HOME-2`). One board holds every project you make, because switching
boards to find out what is next is the thing a folder of plan files already
did badly.

Unless you put up the wall (*Putting a front door on it*, below) there is no sign-in. A Hatch started this way runs with its wall off, which
means it does not ask who you are and does not have anywhere to look it up — so
it signs what you do with whatever your shell already called you (`USER` on
macOS and Linux, `USERNAME` on Windows), and calls you `friend` when neither is
set. The bar says which it used.

**Settings → Your name** changes it, and is worth doing before you file
anything. Every
comment, every move and every event on this board is signed with that name
forever afterwards, and a board where half the trail says `friend` is a board
that cannot answer "who did this." The field appears only on an install with
its wall off, which is every install that started from the line above unless
you put the wall up (*Putting a front door on it*, next).

**Manage → Projects** is also where a project is bound to the repositories its
tickets are about: add a remote, reorder them, and give any of them a base
branch, all from the project's row. The runner reads this rather than a
setting of its own, which is where it is headed - today it still starts from
`HATCH_ROOT` (*Settings*, below).
