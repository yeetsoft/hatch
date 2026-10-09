import { useState } from 'react';
import { Button, Field, Modal, ProjectMark, safeColor } from '@hatch/ui';
import { deleteProjectLogo, patchProject, putProjectLogo, putProjectRepositories } from '../api/client';
import { message } from '../lib/errors';
import { openProjectDraft, projectDraftDiff, projectDraftKeyObjection, type ProjectDraft } from '../lib/projectDraft';
import { projectLogoUrl } from '../lib/projectLogo';
import { moved, repositoryObjection, withoutRemote, withRemote } from '../lib/repositories';
import type { Project } from '../types';
import { ProjectIconPicker } from './ProjectIconPicker';
import { ProjectKeyField } from './ProjectKeyField';
import { ProjectLogoField, type LogoPending } from './ProjectLogoField';

/**
 * The edit modal: name, key (with its speed bump) and the repositories list,
 * all held in one draft and sent as at most two requests on Save - see
 * lib/projectDraft.ts for the diff this computes against.
 *
 * Stays open on error, the CloseSubtreeDialog/WipOverrideDialog convention
 * rather than RekeyDialog's today - the draft is worth keeping around for a
 * retry, and the parent's own error state is never involved.
 */
export function ProjectEditDialog({
  project,
  onClose,
  onSaved,
}: {
  project: Project | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [draft, setDraft] = useState<ProjectDraft | null>(null);
  const [colorDraft, setColorDraft] = useState(safeColor(null));
  const [confirmation, setConfirmation] = useState('');
  const [remote, setRemote] = useState('');
  const [baseBranch, setBaseBranch] = useState('');
  const [logoPending, setLogoPending] = useState<LogoPending>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Reset whenever a different project is picked, so a half-typed
  // confirmation and a stale draft cannot be carried from one project to
  // another - the same idiom RekeyDialog uses today for its own fields.
  const [opened, setOpened] = useState<number | null>(null);
  if (project && project.id !== opened) {
    setOpened(project.id);
    setDraft(openProjectDraft(project));
    setColorDraft(safeColor(project.color));
    setConfirmation('');
    setRemote('');
    setBaseBranch('');
    setLogoPending(null);
    setError(null);
  }

  if (!project || !draft) return <Modal open={false} onClose={onClose} title="" />;

  const objection = projectDraftKeyObjection(draft, confirmation);
  const list = draft.repositories;

  function write(next: typeof list) {
    setDraft((d) => d && { ...d, repositories: next });
  }

  function add() {
    write(withRemote(list, { remote: remote.trim(), baseBranch: baseBranch.trim() || null }));
    setRemote('');
    setBaseBranch('');
  }

  async function save() {
    const diff = projectDraftDiff(draft!);
    if (diff.patch === null && diff.repositories === null && logoPending === null) {
      onClose();
      return;
    }

    setSaving(true);
    try {
      if (diff.patch !== null) await patchProject(project!.id, diff.patch);
      if (diff.repositories !== null) await putProjectRepositories(project!.id, diff.repositories);
      if (logoPending === 'removed') await deleteProjectLogo(project!.id);
      else if (logoPending !== null) await putProjectLogo(project!.id, logoPending.blob);
      setError(null);
      onSaved();
      onClose();
    } catch (err) {
      setError(message(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={`Edit ${project.key}`}
      footer={
        <div className="hatch-form-actions">
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={saving} disabled={objection !== null} onClick={() => void save()}>
            Save
          </Button>
        </div>
      }
    >
      <div className="hatch-form">
        <ProjectMark
          size="lg"
          letters={draft.key}
          color={draft.color}
          icon={draft.icon}
          logoUrl={logoPending === 'removed' ? null : logoPending ? logoPending.previewUrl : projectLogoUrl(project)}
          title={draft.name || draft.key}
        />

        {error && <p className="text-danger">{error}</p>}

        <div className="hatch-field-grid">
          <Field label="Name">
            <input value={draft.name} onChange={(e) => setDraft((d) => d && { ...d, name: e.target.value })} />
          </Field>
          <ProjectKeyField
            knownKey={draft.known.key}
            value={draft.key}
            onChange={(next) => setDraft((d) => d && { ...d, key: next })}
            confirmation={confirmation}
            onConfirmationChange={setConfirmation}
          />
          <Field label="Colour">
            <div className="hatch-color-cell">
              <input
                type="color"
                value={colorDraft}
                aria-label="Colour"
                onChange={(e) => setColorDraft(e.target.value)}
                onBlur={() => {
                  if (colorDraft !== safeColor(draft.color)) setDraft((d) => d && { ...d, color: colorDraft });
                }}
              />
              <Button
                onClick={() => {
                  setDraft((d) => d && { ...d, color: null });
                  setColorDraft(safeColor(null));
                }}
              >
                Clear
              </Button>
            </div>
          </Field>
        </div>

        <h2 className="hatch-section-title">Icon</h2>
        <ProjectIconPicker
          value={draft.icon}
          color={draft.color}
          onChange={(icon) => setDraft((d) => d && { ...d, icon })}
        />

        <ProjectLogoField key={project.id} logoUpdatedAt={project.logoUpdatedAt} onChange={setLogoPending} />

        <h2 className="hatch-section-title">Repositories</h2>
        {list.length === 0 && <p className="text-muted">No repositories.</p>}
        {list.map((repo, index) => (
          <div className="hatch-inline-form" key={repo.remote}>
            <code>{repo.remote}</code>
            {index === 0 && <span className="text-muted">primary</span>}
            <input
              placeholder="Base branch"
              value={repo.baseBranch ?? ''}
              onChange={(e) =>
                write(list.map((entry, i) => (i === index ? { ...entry, baseBranch: e.target.value || null } : entry)))
              }
            />
            <div className="hatch-reorder">
              <Button
                disabled={index === 0}
                aria-label={`Move ${repo.remote} up`}
                onClick={() => write(moved(list, index, index - 1))}
              >
                ↑
              </Button>
              <Button
                disabled={index === list.length - 1}
                aria-label={`Move ${repo.remote} down`}
                onClick={() => write(moved(list, index, index + 1))}
              >
                ↓
              </Button>
            </div>
            <Button variant="danger" onClick={() => write(withoutRemote(list, index))}>
              Remove
            </Button>
          </div>
        ))}
        <div className="hatch-inline-form">
          <input placeholder="Remote" value={remote} onChange={(e) => setRemote(e.target.value)} />
          <input placeholder="Base branch" value={baseBranch} onChange={(e) => setBaseBranch(e.target.value)} />
          <Button disabled={repositoryObjection(remote) !== null} onClick={add}>
            Add
          </Button>
        </div>
      </div>
    </Modal>
  );
}
