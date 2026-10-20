import { useCoarsePointer } from '../lib/viewport';
import { releaseNoteShown } from '../lib/releaseNote';
import { DescriptionEditor } from './DescriptionEditor';
import { IssueSection } from './IssueSection';

/** The release note: markdown a top-level issue carries into the changelog.
    Hidden for a child issue with none - the changelog only shows top-level
    issues, so there is nothing yet to draft (HA-659's AC5) - but still shown
    for a child that was re-parented after a note was already written, so
    the note can still be read and cleared. */
export function ReleaseNote({
  issue,
  onSave,
  error,
}: {
  issue: { parentKey: string | null; releaseNote: string | null };
  onSave: (next: string) => Promise<boolean>;
  error?: string | null;
}) {
  const coarse = useCoarsePointer();

  // After the hook: rules of hooks forbid an early return above it.
  if (!releaseNoteShown(issue)) return null;

  return (
    <IssueSection id="release-note" title="Release note">
      <DescriptionEditor
        title={null}
        noun="release note"
        value={issue.releaseNote ?? ''}
        onSave={onSave}
        error={error}
        editorClassName="hatch-grows"
        clampTo={coarse ? 3 : null}
        rows={4}
      />
    </IssueSection>
  );
}
