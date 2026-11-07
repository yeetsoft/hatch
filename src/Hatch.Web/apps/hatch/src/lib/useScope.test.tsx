import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { useScope } from './useScope';

/* No DOM test environment exists (see the header of lib/timeline.ts), so
   renderToStaticMarkup runs no effects - this is only the no-provider
   default, which is all that is checkable here. The provider's own effect
   is read in the driver, per the ticket. */
function Probe() {
  const { keys, projects, isScoped, inScope, setScope, ids, loaded } = useScope();

  return (
    <>{JSON.stringify([keys, projects, isScoped, inScope('ANY'), setScope([]) === undefined, ids, loaded])}</>
  );
}

describe('useScope with no provider', () => {
  it('answers the unscoped default', () => {
    const html = renderToStaticMarkup(<Probe />);

    expect(html).toBe(JSON.stringify([[], [], false, true, true, [], true]));
  });
});
