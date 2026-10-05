import { useEffect, useState } from 'react';
import { Button, Field } from '@hatch/ui';
import { message } from '../lib/errors';
import { LOGO_ACCEPTED_TYPES, LOGO_BOX, logoCrop } from '../lib/projectLogo';

/** What a chosen file becomes before Save: a downscaled blob with a preview
    URL to show it, `'removed'` once Remove has been pressed, or `null` for
    untouched. Exported so `ProjectEditDialog` can type the state it mirrors
    this up into. */
export type LogoPending = { blob: Blob; previewUrl: string } | 'removed' | null;

/**
 * The logo file input and its Remove button, inside `ProjectEditDialog`.
 *
 * `pending` and its decode `error` are local - the parent never needs either
 * beyond what it receives through `onChange`, and a decode failure
 * (`createImageBitmap` rejecting, or `canvas.toBlob` returning null) is a
 * per-field problem shown through `Field`'s own error slot, not the dialog's.
 *
 * The object URL for a chosen file is revoked by the `useEffect` below, keyed
 * on `pending` - that fires on replacement, on Remove, and on unmount
 * (closing the dialog or switching projects, since the parent remounts this
 * with `key={project.id}`), so there is exactly one place that ever revokes.
 */
export function ProjectLogoField({
  logoUpdatedAt,
  onChange,
}: {
  logoUpdatedAt: string | null;
  onChange: (pending: LogoPending) => void;
}) {
  const [pending, setPending] = useState<LogoPending>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (pending === null || pending === 'removed') return;
    return () => URL.revokeObjectURL(pending.previewUrl);
  }, [pending]);

  function set(next: LogoPending) {
    setPending(next);
    onChange(next);
  }

  async function onFile(file: File) {
    try {
      const bitmap = await createImageBitmap(file);
      const { sx, sy, size } = logoCrop(bitmap.width, bitmap.height);
      const canvas = document.createElement('canvas');
      canvas.width = LOGO_BOX;
      canvas.height = LOGO_BOX;
      const ctx = canvas.getContext('2d');
      if (!ctx) {
        setError('Could not read that image.');
        return;
      }
      ctx.drawImage(bitmap, sx, sy, size, size, 0, 0, LOGO_BOX, LOGO_BOX);
      canvas.toBlob((blob) => {
        if (!blob) {
          setError('Could not read that image.');
          return;
        }
        setError(null);
        set({ blob, previewUrl: URL.createObjectURL(blob) });
      }, 'image/png');
    } catch (err) {
      setError(message(err));
    }
  }

  return (
    <Field label="Logo" error={error}>
      <div className="hatch-inline-form">
        <input
          type="file"
          accept={LOGO_ACCEPTED_TYPES.join(',')}
          onChange={(e) => {
            const file = e.target.files?.[0];
            e.target.value = '';
            if (file) void onFile(file);
          }}
        />
        <Button variant="danger" disabled={logoUpdatedAt === null} onClick={() => set('removed')}>
          Remove
        </Button>
      </div>
    </Field>
  );
}
