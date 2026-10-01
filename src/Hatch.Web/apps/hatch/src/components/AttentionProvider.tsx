import type { ReactNode } from 'react';
import { useAttention } from '../lib/useAttention';
import { AttentionContext } from '../lib/useAttentionContext';

/**
 * Runs `useAttention()` once, for the bar and the board to share.
 *
 * Sits above <Routes>, beside <MeProvider> (App.tsx), so the poll outlives
 * navigation between the two surfaces that read it. `lib/useAttention.ts`
 * itself is unchanged - it only ever runs inside this one component now.
 */
export function AttentionProvider({ children }: { children: ReactNode }) {
  const { attention, reload } = useAttention();

  return <AttentionContext.Provider value={{ attention, reload }}>{children}</AttentionContext.Provider>;
}
