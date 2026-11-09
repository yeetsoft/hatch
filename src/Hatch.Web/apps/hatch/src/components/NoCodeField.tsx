import { useState } from 'react';
import { Button, Field } from '@hatch/ui';
import { NO_CODE_MAX_LENGTH, noCodeDraft, noCodeRequest } from '../lib/noCode';

/**
 * The reason this ticket finished with no code, set or cleared from the edit
 * view. Presentational: the page owns the call, and `error` is the API's own
 * sentence, shown as it came - nothing is decided here about pull requests or
 * branches.
 */
export function NoCodeField({
  noCode,
  busy,
  error,
  onSave,
}: {
  noCode: string | null;
  busy: boolean;
  error: string | null;
  onSave: (reason: string) => Promise<boolean>;
}) {
  const [draft, setDraft] = useState(noCodeDraft(noCode));
  const [known, setKnown] = useState(noCode);

  // Re-syncs when the issue changes underneath - see WipLimitField.
  if (noCode !== known) {
    setKnown(noCode);
    setDraft(noCodeDraft(noCode));
  }

  const reason = noCodeRequest(draft).reason;

  return (
    <Field
      label="Finished with no code"
      as="div"
      hint="One line: why this ticket needed no change. Not allowed once it has a pull request or a branch on origin."
    >
      <input
        type="text"
        value={draft}
        maxLength={NO_CODE_MAX_LENGTH}
        disabled={busy}
        onChange={(e) => setDraft(e.target.value)}
      />
      <div className="hatch-section-actions">
        <Button disabled={busy || reason === '' || reason === noCode} onClick={() => void onSave(draft)}>
          Save
        </Button>
        {noCode && (
          <Button disabled={busy} onClick={() => void onSave('')}>
            Clear
          </Button>
        )}
      </div>
      {error !== null && <p className="text-danger">{error}</p>}
    </Field>
  );
}
