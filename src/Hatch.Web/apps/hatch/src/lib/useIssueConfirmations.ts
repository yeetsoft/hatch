import { createContext, useContext } from 'react';
import type { MoveRaise } from './confirmations';
import type { Issue } from '../types';

/**
 * Saying that something just happened to an issue - it was filed, or it was
 * dragged to another column - from wherever it happened, and taking a drag
 * back.
 *
 * The context and its hook rather than the component that provides them, in a
 * file of their own: a module that exports both a component and a hook cannot
 * be hot-reloaded, and the stack lives in that provider's state - editing the
 * chicklet would empty the corner every time. The same split @hatch/ui's
 * ThemeProvider takes, for the same reason. The provider and the region are in
 * components/Confirmations.tsx.
 */
export interface IssueConfirmations {
  /** Say that this issue was just filed. The created issue is what createIssue
      already returns, so nothing has to be fetched to draw the chicklet. */
  confirm: (issue: Pick<Issue, 'key' | 'title'>) => void;
  /** Say that a card was just dropped into another column, with the request
      that would put it back. Raised once the server has accepted the move. */
  moved: (move: MoveRaise) => void;
  /** Press Undo on one chicklet. The provider sends the request, so it works
      from whatever page the chicklet is showing on. */
  undo: (id: number) => void;
  /** Press Undo on the newest move that can still be taken back. True when
      there was one, so a keyboard handler knows whether it acted and should
      keep the browser's own handling of the key from also running. */
  undoNewest: () => boolean;
  /** Told after an undo has moved a card, so a screen drawing the board can
      catch up. Returns the way to stop listening. */
  onUndone: (listener: () => void) => () => void;
}

export const IssueConfirmationsContext = createContext<IssueConfirmations | null>(null);

/** What a surface that files, or moves, cards needs. The stack itself is
    nobody else's business - it is drawn by the provider and closed by the
    operator. */
export function useIssueConfirmations(): IssueConfirmations {
  const held = useContext(IssueConfirmationsContext);
  if (!held) throw new Error('useIssueConfirmations outside ConfirmationsProvider');
  return held;
}
