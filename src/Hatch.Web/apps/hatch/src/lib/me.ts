import type { Me } from '../types';

/**
 * What the bar draws for the caller, decided apart from the drawing so
 * it can be tested without a rendered tree.
 */
export interface MeView {
  name: string;
  /** The "set your name" hint: only for the local person, who is the one that can set it. */
  showHint: boolean;
  showSignOut: boolean;
  showSignIn: boolean;
  isAdmin: boolean;
}

export function describeMe(me: Me | null): MeView | null {
  if (me === null) return null;

  return {
    name: me.name,
    showHint: me.kind === 'local' && !me.configured,
    showSignOut: me.canSignOut,
    showSignIn: me.canSignIn,
    isAdmin: me.role === 'admin',
  };
}
