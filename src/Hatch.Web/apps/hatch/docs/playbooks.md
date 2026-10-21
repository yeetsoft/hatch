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
