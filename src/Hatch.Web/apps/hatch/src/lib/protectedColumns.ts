/* The one piece of logic every mover shares: whether a card may land in a
   given column, and the reason when it may not. Mirrors the two refusals
   IssuesController checks at every move call site - ProtectedMoveRefusalAsync
   before MergeColumnRefusal - so a caller who clears the first meets the
   second.

   Pure, taking the resolved project and issue rather than reading either off
   a hook, so every consumer (HA-414/415/416) asks the same question before a
   request is ever sent. Not lib/wip.ts's protectedBlocked/
   mergesPullRequestBlocked: those answer whether a flag may be ticked on the
   Statuses admin page, not whether a move may land here. */

import type { Issue, IssueCard, Project, Status } from '../types';

/** Why a move into `target` is refused, or null where it is allowed.
    Checked in the server's order: the approval gate first, so a caller who
    clears it still meets the merge gate - passing one does not excuse the
    other. A project not found in the caller's own list (null or undefined)
    fails closed, the same as a non-approver. */
export function moveBlocked(
  target: Status,
  project: Pick<Project, 'canApprove'> | null | undefined,
  issue: Pick<Issue | IssueCard, 'pullRequestUrl'>,
): string | null {
  if (target.isProtected && !project?.canApprove) {
    return 'Only an owner or approver may move work in here';
  }
  if (target.mergesPullRequest && !issue.pullRequestUrl) {
    return 'This column merges the pull request, and this issue has none';
  }
  return null;
}

/** Why a drop of the card in hand into `target` would be refused, or null
    where it would not. Resolves the dragging card's own project - not the
    column's - through `projectsByKey`, failing closed (the same refusal a
    non-approver gets) where that project is not in the map at all. Null
    where nothing is being dragged, since there is then nothing to check.
    A thin wrapper around `moveBlocked`, kept here rather than beside its one
    caller (BoardPage's `Column`) so it can be unit-tested with no DOM and no
    dnd-kit import. */
export function columnBlocked(
  target: Status,
  dragging: IssueCard | null,
  projectsByKey: Map<string, Project>,
): string | null {
  return dragging ? moveBlocked(target, projectsByKey.get(dragging.projectKey), dragging) : null;
}

/** Why a batch move of every selected result into `target` would be refused for
    at least one of them, or null where none would be. A batch is heterogeneous -
    each result can carry its own project and pull request - so a column is
    blocked for the whole control the moment any one *affected* result's own
    moveBlocked is non-null. A result already standing in `target` is not
    affected by a move there - IssuesController.StageEditAsync only runs either
    refusal when the status patch would actually change the status (`statusId
    != issue.StatusId`) - so it is skipped, the same no-op the server would make
    of it. The reason names the first blocking result's key, the same
    `<code>{key}</code> — {reason}` shape BulkPage's own outcome.failures list
    already uses, since a composed reason naming nobody would leave the caller
    guessing which of up to fifty results is the problem one. */
export function bulkBlocked(
  target: Status,
  issues: IssueCard[],
  projectsByKey: Map<string, Project>,
): string | null {
  for (const issue of issues) {
    if (issue.statusId === target.id) continue;
    const reason = moveBlocked(target, projectsByKey.get(issue.projectKey), issue);
    if (reason) return `${issue.key}: ${reason}`;
  }
  return null;
}
