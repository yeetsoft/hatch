import { describe, expect, it } from 'vitest';
import { describeMe } from './me';
import type { Me } from '../types';

const me = (over: Partial<Me>): Me => ({
  kind: 'local',
  name: 'friend',
  role: null,
  configured: false,
  canSignOut: false,
  canSignIn: false,
  ...over,
});

describe('describeMe', () => {
  it('draws nothing for nobody', () => {
    expect(describeMe(null)).toBeNull();
  });

  it('hints at an unnamed local person and offers nothing else', () => {
    expect(describeMe(me({}))).toEqual({
      name: 'friend',
      showHint: true,
      showSignOut: false,
      showSignIn: false,
      isAdmin: false,
    });
  });

  it('does not hint at a named local person', () => {
    expect(describeMe(me({ name: 'Ada', configured: true }))?.showHint).toBe(false);
  });

  it('offers a local person sign-in when an authority is configured', () => {
    expect(describeMe(me({ canSignIn: true }))?.showSignIn).toBe(true);
  });

  it('gives a person a sign-out and no hint, sign-in or admin links', () => {
    const view = describeMe(me({ kind: 'person', name: 'Ada', role: 'user', configured: true, canSignOut: true }));

    expect(view).toEqual({ name: 'Ada', showHint: false, showSignOut: true, showSignIn: false, isAdmin: false });
  });

  it('offers only sign-in to a browser that is nobody', () => {
    const view = describeMe(me({ kind: 'none', name: '', configured: true, canSignIn: true }));

    expect(view).toEqual({ name: '', showHint: false, showSignOut: false, showSignIn: true, isAdmin: false });
  });

  it('offers a browser that is nobody sign-in even when the API did not say so', () => {
    const view = describeMe(me({ kind: 'none', name: '', configured: true, canSignIn: false }));

    expect(view?.showSignIn).toBe(true);
  });

  it('knows an admin', () => {
    const view = describeMe(me({ kind: 'person', role: 'admin', configured: true, canSignOut: true }));

    expect(view?.isAdmin).toBe(true);
  });
});
