/* Which issue the console's Status commands act on.

   The one in view: the page's own issue if the page is one, else the board's
   peeked card, else the card that had keyboard focus when `/` was pressed. The
   order is the order of what the person is looking at most closely - a peek is
   a dialog over the board, and a dialog is where their attention is. Pure and
   tested in node; ConsoleProvider reads the DOM and the pages, and only asks
   this which one wins. */

export interface InView {
  /** The issue page's own key; null on any other page. */
  pageKey: string | null;
  /** The card the board's peek is open on, or null. */
  peekedKey: string | null;
  /** The card that had focus when the console opened, or null. */
  focusedKey: string | null;
}

/** The issue the person is looking at, or null where none is. */
export function issueInView({ pageKey, peekedKey, focusedKey }: InView): string | null {
  return pageKey ?? peekedKey ?? focusedKey ?? null;
}

/** Anything with `closest` and, on what it finds, `getAttribute` - an Element,
    or a fake in a node test with no DOM. */
export interface Closest {
  closest: (selector: string) => { getAttribute: (name: string) => string | null } | null;
}

/** The key of the issue card the element is, or is inside; null for nothing,
    or for anything that is not a card. */
export function focusedIssueKey(el: Closest | null | undefined): string | null {
  return el?.closest('[data-issue-key]')?.getAttribute('data-issue-key') ?? null;
}
