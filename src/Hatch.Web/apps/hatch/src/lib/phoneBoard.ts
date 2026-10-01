/* What the stacked layout at phone width draws, and what it remembers - pure
   functions so AC6 can pin each one without a render. The board page itself
   decides when to use them; nothing here reads the viewport. */

import type { Status } from '../types';
import { boardColumns, isSettled } from './columns';

/** The order the stacked layout's sections draw in - the board's own column
    order, re-exported so every phone-layout decision (this, startsCollapsed,
    the view choice) has one home. Deliberately not a second implementation:
    BoardPage's desk columns already come from boardColumns, and the two
    layouts must never be able to disagree about what order the board is in. */
export const columnSections = boardColumns;

/** Whether a column's section starts collapsed: shipped or shelved work is
    read, not worked - lib/columns.ts's isSettled, for the same two reasons it
    already states. boardColumns excludes a deferred column from the board
    before this is ever asked, so only isTerminal fires in practice today;
    isSettled is what AC1 asks for, so this keeps answering correctly if that
    ever stops being true. */
export function startsCollapsed(status: Status): boolean {
  return isSettled(status);
}

/** Where the view choice is persisted - namespaced, the way the theme's own
    hatch.theme is (packages/ui/src/theme/themeStore.ts). */
export const BOARD_VIEW_STORAGE_KEY = 'hatch.board.view';

export type BoardView = 'stacked' | 'full';

/** The stored choice, or 'stacked' - try/catch for the same reason
    readLifetime does one: localStorage throws in a partitioned or
    locked-down context, and the board still has to render. */
export function readBoardView(): BoardView {
  try {
    return window.localStorage.getItem(BOARD_VIEW_STORAGE_KEY) === 'full' ? 'full' : 'stacked';
  } catch {
    return 'stacked';
  }
}

/** Persists the choice. A store that cannot be written is not worth
    surfacing: the choice holds until the next reload, as the theme's does. */
export function writeBoardView(view: BoardView): void {
  try {
    window.localStorage.setItem(BOARD_VIEW_STORAGE_KEY, view);
  } catch {
    /* See above. */
  }
}

/** Which layout actually draws. The stored choice only ever matters at phone
    width - AC7 keeps the desk exactly what it is today whatever is stored. */
export function resolveBoardLayout(isPhone: boolean, view: BoardView): BoardView {
  return isPhone ? view : 'full';
}
