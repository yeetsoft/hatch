import { overrideLine } from './wipOverride';
import { DEFAULT_EPIC_WIP_LIMIT } from './wip';
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
     first - see docs/hatch.md, "The hop". Four kinds now carry it: express,
     a child its parent pulled (HA-149's `pulled`, never rendered before this -
     a pre-existing gap this closes rather than opens), an epic entering the
     WIP section, and a story or bug carried under a running epic. Rendered
     before the ordinary from/to line, which it is a variant of. */
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

  /* Filed under an express parent, and born express itself - see
     EfHatchIssue.Express. The parent's key rides the same `created` payload
     that already carries the type and the title. */
  if (event.kind === 'created' && typeof event.payload?.expressFrom === 'string') {
    return `express, from ${event.payload.expressFrom}`;
  }

  /* An override carries { limit, load, to } - no `from` - so it falls through
     to here unless it is read first. */
  if (event.kind === 'wip_overridden') return overrideLine(event.payload) ?? '';

  /* An epic's own ceiling on how many stories run at once - see
     EfHatchIssue.WipLimit. An unset side reads as the default rather than
     "none", the same way IssueCommands.ShowAsync prints it. */
  if (event.kind === 'wip_limit_changed') {
    const fromLimit = typeof from === 'number' ? from : DEFAULT_EPIC_WIP_LIMIT;
    const toLimit = typeof to === 'number' ? to : DEFAULT_EPIC_WIP_LIMIT;
    return `stories at once: ${fromLimit} → ${toLimit}`;
  }

  /* A takeover of a lease that lapsed rather than was let go: { from, heardAt },
     no `to` - the holder that stopped answering, and when it was last heard
     from. The claim_taken row right after it says who holds it now. */
  if (event.kind === 'claim_lapsed') {
    const heardAt = event.payload?.heardAt;
    return `${short(from)} stopped answering, last heard from ${short(heardAt)}`;
  }

  if (from === undefined && to === undefined) return '';
  return `${short(from)} → ${short(to)}`;
}

export function short(value: unknown): string {
  if (value === null || value === undefined) return 'none';
  const text = String(value);
  return text.length > 60 ? `${text.slice(0, 60)}…` : text;
}
