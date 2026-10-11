/* What the confirm-and-file modal needs to compute and to send, with no React
   in it so a node test can drive it the way menuState.ts drives the menu bar -
   see the comment on HA-409 for why. */

import { sendBack } from './columns';
import { message } from './errors';
import type { Priority } from '../components/PriorityControl';
import type { Issue, Status, TrunkBuildBugRequest } from '../types';

export interface BugFields {
  express: boolean;
  priority: Priority;
  statusId: number | null;
}

/** What the modal opens on - HA-406 criterion 2. `statuses` is null before
    the dialog's own fetch resolves, which is also why `statusId` is null
    then: there is no column to send yet, and nothing downstream may treat
    that as "leave it to the server's leftmost fallback". */
export function defaultBugFields(statuses: Status[] | null): BugFields {
  return { express: true, priority: 'expedited', statusId: statuses ? sendBack(statuses)?.id ?? null : null };
}

/** What a set of fields sends - HA-406 criterion 3, the half of it that is a
    pure mapping rather than a press. */
export function bugRequest(fields: BugFields): TrunkBuildBugRequest {
  return { express: fields.express, priority: fields.priority, statusId: fields.statusId };
}

/** Files the bug with exactly what the fields show, or reports the server's
    own refusal. `post` is `fileTrunkBuildBug` in the running app and a stub
    in the test, the same way menuState.ts takes `setTimeout` as a parameter
    instead of a test mocking the clock. */
export async function fileBug(
  buildId: number,
  fields: BugFields,
  post: (id: number, request: TrunkBuildBugRequest) => Promise<Issue>,
): Promise<{ ok: true; issue: Issue } | { ok: false; error: string }> {
  try {
    return { ok: true, issue: await post(buildId, bugRequest(fields)) };
  } catch (err) {
    return { ok: false, error: message(err) };
  }
}
