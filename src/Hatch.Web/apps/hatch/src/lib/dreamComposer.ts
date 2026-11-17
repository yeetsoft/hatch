/* The Dreams composer's decisions, apart from its markup: whether Source is
   showing, what a post does with the draft, and whether the posted text falls
   into the rule. Plain functions, so they are checked without a DOM. */

import { message } from './errors';

/** Source shows while it is pressed open, and stays showing while it holds
    text - a typed source is never hidden behind a control. */
export function sourceOpen(pressed: boolean, source: string): boolean {
  return pressed || source !== '';
}

export type PostOutcome =
  | { kind: 'skipped' }
  | { kind: 'posted'; body: string }
  | { kind: 'refused'; reason: string };

/** One post. The editor's own Ctrl+Enter can fire on an empty box, or twice,
    so an empty body or a post in flight is skipped. A refusal carries the
    reason and leaves the draft to the caller untouched. */
export async function submitDream(
  body: string,
  source: string,
  posting: boolean,
  onPost: (body: string, source: string | null) => Promise<void>,
): Promise<PostOutcome> {
  if (!body.trim() || posting) return { kind: 'skipped' };
  try {
    await onPost(body, source.trim() || null);
    return { kind: 'posted', body };
  } catch (err) {
    return { kind: 'refused', reason: message(err) };
  }
}

/** The posted text falls into the rule unless the person asked for less motion. */
export function fadesOnPost(reducedMotion: boolean): boolean {
  return !reducedMotion;
}
