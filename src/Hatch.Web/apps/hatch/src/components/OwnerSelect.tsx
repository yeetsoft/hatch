import { getPeople } from '../api/client';
import { useLoaded } from '../lib/useLoaded';

/**
 * The people select behind an admin's Owner choice, with "nobody" first. It
 * reads `GET /api/people` itself, so a page that never draws it - a user's
 * Create dialog, a user's Identity card - never makes the call.
 */
export function OwnerSelect({
  value,
  onChange,
  disabled,
  label,
}: {
  value: string;
  onChange: (personId: string) => void;
  disabled?: boolean;
  /** An accessible name, where no visible `<label>` already names the select. */
  label?: string;
}) {
  const { data: people } = useLoaded(getPeople);
  return (
    <select aria-label={label} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)}>
      <option value="">nobody</option>
      {(people ?? []).map((person) => (
        <option key={person.id} value={person.id}>
          {person.name}
        </option>
      ))}
    </select>
  );
}
