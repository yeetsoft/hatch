---
title: Playbooks
section: Concepts
order: 50
summary: What a playbook row says, how one is matched to a move, and what its model, effort, budget and brief limit control.
---

A playbook is the instruction for one transition — one column to another, for
some issue types and shape — never for a job title. "Do the next increment" is
not one job: turning a paragraph of intent into an epic with stories under it
and picking up an already-specified task to write the code are different
thinking at different prices, and a playbook row is what tells a session which
of those it is doing this time, and how hard to think about it.

## The matrix

A playbook row is *(from column, to column, issue types, issue shape, project)
→ prompt, model, effort*. Shape is `any` (the issue's children are not
consulted), `leaf` (it has none) or `parent` (it has at least one), so a
parent's closeout and a childless issue's implementation can be two different
rows at two different prices even though both move the same column to the
same column for the same type.

A move is matched to a row in this order:

1. A row naming the issue's own project is picked before specificity is even
   asked, whatever either row names for type or shape — a project row never
   loses to a board-wide one, and a row naming a different project is never a
   candidate at all.
2. Once that is settled, or neither row names a project, specificity decides:
   naming the type is worth more than naming the shape, so type+shape beats
   type-only, type-only beats shape-only, and shape-only beats neither.
3. Ties between rows at the same specificity go to the older row.

So *Breakdown → Backlog, epics* can say something different from *Breakdown →
Backlog, anything else* without either row knowing the other exists: the epic
row is simply more specific, and wins whenever both could apply.

A repo-sourced project's own layer is a file on its trunk, not a table row —
its own `.hatch/playbooks.yaml` stands in for its project rows here, matched
by the same rules above, and a move the file has no row for falls through to
the board-wide table.

## Model and effort

`Model` is one of four aliases — `haiku`, `sonnet`, `opus`, `fable` — rather
than a pinned id, because a playbook that says "the big one" should still mean
it a year from now, and an alias is a tier each agent maps for itself, so it
means the same thing to every runner regardless of which one dispatches it.

A pinned id is different: it names one agent's own family rather than a tier,
matched against the one list of families Hatch knows — `claude-…` for Claude,
`gpt-…` for Codex, `grok-…` for Grok. **Pinning an id is choosing an agent.** A
runner whose own agent is not that family skips the row rather than running it
under the wrong CLI, and says so; the ticket then waits for a runner of that
agent, and a board with none leaves it sitting rather than guessing.

`Effort` is exactly what the agent CLI's own effort flag accepts — a value it
does not recognise is one the CLI refuses at spawn time, not one the Playbooks
page catches first.

## Budget, the brief limit and denied tools

**Budget** is a playbook row's own ceiling on one session, in millions of
tokens. A session is never told the number it is running against; the first
time its spend crosses it, the next hook hands back a wrap-up message exactly
once — finish the edit in hand, commit what is green, push it, file what is
left as tasks, say what landed, then stop. Crossing it again by half again
cancels the session outright, the same as a lost lease, because a budget
crossed twice over is not a decision left for the session itself to make.

**The brief limit** is the one that moves a ticket rather than spending
against it. A childless issue whose description is longer than its matched
row's brief limit is never dispatched under that row: the sendback hop carries
it back instead, with no session and nothing spent, to the column that feeds
the one where code gets written, because a brief that long has not been cut
into tasks yet. Where the board has no such column to send it back to, the
move is folded instead, naming the brief's length and the limit it is held to.

**Denied tools** is a list of Claude Code permission rules in deny position on
a row — a bare tool name such as `Edit`, or a scoped pattern such as
`Bash(git push *)` — passed to the agent CLI as one denied tool per entry. A
bare name removes only that one tool: a row that denies `Write` alone still
leaves a plain `Bash` call free to write the same file another way.

## Rows that edit, and rows that judge

A row does one of two things with the tree: it changes it, or it reports on it.
Writing the code and fixing a conflict are changing; breaking a note into tasks,
sharpening the criteria and verifying an epic against the trunk are reporting.

For all but one row, *judges* is by instruction only. Each judging row is told
in its prompt to specify, verify or report rather than to write the code, and
nothing but the prompt keeps a session to that. Two things go further:

- **The reflection row** carries a denied-tools list that takes away `Edit`,
  `Write`, `NotebookEdit` and the git writers. It keeps `Read`, a plain `Bash`
  and the `hatch` verbs, so it still reads the repository and moves its own
  ticket. It is not a check. A reflection also reads the hypotheses earlier
  reflections filed, and reports each as confirmed, refuted or unreadable in a
  `Hypotheses read` section that is always present.
