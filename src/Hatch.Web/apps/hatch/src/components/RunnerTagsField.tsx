import type { ReactNode } from 'react';
import { Field } from '@hatch/ui';

export function RunnerTagsField({
  label,
  hint,
  error,
  value,
  onChange,
  disabled = false,
  busy = false,
}: {
  label: ReactNode;
  hint?: ReactNode;
  error?: ReactNode;
  value: string;
  onChange: (next: string) => void;
  disabled?: boolean;
  busy?: boolean;
}) {
  return (
    <Field label={label} hint={hint} error={error} as="div">
      <textarea
        rows={2}
        className="hatch-grows"
        value={value}
        disabled={disabled || busy}
        onChange={(e) => onChange(e.target.value)}
      />
    </Field>
  );
}
