import { useEffect, useState } from 'react';
import { Button, Field, Modal } from '@hatch/ui';
import { fileTrunkBuildBug, getAssignees, getProjects, getStatuses } from '../api/client';
import { boardColumns } from '../lib/columns';
import { useIssueConfirmations } from '../lib/useIssueConfirmations';
import { canApproveBug, defaultBugFields, fileBug, loadBugChoices, type BugFields } from '../lib/trunkBuildBug';
import { PreApprovalControl } from './PreApprovalControl';
import { PriorityControl } from './PriorityControl';
import type { AssigneeDirectory, Issue, Project, Status } from '../types';

/**
 * *The confirm-and-file modal a failing trunk's bug asks for* - HA-406's
 * three fields (pre-approval, priority, which column), reviewed before
 * filing. Pre-approval and priority share a row, as in the New issue dialog;
 * Pre-approved is pressable for an owner or approver of `projectKey`, the
 * project that binds the trunk, and locked for anyone else.
 *
 * Self-fetching: nothing between `NavAttention` and here carries a column
 * list, a directory or the projects down to this depth, so all three are
 * fetched once, when the dialog opens, rather than threaded through every
 * page that might open it.
 */
export function TrunkBuildBugDialog({
  open,
  buildId,
  projectKey,
  onClose,
  onFiled,
}: {
  open: boolean;
  buildId: number;
  /** The project that binds the trunk, or null when none does. */
  projectKey: string | null;
  onClose: () => void;
  /** Called once filing succeeds, after confirm() - TrunkBuildRow's reload(). */
  onFiled: (created: Issue) => void;
}) {
  const [statuses, setStatuses] = useState<Status[] | null>(null);
  const [directory, setDirectory] = useState<AssigneeDirectory | null>(null);
  const [projects, setProjects] = useState<Project[] | null>(null);
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
    // A failed column read lands in `error`, shown under the still-disabled
    // form, so the dialog says why it cannot file rather than sitting idle.
    void loadBugChoices(getStatuses, getAssignees, getProjects).then((loaded) => {
      if (!alive) return;
      if (!loaded.ok) {
        setError(loaded.error);
        return;
      }
      setStatuses(loaded.statuses);
      setDirectory(loaded.directory);
      setProjects(loaded.projects);
      setFields(defaultBugFields(loaded.statuses));
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

  const canApprove = canApproveBug(projects, projectKey);
  const columnChoices = statuses ? boardColumns(statuses).filter((s) => !s.isTerminal) : [];

  return (
    <Modal open={open} onClose={onClose} title="File a bug?">
      <div className="hatch-form">
        {/* Two levels, one row, as in the New issue dialog. */}
        <div className="hatch-field-pair">
          <Field label="Priority" as="div">
            <PriorityControl
              issueKey=""
              priority={fields.priority}
              priorityOwn={fields.priority}
              priorityFrom={null}
              directory={directory}
              busy={saving}
              onChange={(priority) => setFields((f) => ({ ...f, priority }))}
            />
          </Field>

          <Field label="Pre-approval" as="div">
            <PreApprovalControl
              issueKey=""
              preApproval={fields.preApproval}
              preApprovalOwn={fields.preApproval}
              preApprovalFrom={null}
              directory={directory}
              canApprove={canApprove}
              busy={saving}
              onChange={(preApproval) => setFields((f) => ({ ...f, preApproval }))}
            />
          </Field>
        </div>

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
