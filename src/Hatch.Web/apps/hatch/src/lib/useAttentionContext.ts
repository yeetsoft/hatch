import { createContext, useContext } from 'react';
import type { Attention } from '../types';

/**
 * What `useAttention()` answers, read through one poll rather than two - the
 * bar's control and the board's "Waiting on you" section must never disagree
 * about what is waiting, and a second hook call would be a second request
 * that could land a beat apart from the first.
 *
 * The context and its hook rather than the component that provides them, in a
 * file of their own, for lib/useIssueConfirmations.ts's own reason: a module
 * that exports both a component and a hook cannot be hot-reloaded, and the
 * poll lives in that provider's state. The provider is
 * components/AttentionProvider.tsx.
 */
export interface AttentionContextValue {
  attention: Attention | null;
  reload: () => Promise<void>;
}

export const AttentionContext = createContext<AttentionContextValue | null>(null);

/** What a surface drawing on what is waiting on a person needs. Thrown
    outside the provider, exactly as `useIssueConfirmations()` is. */
export function useAttentionContext(): AttentionContextValue {
  const held = useContext(AttentionContext);
  if (!held) throw new Error('useAttentionContext outside AttentionProvider');
  return held;
}
