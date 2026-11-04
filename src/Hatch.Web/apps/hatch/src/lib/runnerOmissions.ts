import type { OmitRunnerRequest, RunnerOmission } from '../types';
import { message } from './errors';

/** The longest omission reason the API accepts (`OmitRunnerRequest.MaxLength`). */
export const OMISSION_REASON_MAX = 200;

/** Whether the omission is the issue's own row, not one inherited from an ancestor. */
export function isOwnOmission(o: RunnerOmission): boolean {
  return o.fromKey === null;
}

/** Who kept the runner off: itself, when it recused, otherwise whoever did. */
export function omissionWho(o: RunnerOmission): string {
  return o.by === o.runner ? 'recused itself' : `omitted by ${o.by}`;
}

/** The reason as written, or a plain statement that there is none. */
export function omissionReason(o: RunnerOmission): string {
  return o.reason ?? 'no reason given';
}

/** True when the runner list loaded and does not name `runner`; marks nothing while it has not. */
export function isNotARunner(runner: string, names: readonly string[] | null): boolean {
  return names !== null && !names.includes(runner);
}

/** The character count under the reason box. */
export function reasonCounter(draft: string): string {
  return `${draft.length}/${OMISSION_REASON_MAX}`;
}

/** The two fields trimmed for the wire; null when there is no runner name to omit. */
export function omissionDraft(runner: string, reason: string): { runner: string; reason: string } | null {
  const name = runner.trim();
  return name === '' ? null : { runner: name, reason: reason.trim() };
}

/** The read-only fact: each runner, with its reason and where it was inherited from. */
export function omissionsFact(list: readonly RunnerOmission[]): string {
  return list
    .map((o) => `${o.runner}${o.reason ? ` (${o.reason})` : ''}${o.fromKey ? ` · from ${o.fromKey}` : ''}`)
    .join('; ');
}

/** Posts an omission, resolving with the API's refusal sentence rather than throwing. */
export async function submitOmission(
  omit: (key: string, runner: string, request: OmitRunnerRequest) => Promise<unknown>,
  key: string,
  runner: string,
  reason: string,
): Promise<{ ok: true } | { ok: false; refusal: string }> {
  try {
    await omit(key, runner, { reason });
    return { ok: true };
  } catch (err) {
    return { ok: false, refusal: message(err) };
  }
}
