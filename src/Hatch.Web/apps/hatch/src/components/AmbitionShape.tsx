/* Shape an ambition: the Dreams page's section for typing what a person wants,
   adding pictures or a video, and pressing once to file a draft ambition.

   Two parts, as WeaveModeControl has: `AmbitionShape` holds the state and makes
   the calls; `AmbitionShapeView` draws from props alone, so a test can render
   it to static markup without a DOM. The page's one Project picker and its
   Choose the dreams switch are read, not repeated - the page hands down the
   picked project, the switch and the dreams listed. */

import { useEffect, useRef, useState, type ChangeEvent, type DragEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button } from '@hatch/ui';
import { shapeAmbition, uploadAttachment } from '../api/client';
import {
  FILES_TAKEN_OFF,
  addFiles,
  bytesToSend,
  dreamsLine,
  shapeBlocker,
  shapeRequest,
  withoutFile,
  type ShapeFile,
} from '../lib/ambitionShape';
import { formatSize } from '../lib/attachmentList';
import { dragHasFiles } from '../lib/descriptionImage';
import { message } from '../lib/errors';
import type { Dream } from '../types';

export interface AmbitionShapeViewProps {
  projectId: number | null;
  projectCount: number;
  useOnScreen: boolean;
  dreams: Dream[];
  words: string;
  files: ShapeFile[];
  /** One sentence per file the server refused, each naming the file. */
  refusals: string[];
  /** The files were taken off because the project changed. */
  changed: boolean;
  uploading: boolean;
  pressing: boolean;
  /** A refusal of the press itself. */
  error: string | null;
  /** A file is being dragged over the section. */
  dragging: boolean;
  onWords: (words: string) => void;
  onPick: (files: File[]) => void;
  onTakeOff: (id: string) => void;
  onPress: () => void;
  /** The drag handlers the section wears; left off, nothing is droppable. */
  drop?: {
    onDragEnter: (e: DragEvent<HTMLElement>) => void;
    onDragLeave: (e: DragEvent<HTMLElement>) => void;
    onDragOver: (e: DragEvent<HTMLElement>) => void;
    onDrop: (e: DragEvent<HTMLElement>) => void;
  };
}

export function AmbitionShapeView(props: AmbitionShapeViewProps) {
  const { projectId, projectCount, useOnScreen, dreams, words, files, refusals, changed, uploading, pressing, error, dragging } =
    props;

  const request = shapeRequest({ projectId, words, useOnScreen, dreams, files });
  const blocker = shapeBlocker({ projectId, projectCount, words, useOnScreen, dreams, files, uploading });

  return (
    <section className="hatch-dream-weave hatch-dream-shape" aria-labelledby="hatch-dream-shape-title" {...props.drop}>
      <h2
        id="hatch-dream-shape-title"
        className={`hatch-gradient-ink hatch-dream-weave__title${pressing ? ' hatch-dream-weave__title--filing' : ''}`}
      >
        Shape an ambition
      </h2>
      <p className="hatch-dream-weave__lede">Files a draft ambition and takes you to it.</p>

      <div className="hatch-dream-shape__words">
        <textarea
          rows={3}
          aria-label="Words"
          className="hatch-dream-shape__input"
          placeholder="Say what you want…"
          value={words}
          onChange={(e) => props.onWords(e.target.value)}
        />
      </div>

      <p className="hatch-dream-weave__choose-hint">{dreamsLine(useOnScreen)}</p>

      <div className="hatch-dream-shape__picker">
        <input
          type="file"
          multiple
          accept="image/*,video/*"
          aria-label="Add images or videos"
          disabled={projectId === null || pressing}
          onChange={(e: ChangeEvent<HTMLInputElement>) => {
            const picked = Array.from(e.target.files ?? []);
            e.target.value = '';
            if (picked.length > 0) props.onPick(picked);
          }}
        />
        {refusals.map((line) => (
          <p key={line} className="text-danger hatch-dream-shape__refusal">
            {line}
          </p>
        ))}
        {changed && files.length === 0 && <p className="text-muted hatch-dream-shape__refusal">{FILES_TAKEN_OFF}</p>}
      </div>

      {files.length > 0 && (
        <ul className="hatch-dream-shape__files" aria-label="Files added">
          {files.map((file) => (
            <li key={file.id} className="hatch-dream-shape__file">
              <span className="hatch-dream-shape__file-name">{file.name}</span>
              <span className="text-muted hatch-dream-shape__file-size">{formatSize(file.size)}</span>
              <button
                type="button"
                className="hatch-dream-shape__file-off"
                aria-label={`Take off ${file.name}`}
                disabled={pressing}
                onClick={() => props.onTakeOff(file.id)}
              >
                Take off
              </button>
            </li>
          ))}
        </ul>
      )}

      <div className="hatch-dream-weave__actions">
        <Button
          variant="primary"
          className="hatch-gradient-fill"
          loading={pressing}
          disabled={request === null || uploading}
          aria-describedby={blocker ? 'hatch-dream-shape-why' : undefined}
          onClick={props.onPress}
        >
          Shape
        </Button>
        {blocker && (
          <p id="hatch-dream-shape-why" className="text-muted hatch-dream-weave__why">
            {blocker}
          </p>
        )}
        {error && <p className="text-danger">{error}</p>}
      </div>

      {dragging && (
        <div className="hatch-dropzone__overlay hatch-dream-shape__overlay" aria-hidden="true">
          Drop to attach
        </div>
      )}
    </section>
  );
}

