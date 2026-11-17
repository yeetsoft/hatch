import type { ReactNode } from 'react';
import { EmptyState } from '@hatch/ui';
import { scopeIsEmpty } from '../lib/scopeEmpty';
import { useMe } from '../lib/useMe';

export const NO_SCOPE_MESSAGE = 'No project is in your scope; an admin sets it on Manage → Users.';

/**
 * Stands in front of a page that is about projects. A caller whose scope holds
 * none gets one sentence in place of the page, not a blank board and not an
 * error. The gate returns before the page's own hooks run, so nothing behind
 * it fetches. Everyone else gets the page, children untouched.
 */
export function ScopeGate({ children }: { children: ReactNode }) {
  const { me } = useMe();

  if (scopeIsEmpty(me))
    return (
      <div className="hatch-page">
        <EmptyState message={NO_SCOPE_MESSAGE} />
      </div>
    );

  return <>{children}</>;
}
