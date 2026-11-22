<!-- Written by Hatch. Version 1. Change this text in Hatch, not here: a newer version replaces the file. -->
# Hatch

Hatch is the tracker this repository's work is planned in. People and agents
both work its tickets. A task you are given may be one, and a problem you
notice can become one.

## The command

`hatch` is the command. `hatch --help` lists what it does, and every
subcommand takes `-h`. `hatch config --show` says which Hatch it reaches.

If `hatch` is not installed, get it from the **Get the runner** page of the
Hatch itself. In a checkout of Hatch, `scripts/hatch.sh` is the same program.

The API key stays outside this repository. Never put it in a file, a commit,
an issue or a comment.

## File an issue

1. Find this repository's project. `git remote get-url origin` gives the
   remote. `hatch api GET /api/projects` lists the projects. Take the one
   whose `repositories` name that remote, as its `remote` or its `canonical`.
   Note its `id`.
2. File the issue. The body goes in on standard input, so the same line works
   in Git Bash, PowerShell and a POSIX shell:

       echo '{"projectId":<id>,"type":"story","title":"...","description":"..."}' | hatch api POST /api/issues -

   `type` is `epic`, `story`, `task` or `bug`. Use `bug` for something broken
   and `story` for something a person gets.
3. The answer carries the new issue's `key`. Tell whoever asked.

## How a ticket reads

A person reads it before anything else.

- The title is a handful of words that name the thing. Five is plenty.
- A story opens with one sentence: who gets what once it lands.
- Sentences are short and say who does what. No coined names. Spell out an
  acronym once.
- Put the acceptance criteria in the description, in a form somebody else
  could check.

## Not yet a ticket

A thought that is not ready to be one goes to the Dream Board:
`hatch dream "the thought, in a sentence or two"`.

## More

The Docs page of the Hatch itself has the long form: every command, and the API.
