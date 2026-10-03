import { Field } from '@hatch/ui';
import { normalizeProjectKey, rekeyObjection } from '../lib/projectKey';

/**
 * The key input, and the speed bump beside it.
 *
 * The confirmation field renders only once the typed key actually differs
 * from `knownKey` - unchanged, there is nothing to confirm. Its objection
 * text lives in `Field`'s own `error` slot, which is the live sentence the
 * story asks for - see lib/projectKey.ts for the rules themselves.
 *
 * Deliberately out of scope: the "N issues will be renumbered" warning
 * RekeyDialog shows today. This field carries no issue count to build that
 * sentence from.
 */
export function ProjectKeyField({
  knownKey,
  value,
  onChange,
  confirmation,
  onConfirmationChange,
}: {
  knownKey: string;
  value: string;
  onChange: (next: string) => void;
  confirmation: string;
  onConfirmationChange: (next: string) => void;
}) {
  const changing = normalizeProjectKey(value) !== normalizeProjectKey(knownKey);

  return (
    <>
      <Field label="Key" hint="Two to six characters, e.g. AER.">
        <input value={value} onChange={(e) => onChange(e.target.value.toUpperCase())} />
      </Field>
      {changing && (
        <Field label={`Type ${knownKey} to confirm`} error={rekeyObjection(knownKey, value, confirmation)}>
          <input value={confirmation} onChange={(e) => onConfirmationChange(e.target.value.toUpperCase())} />
        </Field>
      )}
    </>
  );
}
