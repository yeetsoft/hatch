import { setIssueNoCode } from '../api/client';
import type { NoCodeRequest } from '../types';
import { message } from './errors';

/** Mirrors NoCodeRequest.MaxLength in src/Hatch.Contracts/Dtos.cs. */
export const NO_CODE_MAX_LENGTH = 200;

/** What the no-code field shows: this issue's own reason, blank for none. */
export function noCodeDraft(noCode: string | null): string {
  return noCode ?? '';
}

/** The field, trimmed for the wire: an empty string clears the flag. */
export function noCodeRequest(draft: string): NoCodeRequest {
  return { reason: draft.trim() };
}

/** Writes the no-code reason. Answers null when it went through, or the
    refusal in the server's own words - never throws. */
export async function writeNoCode(key: string, reason: string): Promise<string | null> {
  try {
    await setIssueNoCode(key, noCodeRequest(reason));
    return null;
  } catch (err) {
    return message(err);
  }
}