/** The files held for the project they were uploaded to. Files held for any
    other project than the one picked now are not shown, and are not sent. */
interface Shelf {
  projectId: number | null;
  rows: ShapeFile[];
  refusals: string[];
}

export function AmbitionShape({
  projectId,
  projectCount,
  useOnScreen,
  dreams,
}: {
  projectId: number | null;
  projectCount: number;
  useOnScreen: boolean;
  dreams: Dream[];
}) {
  const navigate = useNavigate();
  const [words, setWords] = useState('');
  const [shelf, setShelf] = useState<Shelf>({ projectId, rows: [], refusals: [] });
  const [uploading, setUploading] = useState(0);
  const [pressing, setPressing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);
  const depth = useRef(0);
  const projectRef = useRef(projectId);
  useEffect(() => {
    projectRef.current = projectId;
  });

  // The route refuses a file linked to another project, so a change of
  // project leaves the files behind, with a line saying so.
  const held = shelf.projectId === projectId;
  const files = held ? shelf.rows : [];
  const refusals = held ? shelf.refusals : [];
  const changed = shelf.projectId !== projectId && shelf.rows.length > 0;

  async function add(picked: File[]) {
    if (projectId === null || pressing || picked.length === 0) return;
    const into = projectId;
    setUploading((n) => n + 1);
    try {
      const added = await addFiles(picked, async (file) => uploadAttachment(await bytesToSend(file), file.name, into));
      // The person changed the project while the files went up: they were
      // stored against the old one and are left there.
      if (projectRef.current !== into) return;
      setShelf((prev) => ({
        projectId: into,
        rows: [...(prev.projectId === into ? prev.rows : []), ...added.rows],
        refusals: added.refusals,
      }));
    } finally {
      setUploading((n) => n - 1);
    }
  }

  async function press() {
    const request = shapeRequest({ projectId, words, useOnScreen, dreams, files });
    if (request === null || uploading > 0 || pressing) return;
    setPressing(true);
    setError(null);

    try {
      const draft = await shapeAmbition(request);
      navigate(`/issues/${draft.key}`);
    } catch (err) {
      setError(message(err));
      setPressing(false);
    }
  }

  // Only a drag that carries files is the section's; text and links are left
  // to the browser, as AttachmentDropZone leaves them.
  const carriesFiles = (e: DragEvent<HTMLElement>) => dragHasFiles(Array.from(e.dataTransfer?.types ?? []));

  return (
    <AmbitionShapeView
      projectId={projectId}
      projectCount={projectCount}
      useOnScreen={useOnScreen}
      dreams={dreams}
      words={words}
      files={files}
      refusals={refusals}
      changed={changed}
      uploading={uploading > 0}
      pressing={pressing}
      error={error}
      dragging={dragging}
      onWords={setWords}
      onPick={(picked) => void add(picked)}
      onTakeOff={(id) => setShelf({ projectId, rows: withoutFile(files, id), refusals })}
      onPress={() => void press()}
      drop={{
        onDragEnter: (e) => {
          if (!carriesFiles(e)) return;
          depth.current += 1;
          setDragging(true);
        },
        onDragLeave: (e) => {
          if (!carriesFiles(e)) return;
          depth.current -= 1;
          if (depth.current <= 0) {
            depth.current = 0;
            setDragging(false);
          }
        },
        onDragOver: (e) => {
          if (!carriesFiles(e)) return;
          e.preventDefault();
          e.dataTransfer.dropEffect = 'copy';
        },
        onDrop: (e) => {
          if (!carriesFiles(e)) return;
          e.preventDefault();
          depth.current = 0;
          setDragging(false);
          void add(Array.from(e.dataTransfer.files));
        },
      }}
    />
  );
}
