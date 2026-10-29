import { useState } from 'react';
import { Button, EmptyState, Field, Modal } from '@hatch/ui';
import { createIssue } from '../api/client';
import { resolveDefaultProject, readRememberedProject, writeRememberedProject } from '../lib/defaultProject';
import { message } from '../lib/errors';
import { onePrToFile } from '../lib/onePr';
import { parentCandidates, parentEmptyMessage, parentHint } from '../lib/parents';
import { useIssueConfirmations } from '../lib/useIssueConfirmations';
import { IssuePicker } from './IssuePicker';
import { MarkdownEditor } from './MarkdownEditor';
import { MomentField } from './MomentField';
import { OnePrSteps, type OnePrSetting } from './OnePrControl';
import { PreApprovalControl, type PreApproval } from './PreApprovalControl';
import { PriorityControl, type Priority } from './PriorityControl';
import { ProjectChooser } from './ProjectChooser';
import { ISSUE_TYPES } from '../types';
import type { AssigneeDirectory, Issue, IssueCard, IssueType, Project } from '../types';

/** What a filing sends for a level the dialog carries with a default: the
    level itself when it was touched, nothing when it was left at the
    default - the server refuses the field's raw presence from a key, even
    the default, so an untouched dialog must send exactly what it always
    has. */
export function omitDefault<T>(level: T, defaultLevel: T): T | undefined {
  return level === defaultLevel ? undefined : level;
}

/**
 * Filing an issue: a project, a type, the parent it hangs under, a title, and somewhere to
 * start writing.
 * No status and no position - the server puts a new issue in the leftmost
 * column at the bottom, so there is nothing here to get wrong.
 */