- **A check playbook** is the only kind of row that cannot move its ticket at
  all. While its claim is held, a key's move of that ticket is refused; the
  session records a verdict, pass or fail with its findings, and the runner
  applies it afterwards. A pass sends the ticket forward. A fail sends it back to
  the column where code is written, with the findings as a comment, and a second
  fail in a row moves nothing and asks a person. No seeded row is a check.
  Only a person marks a playbook as a check, on the Playbooks page; a check left
  with no denied tools carries a badge reading "a check with no denied tools can
  edit" - a warning, and the save is not refused. When a verdict is applied, pass
  or fail, its findings are written on the ticket as a comment (`(no findings)`
  if the session wrote none), and the build session that follows a fail is handed
  the last check's findings.

Who moves work on each edge is set out in [the board and its
columns](the-board-and-its-columns#who-moves-work).

## The ten rows Hatch seeds

The column names below are the stock ones.

| Transition | Types / shape | What it does | Edits or judges |
|---|---|---|---|
| Breakdown → Backlog | epics | Turns a captured idea into an epic: rewrites the description, files stories under it and sequences them | Judges |
| Breakdown → Backlog | stories | Writes at most five acceptance criteria and cuts the plan into tasks; a story that needs more becomes an epic | Judges |
| Breakdown → Backlog | tasks and bugs | Formalises a note: what is wrong, where the code is, a pattern to copy, one criterion | Judges |
| Breakdown → Backlog | reflections | Reads the board's own history for a window, writes the report and files each recommendation | Judges, with denied tools |
| To Do → In Progress | epics | Chooses which story starts | Judges |
| To Do → In Progress | any type | Final analysis: sharpens the criteria, writes the implementation detail onto the ticket and files tasks | Judges |
| In Progress → In Review | any type, leaf | Writes the code, gets it green, pushes it and puts it up for review | **Edits** |
| In Progress → In Review | any type, parent | Closeout: verifies the ticket against the trunk once its children are done | Judges |
| In Progress → In Review | epics | Verifies the epic's own criteria against the trunk and files what is missing | Judges |
| In Review → In Review | every type | The review row: fixes a conflict or a failing build on the branch, and nothing else | **Edits** |

Ten rows are seeded, but a stock board ends with nine. The epic row on **To Do →
In Progress** is retired wherever the board pulls an epic across that move
itself, because a session told to choose what starts would be telling the board
what it has already decided. It goes only while its prompt is still the seeded
text; an edited row stays.

They are seeded because a Hatch whose loop cannot run until somebody fills in a
table is a Hatch that ships broken. With these rows, `go-to-work` needs no
configuration beyond an origin and a key. Models, efforts and budgets are left
out of the table on purpose: they are what an operator retunes, so the current
ones are read on the **Playbooks** screen.

Rows are joined to columns by *name*, so a board that renamed its columns first
seeds nothing. That is the right failure: a playbook wired to the wrong
transition is worse than an empty table. Nothing re-seeds a row afterwards
either. Deleting one is how an operator takes a column back from the loop, and
nothing puts it back.

## When one issue says the matrix is wrong about it

An issue can carry an optional model override and an optional effort override.
Each beats whichever row speaks for that issue, on **every** move it ever makes
rather than on one transition. The two are independent: set one and the other
still comes from the row. They reach that issue and nothing beneath it, and they
accept exactly what a row accepts, refused in the same words.

There is no per-issue prompt. A prompt is the method for a transition, and a
method for one issue is what the description is for.

Anyone who can read the issue can see its overrides. Setting one is a person's.

## Why an agent cannot edit a playbook

Reading the matrix accepts an API key, because that is how a session is told
what it was dispatched with. Every write is refused for API-key calls: creating
a row, changing one, deleting one, and setting an issue's overrides. A key that
tries gets a `403`. A request with no key is treated as the user and is allowed,
as is a browser session.

That refusal is deliberate. An agent that could widen its own instructions and
its own budget is a loop with no end. A session that thinks a row is wrong says
so on the ticket and stops. What a key may and may not do is in [people, roles
and keys](people-roles-and-keys#api-keys), and [what a session is
told](what-a-session-is-told) shows the same rule from the prompt's side.
