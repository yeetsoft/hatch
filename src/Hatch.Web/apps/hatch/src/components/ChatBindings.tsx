import { useState } from 'react';
import { Text } from '@hatch/ui';
import { setChatProjects } from '../api/client';
import { message } from '../lib/errors';
import type { ChatWorkspace, Project } from '../types';

/**
 * The projects a workspace is bound to: a checkbox for every project the reader
 * can see, ticked where the workspace carries it.
 *
 * A toggle sends the whole new set, because the server replaces the set rather
 * than adding to it. The list is disabled while the answer is awaited and a
 * refusal's sentence is shown under it, so it appears under the row it belongs
 * to. `onChange` is handed the set the server answered with.
 */
export function ChatBindings({
  workspace,
  projects,
  busy,
  onChange,
}: {
  workspace: ChatWorkspace;
  projects: Project[];
  busy: boolean;
  onChange: (workspace: ChatWorkspace, projectIds: number[]) => void;
}) {
  const [waiting, setWaiting] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  async function toggle(project: Project, bound: boolean) {
    const next = bound
      ? [...workspace.projectIds, project.id]
      : workspace.projectIds.filter((id) => id !== project.id);
    setWaiting(true);
    try {
      onChange(workspace, await setChatProjects(workspace.id, next));
      setFailure(null);
    } catch (err) {
      setFailure(message(err));
    } finally {
      setWaiting(false);
    }
  }

  return (
    <BindingList
      workspace={workspace}
      projects={projects}
      disabled={busy || waiting}
      failure={failure}
      onToggle={(project, bound) => void toggle(project, bound)}
    />
  );
}

/** The list alone, for a render that has no network. */
export function BindingList({
  workspace,
  projects,
  disabled,
  failure,
  onToggle,
}: {
  workspace: ChatWorkspace;
  projects: Project[];
  disabled: boolean;
  failure: string | null;
  onToggle: (project: Project, bound: boolean) => void;
}) {
  if (projects.length === 0) return <Text tone="muted">No project to bind yet.</Text>;

  return (
    <div className="hatch-chat-projects">
      <div role="group" aria-label={`Projects bound to ${workspace.name}`} className="hatch-chat-projects__list">
        {projects.map((project) => (
          <label key={project.id} className="hatch-chat-projects__item">
            <input
              type="checkbox"
              checked={workspace.projectIds.includes(project.id)}
              disabled={disabled}
              onChange={(e) => onToggle(project, e.target.checked)}
            />
            <span className="hatch-chat-projects__key">{project.key}</span> {project.name}
          </label>
        ))}
      </div>
      {failure && <p className="text-danger hatch-chat-projects__failure">{failure}</p>}
    </div>
  );
}
