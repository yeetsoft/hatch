import { useEffect, useState } from 'react';
import { Button, Field, Modal } from '@hatch/ui';
import { fileTrunkBuildBug, getAssignees, getStatuses } from '../api/client';
import { boardColumns } from '../lib/columns';
import { useIssueConfirmations } from '../lib/useIssueConfirmations';
import { defaultBugFields, fileBug, type BugFields } from '../lib/trunkBuildBug';
import { ExpressControl } from './ExpressControl';
import { PriorityControl } from './PriorityControl';
import type { AssigneeDirectory, Issue, Status } from '../types';

/**
 * *The confirm-and-file modal a failing trunk's bug asks for* - HA-406's
 * three fields (express, priority, which column), reviewed before filing
 * rather than filed outright the way `TrunkBuildRow`'s existing button
 * still does. Unreferenced by anything yet - that wiring is the next task.
 *
 * Self-fetching: nothing between `NavAttention` and here carries a column
 * list or a directory down to this depth, so both are fetched once, when
 * the dialog opens, rather than threaded through every page that might open
 * it.
 */
export function TrunkBuildBugDialog({
  open,
  buildId,
  onClose,
  onFiled,
}: {
  open: boolean;
  buildId: number;
  onClose: () => void;
  /** Called once filing succeeds, after confirm() - TrunkBuildRow's reload(). */
  onFiled: (created: Issue) => void;
}) {
  const [statuses, setStatuses] = useState<Status[] | null>(null);
  const [directory, setDirectory] = useState<AssigneeDirectory | null>(null);
  const [fields, setFields] = useState<BugFields>(() => defaultBugFields(null));
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const { confirm } = useIssueConfirmations();

  // Re-fetches every time the dialog opens rather than once on mount, so a
  // dialog opened for a different build - or reopened after a refusal -
  // always starts from a fresh read rather than a stale one.
  useEffect(() => {
    if (!open) return;
    setError(null);
    setSaving(false);
    setFields(defaultBugFields(null));
    let alive = true;
    Promise.all([getStatuses(), getAssignees().then((d) => d, () => null)]).then(([ss, dir]) => {
      if (!alive) return;
      setStatuses(ss);
      setDirectory(dir);
      setFields(defaultBugFields(ss));
    });
    return () => {
      alive = false;
    };
  }, [open]);

  async function confirmFile() {
    setSaving(true);
    const result = await fileBug(buildId, fields, fileTrunkBuildBug);
    setSaving(false);
    if (result.ok) {
      // Only here, on the way out of the success path - a refusal goes to
      // the branch below and raises nothing, and the dialog goes on showing
      // it with the modal still open, the same as NewIssueDialog's submit().
      confirm(result.issue);
      onFiled(result.issue);
      onClose();
    } else {
      setError(result.error);
    }
  }

  const columnChoices = statuses ? boardColumns(statuses).filter((s) => !s.isTerminal) : [];

  return (
    <Modal open={open} onClose={onClose} title="File a bug?">
      <div className="hatch-form">
        <ExpressControl
          express={fields.express}
          directory={directory}
          busy={saving}
          onChange={(express) => setFields((f) => ({ ...f, express }))}
        />

        <PriorityControl
          issueKey=""
          priority={fields.priority}
          priorityOwn={fields.priority}
          priorityFrom={null}
          directory={directory}
          busy={saving}
          onChange={(priority) => setFields((f) => ({ ...f, priority }))}
        />

        <Field label="Column">
          <select
            value={fields.statusId ?? ''}
            disabled={!statuses || saving}
            onChange={(e) => setFields((f) => ({ ...f, statusId: Number(e.target.value) }))}
          >
            {columnChoices.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </Field>

        {error && <p className="text-danger">{error}</p>}

        <div className="hatch-form-actions">
          <Button onClick={onClose}>Cancel</Button>
          <Button
            variant="primary"
            loading={saving}
            disabled={!statuses || fields.statusId == null}
            onClick={() => void confirmFile()}
          >
            File it
          </Button>
        </div>
      </div>
    </Modal>
  );
}
