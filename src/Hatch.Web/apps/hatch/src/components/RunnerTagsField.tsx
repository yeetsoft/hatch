import type { ReactNode } from 'react';
import { Field } from '@hatch/ui';

export function RunnerTagsField({
  label,
  hint,
  value,
  onChange,
  disabled = false,
  busy = false,
}: {
  label: string;
  hint: ReactNode;
  value: string;
  onChange: (next: string) => void;
  disabled?: boolean;
  busy?: boolean;
}) {
  return (
    <Field label={label} hint={hint} as="div">
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
