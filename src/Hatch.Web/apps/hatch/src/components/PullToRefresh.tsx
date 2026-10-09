import type { PullToRefreshState } from '../lib/usePullToRefresh';

const WORDS: Record<'pulling' | 'ready', string> = {
  pulling: 'Pull to reload',
  ready: 'Release to reload',
};

/**
 * The pull-to-refresh gesture's own indicator: distance and phase come from
 * usePullToRefresh and are owned nowhere else - the size and shape of
 * MessageState.tsx. Hidden from assistive tech: the gesture is a touch drag
 * with no keyboard equivalent, and a successful pull announces itself by
 * being a page load.
 */
export function PullToRefresh({ phase }: PullToRefreshState) {
  if (phase === 'idle') return null;

  return (
    <div className={`hatch-pull-indicator hatch-pull-indicator--${phase}`} aria-hidden="true">
      <span className="hatch-pull-indicator__icon">&#8595;</span>
      <span>{WORDS[phase]}</span>
    </div>
  );
}
