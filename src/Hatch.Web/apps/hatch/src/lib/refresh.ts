/**
 * The decisions behind a page that refreshes itself, as plain functions.
 *
 * `useLoaded` is the wiring; what it may do and which answer it keeps are
 * decided here, where they can be tested without a DOM.
 */

/** How often the board re-asks while it is on screen.
 *
 *  Half a runner's heartbeat. A runner beats at a fifth of its lease - 60 s at
 *  the default 300 s - and that is how often a card's claim and its chatter can
 *  change, so 30 s keeps the board within one heartbeat of the runner. Between
 *  the runners page's 20 s (a control surface under a finger) and the attention
 *  bar's minute: most moves on this board are made by the loop, with nobody
 *  watching, and a read is a few cheap queries. */
export const POLL_MS = 30 * 1000;

/** Whether a timed or visibility-driven refresh may start now: only for a page
    that is on screen, and not while something local - a drag, a move on its way
    to the server - is holding the state still. */
export function mayRefresh({ visible, paused }: { visible: boolean; paused: boolean }): boolean {
  return visible && !paused;
}

/** The three members of `document` that `pollWhileVisible` uses, so a test can
    pass a stand-in without a DOM. */
export interface VisibilitySource {
  readonly visibilityState: DocumentVisibilityState | string;
  addEventListener(type: 'visibilitychange', listener: () => void): void;
  removeEventListener(type: 'visibilitychange', listener: () => void): void;
}

/**
 * Calls `load` on a timer for as long as the page is on screen, and not at all
 * while it is hidden.
 *
 * A tab nobody is looking at sends no timed request. Browsers slow a hidden
 * tab's timers but do not stop them, so the timer is cleared while hidden
 * rather than left to skip its ticks. When the page comes back, `load` is
 * called once at once and a fresh interval starts, so the next tick is a full
 * `everyMs` after the catch-up. A page that starts hidden asks nothing until it
 * is shown. Every case is decided by `mayRefresh`, the one rule that
 * `useLoaded` shares.
 *
 * Returns a function that stops it. An explicit reload is the caller's own
 * `load` and never passes through here.
 */
export function pollWhileVisible({
  load,
  everyMs,
  doc = document,
}: {
  load: () => void;
  everyMs: number;
  doc?: VisibilitySource;
}): () => void {
  let timer: ReturnType<typeof setInterval> | undefined;

  const stopTimer = () => {
    if (timer !== undefined) clearInterval(timer);
    timer = undefined;
  };

  const sync = () => {
    stopTimer();
    if (!mayRefresh({ visible: doc.visibilityState === 'visible', paused: false })) return;
    load();
    timer = setInterval(load, everyMs);
  };

  doc.addEventListener('visibilitychange', sync);
  sync();

  return () => {
    stopTimer();
    doc.removeEventListener('visibilitychange', sync);
  };
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
