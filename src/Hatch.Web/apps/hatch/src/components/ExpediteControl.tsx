import type { AssigneeDirectory } from '../types';

type Priority = 'normal' | 'expedited' | 'emergency';

const NEXT: Record<Priority, Priority> = {
  normal: 'expedited',
  expedited: 'emergency',
  emergency: 'normal',
};

const WORD: Record<Priority, string> = {
  normal: 'Normal',
  expedited: 'Expedited',
  emergency: 'Emergency',
};

/**
 * *This one first, or further.* The three-way: one control that steps an
 * issue up through normal, expedited and emergency and wraps back to normal -
 * drawn the same on the issue page and on the board's summary, because it is
 * the same fact and the same press.
 *
 * A single button that cycles rather than three buttons or a select: the
 * reasoning that made this a toggle at two states still holds at three - the
 * state it is in *is* the answer to "how urgent is this", so the control says
 * so without the reader pressing anything. What it sends on every press is the
 * level it is moving *to*, named, and never "next" or "the other one" - see
 * setPriority for why a bare step on the wire would race two browsers looking
 * at the same card.
 *
 * Presentational and fetching nothing: the page loads the directory once
 * beside its other reads and hands it down, so a refusal lands in the page's
 * own error line in the server's own words and this never has an opinion
 * about whether a press worked.
 */
export function ExpediteControl({
  priority,
  inheritedFrom = null,
  directory,
  busy = false,
  onChange,
}: {
  /** This issue's own level - what a press cycles, never the effective one. */
  priority: Priority;
  /** The ancestor the effective level came from, or null when it is this
      issue's own. Display only - a press still cycles `priority` unchanged. */
  inheritedFrom?: string | null;
  /** Everybody who could own an issue, and who the caller is. Null while it is still loading. */
  directory: AssigneeDirectory | null;
  /** A press is in flight. The button stays where it is and stops answering. */
  busy?: boolean;
  onChange: (priority: Priority) => void;
}) {
  const me = directory?.me ?? null;
  const inherited = inheritedFrom ? ` · inherited from ${inheritedFrom}` : '';

  /* `kind === 'person'` as well as "somebody is here": the write is closed to
     an API key, so a key holding this page would be offered a press that could
     only be refused. It still gets the state, drawn as a word rather than as a
     dead button - an agent reading this page is entitled to know why its
     ticket was reached first, and a control it cannot use is not how to tell
     it. */
  if (me?.kind !== 'person') {
    return (
      <span
        className={`hatch-expedite-said${priority !== 'normal' ? ' on' : ''}${priority === 'emergency' ? ' emergency' : ''}`}
      >
        {WORD[priority]}
        {inherited}
      </span>
    );
  }

  const title =
    priority === 'emergency'
      ? 'The highest priority: goes first, above everything expedited, and the dispatcher considers it before anything else. Press to return it to normal.'
      : priority === 'expedited'
        ? 'This one goes first: it floats to the top of its column and the dispatcher reaches for it before anything else. Press to mark it emergency.'
        : 'Mark this one first: it floats to the top of its column and the dispatcher reaches for it before anything else.';

  return (
    <button
      type="button"
      className={`hatch-expedite${priority !== 'normal' ? ' on' : ''}${priority === 'emergency' ? ' emergency' : ''}`}
      disabled={busy}
      title={title}
      onClick={() => onChange(NEXT[priority])}
    >
      {priority === 'emergency' ? '🚨 Emergency' : priority === 'expedited' ? '↑ Expedited' : 'Expedite'}
      {inherited}
    </button>
  );
}
