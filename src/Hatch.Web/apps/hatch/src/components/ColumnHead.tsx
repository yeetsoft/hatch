import { LockIcon } from '@hatch/ui';
import { StatusDot } from './StatusPill';
import type { Status } from '../types';

/** Why a protected column's lock mark says what it says - the same sentence
    moveBlocked's approval-gate branch returns, written here as a second
    literal rather than shared: this is a static display fact about the
    column, not a move-permission check, and askingWords already recomputes
    its own sentence locally rather than sharing one. */
const LOCK_REASON = 'Only an owner or approver may move work in here';

/**
 * The row every column head draws, whether it is `Column`'s `<header>` or
 * `PhoneColumn`'s `<summary>`: the dot, the name, the count, the lock mark
 * where the column is protected, and the asking badge. Pulled out so it can
 * be tested with `renderToStaticMarkup` alone - `Column` and `PhoneColumn`
 * both sit inside dnd-kit's tree, and apps/hatch's vitest has no jsdom.
 */
export function ColumnHead({ status, count, asking }: { status: Status; count: number; asking: number }) {
  const askingWords = `${asking} card${asking === 1 ? '' : 's'} waiting on an answer`;

  return (
    <>
      <StatusDot status={status} />
      <span className="hatch-column-name">{status.name}</span>
      <span className="hatch-column-count">{count}</span>
      {status.isProtected && (
        <span className="hatch-column-lock" role="img" aria-label={LOCK_REASON} title={LOCK_REASON}>
          <LockIcon className="hatch-column-lock-glyph" />
        </span>
      )}
      {asking > 0 && (
        <span className="hatch-column-asking" role="img" aria-label={askingWords} title={askingWords}>
          ?{asking}
        </span>
      )}
    </>
  );
}
