import { setReleaseNote } from '../api/client';
import { message } from './errors';

/** Whether the issue page draws the Release note section at all - a child
    with no note has nothing to show there, but a top-level issue always
    carries the control to add one. See HA-659's AC5. */
export function releaseNoteShown(issue: { parentKey: string | null; releaseNote: string | null }): boolean {
  return issue.parentKey === null || (issue.releaseNote ?? '').trim() !== '';
}

/** Writes the note. Answers null when it went through, or the refusal in the
    server's own words - never throws. */
export async function writeReleaseNote(key: string, note: string): Promise<string | null> {
  try {
    await setReleaseNote(key, note);
    return null;
  } catch (err) {
    return message(err);
  }
}
