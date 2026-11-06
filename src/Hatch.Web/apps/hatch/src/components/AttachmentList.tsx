/* Lists the attachments linked anywhere in a block of markdown - the ids
   `linkedAttachmentIds` scans out of it - fetching whichever of them has not
   already been asked for and caching the answers for as long as this stays
   mounted.

   `known` is read through a ref so the fetching effect depends on the id key
   alone: depending on `known` directly would re-run the effect after every
   settle and retry a failed id in a loop. The unmount guard lives in its own
   mount-only effect, since the keyed effect's cleanup runs on every key
   change and an in-flight answer must still land by id whatever the current
   text has become by then. */

import { useEffect, useRef, useState } from 'react';
import { Button } from '@hatch/ui';
import { deleteAttachment, getAttachments } from '../api/client';
import { askToDeleteAttachment } from '../lib/deletion';
import {
  attachmentRows,
  attachmentUrl,
  chunks,
  formatSize,
  linkedAttachmentIds,
  missingIds,
  settle,
  type AttachmentRow,
  type Known,
} from '../lib/attachmentList';

export function AttachmentList({
  text,
  onDeleted,
  onDeleteError,
}: {
  text: string;
  onDeleted: (id: string) => void;
  onDeleteError: (message: string) => void;
}) {
  const key = linkedAttachmentIds(text).join(',');
  const ids = key === '' ? [] : key.split(',');

  const [known, setKnown] = useState<Known>(new Map());
  const [failed, setFailed] = useState<ReadonlySet<string>>(new Set());
  const [deleting, setDeleting] = useState<ReadonlySet<string>>(new Set());
  const inFlight = useRef(new Set<string>());
  const live = useRef(true);

  const knownRef = useRef(known);
  knownRef.current = known;

  useEffect(() => {
    live.current = true;
    return () => {
      live.current = false;
    };
  }, []);

  useEffect(() => {
    const missing = missingIds(ids, knownRef.current, inFlight.current);
    if (missing.length === 0) return;

    for (const id of missing) inFlight.current.add(id);
    setFailed((f) => {
      if (!missing.some((id) => f.has(id))) return f;
      const next = new Set(f);
      for (const id of missing) next.delete(id);
      return next;
    });

    for (const chunk of chunks(missing, 100)) {
      getAttachments(chunk).then(
        (found) => {
          for (const id of chunk) inFlight.current.delete(id);
          if (live.current) setKnown((k) => settle(k, chunk, found));
        },
        () => {
          for (const id of chunk) inFlight.current.delete(id);
          if (live.current) setFailed((f) => new Set([...f, ...chunk]));
        },
      );
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  async function handleDelete(id: string, fileName: string) {
    setDeleting((d) => new Set(d).add(id));
    const outcome = await askToDeleteAttachment(fileName, (q) => confirm(q), () => deleteAttachment(id));
    setDeleting((d) => {
      const next = new Set(d);
      next.delete(id);
      return next;
    });
    if (outcome.outcome === 'deleted') onDeleted(id);
    else if (outcome.outcome === 'refused') onDeleteError(outcome.error);
  }

  if (ids.length === 0) return null;
  return (
    <AttachmentRows rows={attachmentRows(ids, known, failed)} deleting={deleting} onDelete={handleDelete} />
  );
}

export function AttachmentRows({
  rows,
  deleting,
  onDelete,
}: {
  rows: AttachmentRow[];
  deleting: ReadonlySet<string>;
  onDelete: (id: string, fileName: string) => void;
}) {
  if (rows.length === 0) return null;
  return (
    <section className="hatch-attachments" aria-label="Attachments">
      <ul>
        {rows.map((row) => (
          <li key={row.id}>
            {row.state === 'file' && row.attachment && (
              <>
                <a href={attachmentUrl(row.id)} download={row.attachment.fileName}>
                  {row.attachment.fileName}
                </a>
                <span className="text-muted">{formatSize(row.attachment.size)}</span>
                <Button
                  variant="danger"
                  loading={deleting.has(row.id)}
                  onClick={() => onDelete(row.id, row.attachment!.fileName)}
                >
                  Delete
                </Button>
              </>
            )}
            {row.state === 'deleted' && <span className="text-muted">(deleted)</span>}
            {row.state === 'loading' && <span className="text-muted">Loading…</span>}
            {row.state === 'unavailable' && (
              <>
                <a href={attachmentUrl(row.id)}>{row.id}</a>
                <span className="text-muted">(unavailable)</span>
              </>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
