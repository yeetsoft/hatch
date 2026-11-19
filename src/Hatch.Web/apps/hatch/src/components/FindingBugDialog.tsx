import { useEffect, useState } from 'react';
import { Button, Field, Modal } from '@hatch/ui';
import { fileGooseFindingBug, getAssignees, getProjects, getStatuses } from '../api/client';
import { boardColumns } from '../lib/columns';
import { useIssueConfirmations } from '../lib/useIssueConfirmations';
import { canApproveBug, defaultBugFields, loadBugChoices, type BugFields } from '../lib/trunkBuildBug';
import { fileFindingBug } from '../lib/findingBug';
import { PreApprovalControl } from './PreApprovalControl';
import { PriorityControl } from './PriorityControl';
import type { AssigneeDirectory, Issue, Project, Status } from '../types';

/**
 * *The confirm-and-file dialog a goose finding's bug asks for* - the same three
 * fields as the trunk build's (pre-approval, priority, which column), reviewed
 * before filing. A finding that names a project files under it; one that names
 * none adds a Project choice, nothing chosen at first, and File it stays
 * disabled until one is. Pre-approved is pressable for an owner or approver of
 * the finding's project, or of the chosen one.
 *
 * Self-fetching for the reason TrunkBuildBugDialog is.
 */
export function FindingBugDialog({
  open,
  findingId,
  projectKey,
  onClose,
  onFiled,
}: {
  open: boolean;
  findingId: number;
  /** The project the finding names, or null when it names none. */
  projectKey: string | null;
  onClose: () => void;
  /** Called once filing succeeds, after confirm() - FindingRow's reload(). */
  onFiled: (created: Issue) => void;
}) {
  const [statuses, setStatuses] = useState<Status[] | null>(null);
  const [directory, setDirectory] = useState<AssigneeDirectory | null>(null);
  const [projects, setProjects] = useState<Project[] | null>(null);
  const [fields, setFields] = useState<BugFields>(() => defaultBugFields(null));
  const [chosen, setChosen] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const { confirm } = useIssueConfirmations();

  // Re-fetches every time the dialog opens rather than once on mount, so a
  // dialog opened for a different finding - or reopened after a refusal -
  // always starts from a fresh read rather than a stale one.
  useEffect(() => {
    if (!open) return;
    setError(null);
    setSaving(false);
    setFields(defaultBugFields(null));
    setChosen(null);
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
    const result = await fileFindingBug(findingId, fields, projectKey ? null : chosen, fileGooseFindingBug);
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

  const canApprove = canApproveBug(projects, projectKey ?? chosen);
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

        {projectKey == null && (
          <Field label="Project">
            <select value={chosen ?? ''} disabled={!projects || saving} onChange={(e) => setChosen(e.target.value || null)}>
              <option value="">Choose a project</option>
              {(projects ?? []).map((p) => (
                <option key={p.key} value={p.key}>
                  {p.key} - {p.name}
                </option>
              ))}
            </select>
          </Field>
        )}

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
            disabled={!statuses || fields.statusId == null || (projectKey == null && chosen == null)}
            onClick={() => void confirmFile()}
          >
            File it
          </Button>
        </div>
      </div>
    </Modal>
  );
}
