/**
 * The decisions behind a page that refreshes itself, as plain functions.
 *
 * `useLoaded` is the wiring; what it may do and which answer it keeps are
 * decided here, where they can be tested without a DOM.
 */

/** Whether a timed or visibility-driven refresh may start now: only for a page
    that is on screen, and not while something local - a drag, a move on its way
    to the server - is holding the state still. */
export function mayRefresh({ visible, paused }: { visible: boolean; paused: boolean }): boolean {
  return visible && !paused;
}

/**
 * Whether an answer that has come back may be applied.
 *
 * An answer that is not the latest is dropped, always. A background answer
 * (the interval, `visibilitychange`, focus) is also dropped while paused, so a
 * refresh that began before a drag cannot land in the middle of it; it is
 * dropped rather than held, because the next tick makes it good. An explicit
 * one - the caller asked, after a move or an Undo - applies even while paused,
 * since the caller is the one holding the pause.
 */
export function mayApply({
  background,
  current,
  paused,
}: {
  background: boolean;
  current: boolean;
  paused: boolean;
}): boolean {
  return current && !(background && paused);
}

export interface Ledger {
  /** Start a load. The ticket it returns is current until anything newer happens. */
  begin(): number;
  /** Local state was written directly, so every load already on its way is stale. */
  invalidate(): void;
  /** Whether nothing has begun or invalidated since this ticket was issued. */
  isCurrent(ticket: number): boolean;
}

/** Latest answer wins: answers arrive in whatever order the network chooses, and
    only the newest load - begun after the last write of local state - may be applied. */
export function createLedger(): Ledger {
  let counter = 0;
  return {
    begin: () => ++counter,
    invalidate: () => void ++counter,
    isCurrent: (ticket) => ticket === counter,
  };
}
