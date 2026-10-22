import type { ReactNode } from 'react';
import { useState } from 'react';
import { RunnerTagsField } from './RunnerTagsField';
import { runnerTagsDraft, runnerTagsRequest, sameTags } from '../lib/runnerTags';
import type { Status } from '../types';

/**
 * A column's own runner tags, editable right where they are read - blur-commit,
 * re-sync when they change underneath, the way IssueRunnerTagsOwnField is. The
 * column header names the field, so the label here is visually hidden rather
 * than drawn per row.
 */
export function StatusRunnerTagsField({
  status,
  busy,
  refusal,
  onSave,
}: {
  status: Status;
  busy: boolean;
  refusal?: ReactNode;
  onSave: (tags: string[]) => void;
}) {
  const [draft, setDraft] = useState(runnerTagsDraft(status.runnerTags));
  const [known, setKnown] = useState(status.runnerTags);

  // Re-syncs when the row changes underneath - see IssueRunnerTagsOwnField.
  if (!sameTags(status.runnerTags, known)) {
    setKnown(status.runnerTags);
    setDraft(runnerTagsDraft(status.runnerTags));
  }

  return (
    <div
      onBlur={() => {
        const next = runnerTagsRequest(draft);
        if (!sameTags(next, status.runnerTags)) onSave(next);
      }}
    >
      <RunnerTagsField
        label={<span className="hatch-visually-hidden">{status.name} runner tags</span>}
        value={draft}
        busy={busy}
        error={refusal}
        onChange={setDraft}
      />
    </div>
  );
}
