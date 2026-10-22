import { useId, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, Field, Modal, ProjectMark, safeColor } from '@hatch/ui';
import {
  claimProject,
  deleteProjectLogo,
  deleteProjectMember,
  fileProjectReflection,
  getAssignees,
  getPeople,
  getStatuses,
  patchProject,
  putProjectLanding,
  putProjectLogo,
  putProjectMember,
  putProjectPlaybooksSource,
  putProjectReflection,
  putProjectRepositories,
  putProjectRunnerTags,
} from '../api/client';
import { message } from '../lib/errors';
import { boardColumns } from '../lib/columns';
import { fileReflection } from '../lib/projectReflect';
import {
  openProjectDraft,
  projectDraftDiff,
  projectDraftKeyObjection,
  projectLandingDiff,
  projectReflectionDiff,
  projectRepoSourcedDiff,
  projectRunnerTagsDiff,
  type ProjectDraft,
} from '../lib/projectDraft';
import { runnerTagsSentence } from '../lib/runnerTags';
import {
  hasOwner,
  openProjectMembersDraft,
  projectMembersDiff,
  removeObjection,
  reroled,
  roleObjection,
  withMember,
  withoutMember,
  type ProjectMembersDraft,
} from '../lib/projectMembers';
import { projectLogoUrl } from '../lib/projectLogo';
import { moved, repositoryObjection, withoutRemote, withRemote } from '../lib/repositories';
import { useLoaded } from '../lib/useLoaded';
import type { Project } from '../types';
import { PreApprovalControl } from './PreApprovalControl';
import { PriorityControl } from './PriorityControl';
import { ProjectIconPicker } from './ProjectIconPicker';
import { ProjectKeyField } from './ProjectKeyField';
import { ProjectLogoField, type LogoPending } from './ProjectLogoField';
import { RunnerTagsField } from './RunnerTagsField';

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
  const [membersDraft, setMembersDraft] = useState<ProjectMembersDraft | null>(null);
  const [personPick, setPersonPick] = useState('');
  const [colorDraft, setColorDraft] = useState(safeColor(null));
  const [confirmation, setConfirmation] = useState('');
  const [remote, setRemote] = useState('');
  const [baseBranch, setBaseBranch] = useState('');
  const [logoPending, setLogoPending] = useState<LogoPending>(null);
  const [saving, setSaving] = useState(false);
  const [claiming, setClaiming] = useState(false);
  const [reflecting, setReflecting] = useState(false);
  const [reflectError, setReflectError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const landingName = useId();
  const navigate = useNavigate();

  const { data: people } = useLoaded(getPeople);
  const { data: directory } = useLoaded(getAssignees);
  const { data: statuses } = useLoaded(getStatuses);

  // Reset whenever a different project is picked, so a half-typed
  // confirmation and a stale draft cannot be carried from one project to
  // another - the same idiom RekeyDialog uses today for its own fields.
  const [opened, setOpened] = useState<number | null>(null);
  if (project && project.id !== opened) {
    setOpened(project.id);
    setDraft(openProjectDraft(project));
    setMembersDraft(openProjectMembersDraft(project));
    setPersonPick('');
    setColorDraft(safeColor(project.color));
    setConfirmation('');
    setRemote('');
    setBaseBranch('');
    setLogoPending(null);
    setError(null);
    setReflectError(null);
  }

  if (!project || !draft || !membersDraft) return <Modal open={false} onClose={onClose} title="" />;

  const objection = projectDraftKeyObjection(draft, confirmation);
  const list = draft.repositories;
  const members = membersDraft.members;
  const availablePeople = (people ?? []).filter((person) => !members.some((m) => m.personId === person.id));

  function write(next: typeof list) {
    setDraft((d) => d && { ...d, repositories: next });
  }

  function writeMembers(next: typeof members) {
    setMembersDraft((d) => d && { ...d, members: next });
  }

  function addMember() {
    const person = availablePeople.find((p) => p.id === personPick);
    if (!person) return;
    writeMembers(withMember(members, person, 'approver'));
    setPersonPick('');
  }

  function add() {
    write(withRemote(list, { remote: remote.trim(), baseBranch: baseBranch.trim() || null }));
    setRemote('');
    setBaseBranch('');
  }

  async function save() {
    const diff = projectDraftDiff(draft!);
    const landingDiff = projectLandingDiff(draft!);
    const repoSourcedDiff = projectRepoSourcedDiff(draft!);
    const reflectionDiff = projectReflectionDiff(draft!);
    const runnerTagsDiff = projectRunnerTagsDiff(draft!);
    const membersDiff = projectMembersDiff(membersDraft!);
    if (
      diff.patch === null &&
      diff.repositories === null &&
      landingDiff === null &&
      repoSourcedDiff === null &&
      reflectionDiff === null &&
      runnerTagsDiff === null &&
      logoPending === null &&
      membersDiff.puts.length === 0 &&
      membersDiff.deletes.length === 0
    ) {
      onClose();
      return;
    }

    setSaving(true);
    try {
      if (diff.patch !== null) await patchProject(project!.id, diff.patch);
      if (diff.repositories !== null) await putProjectRepositories(project!.id, diff.repositories);
      if (landingDiff !== null) await putProjectLanding(project!.id, landingDiff);
      if (repoSourcedDiff !== null) await putProjectPlaybooksSource(project!.id, repoSourcedDiff);
      if (reflectionDiff !== null) await putProjectReflection(project!.id, reflectionDiff.cadenceDays);
      if (runnerTagsDiff !== null) await putProjectRunnerTags(project!.id, runnerTagsDiff);
      for (const put of membersDiff.puts) await putProjectMember(project!.id, put.personId, put.role);
      for (const personId of membersDiff.deletes) await deleteProjectMember(project!.id, personId);
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

  async function claim() {
    setClaiming(true);
    try {
      await claimProject(project!.id);
      setError(null);
      onSaved();
      onClose();
    } catch (err) {
      setError(message(err));
    } finally {
      setClaiming(false);
    }
  }

  async function reflectNow() {
    setReflecting(true);
    try {
      const result = await fileReflection(project!.id, fileProjectReflection);
      if (result.ok) {
        onClose();
        navigate(`/issues/${result.issue.key}`);
      } else {
        setReflectError(result.error);
      }
    } finally {
      setReflecting(false);
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

        <h2 className="hatch-section-title">Landing</h2>
        {project.canAdminister ? (
          <>
            <fieldset className="hatch-landing-choice">
              <label>
                <input
                  type="radio"
                  name={landingName}
                  checked={draft.landing === 'pull-request'}
                  onChange={() => setDraft((d) => d && { ...d, landing: 'pull-request' })}
                />
                By pull request
              </label>
              <label>
                <input
                  type="radio"
                  name={landingName}
                  checked={draft.landing === 'trunk'}
                  onChange={() => setDraft((d) => d && { ...d, landing: 'trunk' })}
                />
                On the trunk
              </label>
            </fieldset>
            <p className="hatch-section-hint text-muted">
              Trunk landing pushes the session's commits straight to the trunk branch - no branch, no pull request -
              and the loop carries the ticket to Done once the trunk is green.
            </p>
            <label>
              <input
                type="checkbox"
                checked={draft.checksRequired}
                onChange={(e) => setDraft((d) => d && { ...d, checksRequired: e.target.checked })}
              />
              Checks are required
            </label>
            <label>
              <input
                type="checkbox"
                checked={draft.onePullRequest}
                onChange={(e) => setDraft((d) => d && { ...d, onePullRequest: e.target.checked })}
              />
              A story's tasks share one branch and one pull request
            </label>
            <p className="hatch-section-hint text-muted">
              The default for every story in this project; a story can override it either way.
              Off, each task gets its own branch and pull request, merged one after another.
            </p>
            <label>
              <input
                type="checkbox"
                checked={draft.autoHealBugEnabled}
                onChange={(e) => setDraft((d) => d && { ...d, autoHealBugEnabled: e.target.checked })}
              />
              File a bug when the trunk fails
            </label>
            <p className="hatch-section-hint text-muted">
              Files an emergency, pre-approved bug the moment a repository's trunk build fails, so the loop can start
              healing it with no session.
            </p>
            <fieldset className="hatch-landing-choice" disabled={!draft.autoHealBugEnabled}>
              <PriorityControl
                issueKey=""
                priority={draft.autoHealBugPriority}
                priorityOwn={draft.autoHealBugPriority}
                priorityFrom={null}
                directory={directory}
                onChange={(priority) => setDraft((d) => d && { ...d, autoHealBugPriority: priority })}
              />
              <PreApprovalControl
                issueKey=""
                preApproval={draft.autoHealBugPreApproval}
                preApprovalOwn={draft.autoHealBugPreApproval}
                preApprovalFrom={null}
                directory={directory}
                canApprove={project.canApprove}
                onChange={(level) => setDraft((d) => d && { ...d, autoHealBugPreApproval: level })}
              />
              <Field label="Column">
                <select
                  value={draft.autoHealBugStatusId ?? ''}
                  onChange={(e) =>
                    setDraft((d) => d && { ...d, autoHealBugStatusId: e.target.value === '' ? null : Number(e.target.value) })
                  }
                >
                  <option value="">Automatic (tracks the column that feeds implementation)</option>
                  {(statuses ? boardColumns(statuses).filter((s) => !s.isTerminal) : []).map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </select>
              </Field>
            </fieldset>
            <div className="hatch-inline-form">
              <label>
                Reflects every{' '}
                <input
                  inputMode="numeric"
                  value={draft.reflectionCadenceDays}
                  onChange={(e) => setDraft((d) => d && { ...d, reflectionCadenceDays: e.target.value })}
                />{' '}
                day(s)
              </label>
              <Button loading={reflecting} onClick={() => void reflectNow()}>
                Reflect now
              </Button>
            </div>
            {reflectError && <p className="text-danger">{reflectError}</p>}
            {project.reflectionCadenceDays !== null && (
              <p className="hatch-section-hint text-muted">
                Next due {project.reflectionNextDueAt ? new Date(project.reflectionNextDueAt).toLocaleDateString() : ''}. Blank
                turns it off.
              </p>
            )}
            <label>
              <input
                type="checkbox"
                checked={draft.repoSourced}
                disabled={list.length === 0}
                onChange={(e) => setDraft((d) => d && { ...d, repoSourced: e.target.checked })}
              />
              Read playbooks from <code>.hatch/playbooks.yaml</code> on the trunk
            </label>
            {list.length === 0 && (
              <p className="hatch-section-hint text-muted">
                {project.key} binds no repository - bind one on the Projects page first.
              </p>
            )}
            {(project.repoSourcedBy || project.repoSourcedAt) && (
              <p className="hatch-section-hint text-muted">
                set by {project.repoSourcedBy} on {project.repoSourcedAt ? new Date(project.repoSourcedAt).toLocaleDateString() : ''}
              </p>
            )}
            <RunnerTagsField
              label="Runner tags"
              hint="One tag per line. Only a runner advertising every one of these is handed this project's implementation work. Blank means any runner."
              value={draft.runnerTags}
              onChange={(next) => setDraft((d) => d && { ...d, runnerTags: next })}
            />
          </>
        ) : (
          <>
            <p>{`${draft.landing === 'trunk' ? 'On the trunk' : 'By pull request'}, checks ${draft.checksRequired ? 'required' : 'not required'}, ${draft.onePullRequest ? 'one pull request per story' : 'one pull request per task'}, auto-heal ${
              draft.autoHealBugEnabled
                ? `files a ${draft.autoHealBugPriority} bug at ${draft.autoHealBugPreApproval} pre-approval in ${statuses?.find((s) => s.id === draft.autoHealBugStatusId)?.name ?? 'the automatic column'}`
                : 'off'
            }, playbooks ${draft.repoSourced ? 'sourced from the repository' : 'written on the board'}, ${
              project.reflectionCadenceDays === null
                ? 'never reflects'
                : `reflects every ${project.reflectionCadenceDays} day(s), next due ${project.reflectionNextDueAt ? new Date(project.reflectionNextDueAt).toLocaleDateString() : 'unknown'}`
            }.`}</p>
            <p>{runnerTagsSentence(project.runnerTags ?? [])}</p>
          </>
        )}

        <h2 className="hatch-section-title">Members</h2>
        {!hasOwner(project.members) && (
          <Button loading={claiming} onClick={() => void claim()}>
            Claim
          </Button>
        )}
        {members.length === 0 && <p className="text-muted">No members.</p>}
        {members.map((member) => {
          const otherRole = member.role === 'owner' ? 'approver' : 'owner';
          const roleObj = roleObjection(members, member.personId, otherRole);
          const removeObj = removeObjection(members, member.personId);
          return (
            <div className="hatch-inline-form" key={member.personId}>
              <span>{member.name}</span>
              {project.canAdminister ? (
                <>
                  <select
                    aria-label={`Role of ${member.name}`}
                    value={member.role}
                    disabled={roleObj !== null}
                    title={roleObj ?? undefined}
                    onChange={(e) => writeMembers(reroled(members, member.personId, e.target.value))}
                  >
                    <option value="owner">owner</option>
                    <option value="approver">approver</option>
                  </select>
                  <Button
                    variant="danger"
                    disabled={removeObj !== null}
                    title={removeObj ?? undefined}
                    onClick={() => writeMembers(withoutMember(members, member.personId))}
                  >
                    Remove
                  </Button>
                </>
              ) : (
                <span>{member.role}</span>
              )}
            </div>
          );
        })}
        {project.canAdminister && (
          <div className="hatch-inline-form">
            <select aria-label="Add member" value={personPick} onChange={(e) => setPersonPick(e.target.value)}>
              <option value="">Pick a person</option>
              {availablePeople.map((person) => (
                <option key={person.id} value={person.id}>
                  {person.name}
                </option>
              ))}
            </select>
            <Button disabled={!personPick} onClick={addMember}>
              Add
            </Button>
          </div>
        )}
      </div>
    </Modal>
  );
}
