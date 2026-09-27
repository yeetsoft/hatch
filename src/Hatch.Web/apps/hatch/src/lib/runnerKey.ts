import type { Me } from '../types';

/**
 * What the Runner page should say about a key, from who is looking.
 *
 * `Me.kind` is `person` only when the wall is up. Somebody who has not been
 * answered yet is `none` - the page is silent by design until it knows, so
 * nothing flashes a claim that turns out wrong. Only an Admin is sent to the
 * keys page; anyone else is told to ask, since the page would refuse them.
 */
export type KeyAdvice = 'needs-key-admin' | 'needs-key-user' | 'none';

export function keyAdvice(me: Me | null): KeyAdvice {
  if (me?.kind !== 'person') return 'none';
  return me.role === 'admin' ? 'needs-key-admin' : 'needs-key-user';
}
