import type { AssigneeDirectory } from '../types';

/**
 * *Carried past a column marked to skip, with no session.* One control that
 * marks an issue express and, pressed again, unmarks it - drawn the same on
 * the issue page and on the peek, because it is the same fact and the same
 * press.
 *
 * A toggle button rather than a checkbox and rather than a pair of buttons,
 * for the reason ExpediteControl is one: the state it is in *is* the answer
 * to "is this express", so the control says so without the reader pressing
 * anything, and `aria-pressed` says the same thing to a screen reader. What it
 * sends is the state it wants and never "the other one" - see ExpressRequest
 * for why a toggle on the wire would race two browsers looking at the same
 * card.
 *
 * Presentational and fetching nothing, for the same reason ExpediteControl is:
 * the page loads the directory once beside its other reads and hands it down,
 * so a refusal lands in the page's own error line in the server's own words
 * and this never has an opinion about whether a press worked.
 */
export function ExpressControl({
  express,
  directory,
  busy = false,
  onChange,
}: {
  express: boolean;
  /** Everybody who could own an issue, and who the caller is. Null while it is still loading. */
  directory: AssigneeDirectory | null;
  /** A press is in flight. The button stays where it is and stops answering. */
  busy?: boolean;
  onChange: (express: boolean) => void;
}) {
  const me = directory?.me ?? null;

  /* `kind === 'person'` as well as "somebody is here": the write is closed to
     an API key, so a key holding this page would be offered a press that could
     only be refused. It still gets the state, drawn as a word rather than as a
     dead button - an agent reading this page is entitled to know why it was
     carried on without a session, and a control it cannot use is not how to
     tell it. */
  if (me?.kind !== 'person') {
    return (
      <span className={`hatch-express-said${express ? ' on' : ''}`}>
        {express ? 'Express' : 'Not express'}
      </span>
    );
  }

  return (
    <button
      type="button"
      className={`hatch-express${express ? ' on' : ''}`}
      aria-pressed={express}
      disabled={busy}
      title={
        express
          ? "Carried past every column marked Express skips, with no session, whenever it holds no open question. Changes no order. Press to unmark it."
          : 'Mark it express: carried past every column marked Express skips, with no session, whenever it holds no open question. Changes no order.'
      }
      onClick={() => onChange(!express)}
    >
      {express ? '» Express' : 'Express'}
    </button>
  );
}
