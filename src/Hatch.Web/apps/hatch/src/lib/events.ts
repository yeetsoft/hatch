import { overrideLine } from './wipOverride';
import type { IssueEvent } from '../types';

/**
 * One line saying what an event did. Long values are cut rather than wrapped -
 * a description edit carries both whole texts in its payload, and the trail is
 * a list of what happened, not a diff viewer.
 *
 * In `lib/` rather than on the issue page, so it can be tested with no DOM -
 * the web test run has neither, and any decision that grows lives here.
 */
export function describe(event: IssueEvent): string {
  const { from, to } = event.payload ?? {};

  /* A delivery names the runner it was handed to and nothing it changed from. */
  if (event.kind === 'message_delivered') return to === undefined ? '' : `to ${short(to)}`;

  /* The hop: a move the loop made itself, with nobody reading the ticket
     first - see docs/hatch.md, "The hop". Six kinds now carry it: express,
     a child its parent pulled (HA-149's `pulled`, never rendered before this -
     a pre-existing gap this closes rather than opens), an epic entering the
     WIP section, a story or bug carried under a running epic, a childless
     issue whose brief is over its playbook's BriefLimit sent back a column
     (HA-394's `sendback`, carrying the brief's length and the limit it
     crossed), and an approve or close hop carried by a pre-approval
     (WorkController's `HopKinds.Approve`/`.Close`, both writing the same
     `preapproved` flag), and a close the project's own setting made, with no
     pre-approval behind it (`approved: 'setting'`), and a declared childless
     ticket the project's no-code setting closed (`approved: 'nocode'`). Beside them, and not a hop:
     a pass WorkController's `VerdictWork` lands in the merge column, because
     the project has *A passing check approves the merge* on, carries
     `approved: 'check'` - a person never reads the ticket before it moves -
     but is rendered here too, as another variant of the plain from/to line.
     Rendered before the ordinary from/to line, which it is a variant of. */
  if (event.kind === 'status_changed' && event.payload?.express === true) {
    return `${short(from)} → ${short(to)}, express`;
  }
  if (event.kind === 'status_changed' && event.payload?.pulled === true) {
    return `${short(from)} → ${short(to)}, parent pulled`;
  }
  if (event.kind === 'status_changed' && event.payload?.epic === true) {
    return `${short(from)} → ${short(to)}, epic`;
  }
  if (event.kind === 'status_changed' && typeof event.payload?.under === 'string') {
    return `${short(from)} → ${short(to)}, under ${short(event.payload.under)}`;
  }
  if (event.kind === 'status_changed' && event.payload?.sendback === true) {
    const { length, limit } = event.payload;
    return typeof length === 'number' && typeof limit === 'number'
      ? `${short(from)} → ${short(to)}, sent back - brief is ${length} characters, limit ${limit}`
      : `${short(from)} → ${short(to)}, sent back`;
  }
  if (event.kind === 'status_changed' && event.payload?.preapproved === true) {
    return `${short(from)} → ${short(to)}, pre-approved`;
  }
  if (event.kind === 'status_changed' && event.payload?.approved === 'setting') {
    return `${short(from)} → ${short(to)}, closed by the project's setting`;
  }
  if (event.kind === 'status_changed' && event.payload?.approved === 'nocode') {
    return `${short(from)} → ${short(to)}, closed by the project's no-code setting`;
  }
  if (event.kind === 'status_changed' && event.payload?.approved === 'check') {
    return `${short(from)} → ${short(to)}, approved by a passing check`;
  }

  /* express_changed is retired - HA-538 renamed its controller - but old rows
     still carry the boolean pair it used to write. expressFact's own words. */
  if (event.kind === 'express_changed') {
    return `${from ? 'Express' : 'Not express'} → ${to ? 'Express' : 'Not express'}`;
  }

  /* A created row from before HA-469 named an express parent by key rather
     than carrying from/to at all. */
  if (event.kind === 'created' && typeof event.payload?.expressFrom === 'string') {
    return `express, inherited from ${short(event.payload.expressFrom)}`;
  }

  /* An override carries { limit, load, to } - no `from` - so it falls through
     to here unless it is read first. */
  if (event.kind === 'wip_overridden') return overrideLine(event.payload) ?? '';

  /* An epic's own ceiling on how many stories run at once - see
     EfHatchIssue.WipLimit. An unset side reads as "no limit", the same way
     IssueCommands.ShowAsync prints it. */
  if (event.kind === 'wip_limit_changed') {
    const fromLimit = typeof from === 'number' ? from : 'no limit';
    const toLimit = typeof to === 'number' ? to : 'no limit';
    return `stories at once: ${fromLimit} → ${toLimit}`;
  }

  /* A takeover of a lease that lapsed rather than was let go: { from, heardAt },
     no `to` - the holder that stopped answering, and when it was last heard
     from. The claim_taken row right after it says who holds it now. */
  if (event.kind === 'claim_lapsed') {
    const heardAt = event.payload?.heardAt;
    return `${short(from)} stopped answering, last heard from ${short(heardAt)}`;
  }

  /* project_changed carries { from, to } on every issue the move touched,
     plus { descendants } on the root - a count of how many others moved
     with it. No descendants, or zero, falls through to the generic line. */
  if (event.kind === 'project_changed' && typeof event.payload?.descendants === 'number' && event.payload.descendants > 0) {
    return `${short(from)} → ${short(to)}, ${event.payload.descendants} more moved with it`;
  }

  /* stalled carries { why } - no from/to - so it falls through to the blank
     line below unless it is read first. held/resumed carry { from, to } as
     booleans, which the generic line would draw as "false → true": technically
     not wrong, just not a sentence worth reading next to a badge that already
     says "held". */
  if (event.kind === 'stalled') return typeof event.payload?.why === 'string' ? event.payload.why : '';
  if (event.kind === 'held' || event.kind === 'resumed') return '';

  /* stall_declined carries { why, because } - the board left a runner's stall
     mark off because the session spent nothing (see docs/hatch.md, "The stall
     mark"). The runner's own sentence follows when it sent one. */
  if (event.kind === 'stall_declined') {
    const why = typeof event.payload?.why === 'string' ? event.payload.why : '';
    const lead = 'No stall mark: the session spent nothing';
    return why === '' ? lead : `${lead} (the runner said: ${short(why)})`;
  }

  /* ordered carries { runner, by } - no from/to - a person's own order from
     the Runners page, placed on the issue it names. */
  if (event.kind === 'ordered') {
    const { runner, by } = event.payload ?? {};
    return typeof runner === 'string' && typeof by === 'string' ? `ordered ${runner} to work, by ${by}` : '';
  }

  /* A capability event names the row it touched, by position and words. An add
     and a removal carry { id, position, statement }; an edit carries whole
     { statement, howToTell } values each side; a reorder carries the ids in
     the old and the new order - no words, so it says how many moved. */
  if (event.kind === 'capability_added' || event.kind === 'capability_removed') {
    const { position, statement } = event.payload ?? {};
    const verb = event.kind === 'capability_added' ? 'added as' : 'removed from';
    return typeof position === 'number' ? `${verb} #${position}: ${short(statement)}` : '';
  }
  if (event.kind === 'capability_edited') {
    const { position } = event.payload ?? {};
    const was = from as { statement?: unknown; howToTell?: unknown } | undefined;
    const now = to as { statement?: unknown; howToTell?: unknown } | undefined;
    if (typeof position !== 'number' || was === undefined || now === undefined) return '';
    const changed = [was.statement !== now.statement ? 'statement' : null, was.howToTell !== now.howToTell ? 'how to tell' : null]
      .filter((x) => x !== null)
      .join(' and ');
    return `#${position}: ${changed} changed, now ${short(now.statement)}`;
  }
  if (event.kind === 'capability_moved') {
    const before = Array.isArray(from) ? from : [];
    const after = Array.isArray(to) ? to : [];
    const moved = after.filter((id, i) => before[i] !== id).length;
    return `${moved} of ${after.length} capabilities moved`;
  }

  /* A capability verdict carries { id, position, verdict, sha, author }. */
  if (event.kind === 'capability_judged') {
    const { position, verdict, sha } = event.payload ?? {};
    if (typeof position !== 'number' || typeof verdict !== 'string' || typeof sha !== 'string') return '';
    return `#${position} judged ${verdict} at ${sha.slice(0, 7)}`;
  }

  /* An outcome on this epic, from a change to the ambition above it. Carries
     { ambition, changeEventId, outcome, why } - no from/to. */
  if (event.kind === 'change_outcome_recorded') {
    const { ambition, outcome, why } = event.payload ?? {};
    const words: Record<string, string> = {
      carries_on: 'carries on',
      not_touched: 'not touched',
      serves_nothing: 'serves nothing',
    };
    if (typeof ambition !== 'string' || typeof outcome !== 'string' || words[outcome] === undefined) return '';
    return `change to ${short(ambition)}: ${words[outcome]} - ${short(why)}`;
  }

  /* A serves change carries the capability ids each side. Either side may be
     empty, which the generic arrow would draw as a bare blank. */
  if (event.kind === 'serves_changed') {
    const before = Array.isArray(from) ? from.length : 0;
    const after = Array.isArray(to) ? to.length : 0;
    return `serves ${after} ${after === 1 ? 'capability' : 'capabilities'}, was ${before}`;
  }

  if (from === undefined && to === undefined) return '';
  return `${short(from)} → ${short(to)}`;
}

export function short(value: unknown): string {
  if (value === null || value === undefined) return 'none';
  const text = String(value);
  return text.length > 60 ? `${text.slice(0, 60)}…` : text;
}
