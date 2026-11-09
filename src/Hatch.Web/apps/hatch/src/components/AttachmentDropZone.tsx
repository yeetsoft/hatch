/* Wraps a description's edit/preview with a drag-and-drop surface for files.

   LISTENERS ARE NATIVE, on the wrapper, capture phase - the same reach
   MarkdownEditor's own paste listener uses to get there before Monaco does.
   A drag that carries no files (text, a link) is left alone: every handler
   returns at once unless `dragHasFiles` says otherwise, so nothing here ever
   draws for a drag Monaco or the browser would rather have.

   `editor` and `onAppend` are read through refs refreshed each render - the
   `insertRef` / `onChangeRef` idiom in MarkdownEditor.tsx - since the upload
   they are read from outlives the render that started it. */

import { useEffect, useRef, useState, type ReactNode, type RefObject } from 'react';
import { uploadAttachment } from '../api/client';
import { dragHasFiles, markdownForDrop } from '../lib/descriptionImage';
import { message } from '../lib/errors';
import { LOGO_ACCEPTED_TYPES } from '../lib/projectLogo';
import { shrink } from '../lib/shrinkImage';
import type { MarkdownEditorHandle } from './MarkdownEditor';

export function AttachmentDropZone({
  editor,
  onAppend,
  disabled,
  children,
}: {
  /** Where a focused caret would take the uploaded markdown, if there is one. */
  editor: RefObject<MarkdownEditorHandle | null>;
  /** Where the markdown goes when no editor has the caret. */
  onAppend: (markdown: string) => void;
  disabled?: boolean;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const depth = useRef(0);
  const busy = useRef(false);
  const live = useRef(true);
  const [dragging, setDragging] = useState(false);
  const [note, setNote] = useState<string | null>(null);

  const editorRef = useRef(editor);
  useEffect(() => {
    editorRef.current = editor;
  });
  const onAppendRef = useRef(onAppend);
  useEffect(() => {
    onAppendRef.current = onAppend;
  });

  useEffect(() => {
    live.current = true;
    return () => {
      live.current = false;
    };
  }, []);

  useEffect(() => {
    const el = ref.current;
    if (!el || disabled) return;

    const carriesFiles = (e: DragEvent) => dragHasFiles(Array.from(e.dataTransfer?.types ?? []));

    const onDragEnter = (e: DragEvent) => {
      if (!carriesFiles(e)) return;
      depth.current += 1;
      setDragging(true);
    };

    const onDragLeave = (e: DragEvent) => {
      if (!carriesFiles(e)) return;
      depth.current -= 1;
      if (depth.current <= 0) {
        depth.current = 0;
        setDragging(false);
      }
    };

    const onDragOver = (e: DragEvent) => {
      if (!carriesFiles(e)) return;
      e.preventDefault();
      e.stopPropagation();
      if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
    };

    const onDrop = (e: DragEvent) => {
      if (!carriesFiles(e)) return;
      e.preventDefault();
      e.stopPropagation();
      depth.current = 0;
      setDragging(false);

      const files = Array.from(e.dataTransfer?.files ?? []);
      if (files.length === 0 || busy.current) return;
      busy.current = true;
      setNote(`Uploading ${files.length} file${files.length === 1 ? '' : 's'}…`);

      markdownForDrop(files, (f) =>
        (LOGO_ACCEPTED_TYPES.includes(f.type) ? shrink(f) : Promise.resolve(f)).then((blob) => uploadAttachment(blob, f.name)),
      ).then(
        (text) => {
          busy.current = false;
          if (!live.current) return;
          if (!editorRef.current.current?.insertAtCaret(text)) onAppendRef.current(text);
          setNote(null);
        },
        (err: unknown) => {
          busy.current = false;
          if (!live.current) return;
          setNote(message(err));
        },
      );
    };

    el.addEventListener('dragenter', onDragEnter, true);
    el.addEventListener('dragleave', onDragLeave, true);
    el.addEventListener('dragover', onDragOver, true);
    el.addEventListener('drop', onDrop, true);
    return () => {
      el.removeEventListener('dragenter', onDragEnter, true);
      el.removeEventListener('dragleave', onDragLeave, true);
      el.removeEventListener('dragover', onDragOver, true);
      el.removeEventListener('drop', onDrop, true);
    };
  }, [disabled]);

  return (
    <div ref={ref} className="hatch-dropzone">
      {children}
      {dragging && (
        <div className="hatch-dropzone__overlay" aria-hidden="true">
          Drop to attach
        </div>
      )}
      {note && (
        <p className="hatch-md-editor__failed" role="status">
          {note}
        </p>
      )}
    </div>
  );
}
