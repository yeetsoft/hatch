import { statusVars } from '../lib/color';
import { PreApprovalControl, type PreApproval } from './PreApprovalControl';
import { PriorityControl, type Priority } from './PriorityControl';
import { StatusPicker } from './StatusPicker';
import type { AssigneeDirectory, Issue, Status } from '../types';

/**
 * Where this issue stands and how it is carried: the column it is in, how
 * urgent it is, and how much of its approval is banked.
 *
 * Status was a <select> in a row of five pickers, which made "what is the state
 * of this thing" - the first question anybody opens an issue with - the same
 * size as its ready date. Here it is the page's own band: the column it is in,
 * in that column's colour, as a pill that opens the board's columns on a press
 * - the same StatusPicker the board peek uses, so the two screens cannot
 * disagree about what is on offer. Priority and Pre-approval are its peers and
 * sit beside it, the same controls the peek draws, at the same size.
 *
 * Each control opens its own band under the row (`order: 1; flex-basis: 100%`,
 * see App.css), so the three are direct children of the bar rather than
 * wrapped with their labels.
 */
export function IssueStatusBar({
  issue,
  statuses,
  directory,
  canApprove,
  priorityBusy,
  preApprovalBusy,
  onMove,
  blocked,
  onPriority,
  onPreApproval,
}: {
  issue: Issue;
  statuses: Status[];
  /** Everybody who could own an issue, and who the caller is. Null while it is
      still loading. */
  directory: AssigneeDirectory | null;
  /** Whether the caller may set or lower preapproved on this issue's project. */
  canApprove: boolean;
  priorityBusy: boolean;
  preApprovalBusy: boolean;
  onMove: (statusId: number) => void;
  /** Why a move into this column is refused, or null where it is allowed. */
  blocked: (status: Status) => string | null;
  onPriority: (priority: Priority) => void;
  onPreApproval: (level: PreApproval) => void;
}) {
  const current = statuses.find((s) => s.id === issue.statusId);

  return (
    <section className="hatch-status-bar" style={statusVars(current?.color)} aria-label="Status">
      <span className="hatch-status-bar-label">Status</span>
      {current ? (
        // Keyed so following a parent or child link, which keeps this page
        // mounted, never carries an open band across issues.
        <StatusPicker
          key={issue.key}
          issueKey={issue.key}
          status={current}
          statuses={statuses}
          size="lg"
          onMove={onMove}
          blocked={blocked}
        />
      ) : (
        <span className="text-muted">unknown</span>
      )}

      <span className="hatch-status-bar-label">Priority</span>
      <PriorityControl
        key={`priority-${issue.key}`}
        size="lg"
        issueKey={issue.key}
        priority={issue.priority}
        priorityOwn={issue.priorityOwn}
        priorityFrom={issue.priorityFrom}
        directory={directory}
        busy={priorityBusy}
        onChange={onPriority}
      />

      <span className="hatch-status-bar-label">Pre-approval</span>
      <PreApprovalControl
        key={`preapproval-${issue.key}`}
        size="lg"
        issueKey={issue.key}
        preApproval={issue.preApproval}
        preApprovalOwn={issue.preApprovalOwn}
        preApprovalFrom={issue.preApprovalFrom}
        canApprove={canApprove}
        directory={directory}
        busy={preApprovalBusy}
        onChange={onPreApproval}
      />
    </section>
  );
}