export function NewIssueDialog({
  open,
  projects,
  candidates,
  defaultProjectKey,
  directory,
  onClose,
  onCreated,
}: {
  open: boolean;
  projects: Project[];
  /** The board's cards. The dialog filters them down to what the chosen
      project and type may hang under - see lib/parents.ts. */
  candidates: IssueCard[];
  /** The board's current project filter, `''` when unfiltered - tried before
      the remembered project. */
  defaultProjectKey: string;
  /** Everybody who could own an issue, and who the caller is - handed
      straight to `PriorityControl` and `PreApprovalControl`, the same as
      the issue page and the peek already do. */
  directory: AssigneeDirectory | null;
  onClose: () => void;
  onCreated: (created: Issue) => void;
}) {
  const [projectId, setProjectId] = useState<number | null>(null);
  const [type, setType] = useState<IssueType>('task');
  const [onePr, setOnePr] = useState<OnePrSetting>('inherit');
  const [priority, setPriority] = useState<Priority>('normal');
  const [preApproval, setPreApproval] = useState<PreApproval>('none');
  const [parentKey, setParentKey] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [readyAt, setReadyAt] = useState('');
  const [dueAt, setDueAt] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const { confirm } = useIssueConfirmations();

  const chosen = projectId ?? resolveDefaultProject(projects, defaultProjectKey, readRememberedProject());

  // The pick is kept as chosen and clamped on the way out, as ChildComposer
  // does its type: a project or type the pick is no candidate for reads
  // `— none —` and files none, and switching back shows it again. No effect.
  const projectKey = projects.find((p) => p.id === chosen)?.key;
  const parents = projectKey ? parentCandidates(candidates, projectKey, type) : [];
  const parent = parentKey && parents.some((c) => c.key === parentKey) ? parentKey : null;
  const keyPreview = projectKey ? `${projectKey}-…` : null;
  const canApprove = projects.find((p) => p.id === chosen)?.canApprove ?? false;
  // Clamped on the way out like `parent` is: a stored preapproved that the
  // chosen project's approver may not set would otherwise be refused by the
  // server once File it is pressed, rather than kept locked here as it was
  // while the band was open.
  const clampedPreApproval: PreApproval = !canApprove && preApproval === 'preapproved' ? 'none' : preApproval;

  function handleClose() {
    setProjectId(null);
    onClose();
  }

  async function submit() {
    if (chosen === null) {
      setError('there are no projects yet - make one on the Projects page');
      return;
    }

    setSaving(true);
    try {
      const created = await createIssue({
        projectId: chosen,
        type,
        parentKey: parent,
        title,
        description,
        readyAt,
        dueAt,
        onePr: onePrToFile(type, onePr),
        priority: omitDefault(priority, 'normal'),
        preApproval: omitDefault(clampedPreApproval, 'none'),
      });
      // Only here, on the way out of the success path: a filing the server
      // refused goes to the catch below and raises nothing, and the dialog goes
      // on showing the refusal as it always has.
      confirm(created);
      if (projectKey) writeRememberedProject(projectKey);
      setProjectId(null);
      setParentKey(null);
      setOnePr('inherit');
      setPriority('normal');
      setPreApproval('none');
      setTitle('');
      setDescription('');
      setReadyAt('');
      setDueAt('');
      setError(null);
      onCreated(created);
      onClose();
    } catch (err) {
      setError(message(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal open={open} onClose={handleClose} title="New issue">
      <div className="hatch-form">
        {projects.length === 0 ? (
          <EmptyState message="No projects yet. Make one on the Projects page first." />
        ) : (
          <>
            <ProjectChooser legend="Project" projects={projects} value={chosen} onChange={setProjectId} />

            <Field label="Type">
              <select value={type} onChange={(e) => {
                  setType(e.target.value as IssueType);
                  setOnePr('inherit');
                }}>
                {ISSUE_TYPES.map((t) => (
                  <option key={t} value={t}>
                    {t}
                  </option>
                ))}
              </select>
            </Field>

            {(type === 'story' || type === 'bug') && (
              <Field
                label="One pull request"
                as="div"
                hint={`Whether this ${type}'s tasks share its branch and open one pull request, merged once, instead of one each.`}
              >
                <OnePrSteps
                  value={onePr}
                  label={`Set the new ${type}'s one pull request setting`}
                  busy={saving}
                  onChange={setOnePr}
                />
              </Field>
            )}

            {/* `as="div"` for the reason on the issue page's Parent field: the
                popup would otherwise join the control's accessible name. */}
            <Field label="Parent" as="div" hint={parentHint(type)}>
              <IssuePicker
                label="Parent"
                value={parent}
                candidates={parents}
                emptyMessage={parentEmptyMessage(projectKey, type)}
                onChange={async (key) => setParentKey(key || null)}
              />
            </Field>

            <Field label="Title">
              <input value={title} onChange={(e) => setTitle(e.target.value)} autoFocus />
            </Field>

            <Field label="Description" as="div" hint="Markdown, rendered on the issue page.">
              <MarkdownEditor
                value={description}
                onChange={setDescription}
                rows={6}
                className="hatch-grows"
                ariaLabel="Description"
              />
            </Field>

            {/* Both optional, and both usually left empty. They are here rather than
                behind a second visit because the moment worth setting a ready date
                is the moment the thing is thought of - buy a certificate today,
                file the renewal for next August before closing the tab. */}
            <MomentField
              label="Ready"
              hint="Folded off the board until this day."
              value={readyAt}
              onChange={setReadyAt}
            />
            <MomentField label="Due" value={dueAt} onChange={setDueAt} />

            <Field label="Priority" as="div">
              <PriorityControl
                issueKey=""
                priority={priority}
                priorityOwn={priority}
                priorityFrom={null}
                directory={directory}
                busy={saving}
                onChange={setPriority}
              />
            </Field>

            <Field label="Pre-approval" as="div">
              <PreApprovalControl
                issueKey=""
                preApproval={clampedPreApproval}
                preApprovalOwn={clampedPreApproval}
                preApprovalFrom={null}
                directory={directory}
                canApprove={canApprove}
                busy={saving}
                onChange={setPreApproval}
              />
            </Field>

            {error && <p className="text-danger">{error}</p>}
          </>
        )}

        <div className="hatch-form-actions hatch-newissue-actions">
          {keyPreview && <span className="hatch-key-preview">{keyPreview}</span>}
          <div className="hatch-newissue-actions__buttons">
            <Button onClick={handleClose}>Cancel</Button>
            {projects.length > 0 && (
              <Button variant="primary" loading={saving} disabled={chosen === null} onClick={() => void submit()}>
                File it
              </Button>
            )}
          </div>
        </div>
      </div>
    </Modal>
  );
}
