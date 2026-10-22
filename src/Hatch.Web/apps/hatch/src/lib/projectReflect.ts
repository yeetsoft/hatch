import { message } from './errors';
import type { Issue } from '../types';

/** Files the reflection the dialog's *Reflect now* button asks for, or reports
    the server's own refusal. `post` is `fileProjectReflection` in the running
    app and a stub in the test, the same way `fileBug`'s `post` is. The dedupe
    answer is just another `200`/`Issue`, so it needs no branch of its own -
    this cannot tell the two apart and should not try to. */
export async function fileReflection(
  projectId: number,
  post: (id: number) => Promise<Issue>,
): Promise<{ ok: true; issue: Issue } | { ok: false; error: string }> {
  try {
    return { ok: true, issue: await post(projectId) };
  } catch (err) {
    return { ok: false, error: message(err) };
  }
}
