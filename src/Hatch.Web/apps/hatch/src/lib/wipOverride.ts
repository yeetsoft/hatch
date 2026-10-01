/* The WIP override: reading the refusal a full section sends back, and
   building the retry that carries `wipOverride: true`. Pure, so both pages'
   dialogs read the same sentence and send the same shape - see
   docs/hatch.md, "WIP". */

import { HttpError } from './errors';
import type { WipRefusal } from '../types';

/** The refusal a full WIP section sent back, or null when `err` is not one -
    a stale `fromStatusId` (a bare-string 409), any other status, or a plain
    Error with no body at all. */
export function wipRefusal(err: unknown): WipRefusal | null {
  if (!(err instanceof HttpError) || err.status !== 409) return null;
  const body = err.body;
  if (body === null || typeof body !== 'object') return null;

  const { error, load, limit } = body as { error?: unknown; load?: unknown; limit?: unknown };
  if (typeof error !== 'string' || typeof load !== 'number' || typeof limit !== 'number') return null;

  return { error, load, limit };
}

/** The same request, with the override flag set and every other field
    untouched. */
export function overridden<T extends object>(request: T): T & { wipOverride: true } {
  return { ...request, wipOverride: true };
}

/** The server's sentence, cased and punctuated for a dialog. The server's own
    sentence carries no terminal punctuation - see Wip.Sentence. */
export function refusalText(refusal: WipRefusal): string {
  const sentence = refusal.error;
  return `${sentence.charAt(0).toUpperCase()}${sentence.slice(1)}.`;
}

/** The event trail's line for a `wip_overridden` payload, or null when the
    payload does not carry what it needs - so `describe` falls back to its
    generic line rather than drawing a broken one. `epic` is present only when
    it was the epic's own limit (HA-112) that was stepped over, and names it. */
export function overrideLine(payload: { [key: string]: unknown } | null): string | null {
  if (!payload) return null;
  const { load, limit, to, epic } = payload;
  if (typeof load !== 'number' || typeof limit !== 'number' || typeof to !== 'string') return null;
  if (epic !== undefined && typeof epic !== 'string') return null;

  return typeof epic === 'string'
    ? `overrode ${epic}'s limit — ${load} of ${limit} into ${to}`
    : `overrode the WIP limit — ${load} of ${limit} into ${to}`;
}
