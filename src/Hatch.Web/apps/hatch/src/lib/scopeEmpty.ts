import type { Me } from '../types';

/**
 * Whether the caller's scope holds no project - the server's own reading, not
 * a guess from an empty list: a fresh install is unbounded and has no
 * projects yet, and that is not this. No answer, a 204 and a server that
 * predates `scope` all read false, so the page carries on as it always did;
 * the server enforces, and this only explains.
 */
export const scopeIsEmpty = (me: Me | null): boolean => me?.scope?.mode === 'none';
