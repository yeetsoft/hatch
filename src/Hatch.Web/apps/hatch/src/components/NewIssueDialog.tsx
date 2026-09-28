import { useState } from 'react';
import { Button, Field, Modal } from '@hatch/ui';
import { createIssue } from '../api/client';
import { message } from '../lib/errors';
import { parentCandidates, parentHint } from '../lib/parents';
import { useIssueConfirmations } from '../lib/useIssueConfirmations';
import { IssuePicker } from './IssuePicker';
import { MarkdownEditor } from './MarkdownEditor';
import { MomentField } from './MomentField';
import { ISSUE_TYPES } from '../types';
import type { IssueCard, IssueType, Project } from '../types';

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
  onClose,
  onCreated,
}: {
  open: boolean;
  projects: Project[];
  /** The board's cards. The dialog filters them down to what the chosen
      project and type may hang under - see lib/parents.ts. */
  candidates: IssueCard[];
  /** The board's filtered project, if one is set - see BoardFilters. Wins over
      the first-project fallback below, until somebody picks another. */
  defaultProjectKey: string;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [projectId, setProjectId] = useState<number | null>(null);
  const [type, setType] = useState<IssueType>('task');
  const [parentKey, setParentKey] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [readyAt, setReadyAt] = useState('');
  const [dueAt, setDueAt] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const { confirm } = useIssueConfirmations();

  // The filtered project, then the first project, until somebody picks
  // another. One project is the ordinary case and choosing it for them is one
  // fewer thing to do - and filing from a filtered board almost always means
  // filing into it.
  const filtered = projects.find((p) => p.key === defaultProjectKey);
  const chosen = projectId ?? filtered?.id ?? projects[0]?.id ?? null;

  // The pick is kept as chosen and clamped on the way out, as ChildComposer
  // does its type: a project or type the pick is no candidate for reads
  // `— none —` and files none, and switching back shows it again. No effect.
  const projectKey = projects.find((p) => p.id === chosen)?.key;
  const parents = projectKey ? parentCandidates(candidates, projectKey, type) : [];
  const parent = parentKey && parents.some((c) => c.key === parentKey) ? parentKey : null;

  async function submit() {
    if (chosen === null) {
      setError('there are no projects yet - make one on the Projects page');
      return;
    }

    setSaving(true);
    try {
      const created = await createIssue({ projectId: chosen, type, parentKey: parent, title, description, readyAt, dueAt });
      // Only here, on the way out of the success path: a filing the server
      // refused goes to the catch below and raises nothing, and the dialog goes
      // on showing the refusal as it always has.
      confirm(created);
      setParentKey(null);
      setTitle('');
      setDescription('');
      setReadyAt('');
      setDueAt('');
      setError(null);
      onCreated();
      onClose();
    } catch (err) {
      setError(message(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal open={open} onClose={onClose} title="New issue">
      <div className="hatch-form">
        <Field label="Project">
          <select value={chosen ?? ''} onChange={(e) => setProjectId(Number(e.target.value))}>
            {projects.map((p) => (
              <option key={p.id} value={p.id}>
                {p.key} — {p.name}
              </option>
            ))}
          </select>
        </Field>

        <Field label="Type">
          <select value={type} onChange={(e) => setType(e.target.value as IssueType)}>
            {ISSUE_TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </Field>

        {/* `as="div"` for the reason on the issue page's Parent field: the
            popup would otherwise join the control's accessible name. */}
        <Field label="Parent" as="div" hint={parentHint(type)}>
          <IssuePicker
            label="Parent"
            value={parent}
            candidates={parents}
            emptyMessage={`Nothing in ${projectKey} can be a parent of a ${type} yet.`}
            onChange={async (key) => setParentKey(key || null)}
          />
        </Field>

        <Field label="Title">
          <input value={title} onChange={(e) => setTitle(e.target.value)} autoFocus />
        </Field>

        {/* `as="div"` because the editor is not something a <label> can choose:
            MarkdownEditor focuses it when the label is clicked, and names it
            with `ariaLabel`. */}
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
        <MomentField label="Ready" hint="Folded off the board until this day." value={readyAt} onChange={setReadyAt} />
        <MomentField label="Due" value={dueAt} onChange={setDueAt} />

        {error && <p className="text-danger">{error}</p>}

        <div className="hatch-form-actions">
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={saving} onClick={() => void submit()}>
            File it
          </Button>
        </div>
      </div>
    </Modal>
  );
}
