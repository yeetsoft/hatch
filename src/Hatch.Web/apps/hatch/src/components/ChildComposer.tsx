import { useRef, useState } from 'react';
import { Button, Field, ProjectMark } from '@hatch/ui';
import { createIssue } from '../api/client';
import { childTypes, clampChild } from '../lib/childTypes';
import { message } from '../lib/errors';
import { useIssueConfirmations } from '../lib/useIssueConfirmations';
import type { IssueType, Project } from '../types';
import { TypeControl } from './TypeControl';

/**
 * Filing the next child where the children are listed.
 *
 * A type and a title and nothing else. A description and dates belong to the
 * child and are set on its own page; asking for them here would make filing
 * three stories under an epic three visits to a dialog, which is the thing
 * this card exists to stop.
 *
 * What it may offer is not decided here - childTypes reads the same table the
 * server refuses by, so the picker cannot come to disagree with the refusal.
 * It is read from the parent's type in this component, and a task, which takes
 * nothing, draws nothing.
 */
export function ChildComposer({
  projectId,
  project,
  parentKey,
  parentType,
  onFiled,
}: {
  projectId: number;
  /** The project this child is filed into, for the mark beside File it. Null
      draws nothing there - the composer still files either way. */
  project: Project | null;
  parentKey: string;
  parentType: IssueType;
  onFiled: () => void;
}) {
  const [picked, setPicked] = useState<IssueType | null>(null);
  const [title, setTitle] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const box = useRef<HTMLInputElement>(null);
  const { confirm } = useIssueConfirmations();

  const types = childTypes(parentType);
  if (types.length === 0) return null;

  // Null until somebody chooses, so the first legal type is the default without
  // an effect to set it - and clamped on the way out, because the issue's own
  // type is editable on this page: a picker still holding "story" after the
  // epic above it became a story would offer what the server refuses.
  const chosen = clampChild(picked, types);

  async function submit() {
    setSaving(true);
    try {
      const created = await createIssue({ projectId, type: chosen, title: title.trim(), parentKey });
      // The same corner the board's dialog raises: a child filed here is an
      // issue filed, and the key it got is worth as much from this box as from
      // that one. On the success path only - the catch below is untouched.
      confirm(created);
      setTitle('');
      setError(null);
      box.current?.focus();
      onFiled();
    } catch (err) {
      // The title is deliberately left where it was typed - the refusal is
      // usually about the type, and retyping the title to try again is a tax.
      setError(message(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="hatch-inline-form hatch-child-composer" role="group" aria-label="File a child issue">
      {/* `as="div"`, as in the Details card: the pill is a button and its open
          band is not a labelled control, so there is nothing for a label to
          point at. The pill names the new child, not the parent it hangs under. */}
      <Field label="Type" as="div">
        <TypeControl
          issueKey={parentKey}
          type={chosen}
          types={types}
          busy={saving}
          label="Type of the new child"
          onChange={setPicked}
        />
      </Field>

      <Field label="Title" className="hatch-child-composer-title">
        <input
          ref={box}
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          // Enter files it. A title is one line, so there is no newline for
          // Enter to be taking away - unlike the comment boxes on this page,
          // which need meta+Enter for exactly that reason.
          onKeyDown={(e) => {
            if (e.key === 'Enter' && title.trim() && !saving) void submit();
          }}
        />
      </Field>

      <Button variant="primary" loading={saving} disabled={!title.trim()} onClick={() => void submit()}>
        File it
      </Button>

      {project && (
        <span className="hatch-key-preview">
          <ProjectMark letters={project.key} color={project.color} icon={project.icon} size="sm" title={project.name} />
          {project.key}-…
        </span>
      )}

      {/* Said here, under the box that caused it, in the sentence the server
          wrote - see fetchJson in api/client.ts. */}
      {error && <p className="text-danger">{error}</p>}
    </div>
  );
}
