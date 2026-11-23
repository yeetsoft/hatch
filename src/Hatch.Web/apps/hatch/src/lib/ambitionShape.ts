/* The pure side of shaping an ambition on the Dreams page: what a press sends,
   why it cannot be pressed, and the files added before it. Copied in shape
   from `weaveRequest` and `weaveBlocker` in lib/dreams.ts, which it sits
   beside; the component (components/AmbitionShape.tsx) holds only state. */

import { message } from './errors';
import { LOGO_ACCEPTED_TYPES } from './projectLogo';
import { shrink } from './shrinkImage';
import type { Attachment, Dream, DreamShapeRequest } from '../types';

/** One file added to a shaping: the stored attachment's id, and the name and
    size the row shows. */
export interface ShapeFile {
  id: string;
  name: string;
  size: number;
}

/** What a shaping press sends, or `null` while nothing has been given: no
    project, or no words, no dreams sent and no file. `words` is trimmed and
    left out when empty. `dreamIds` is sent, in list order, only with the
    switch on and a non-empty list. `attachmentIds` follow the rows' order. */
export function shapeRequest(args: {
  projectId: number | null;
  words: string;
  useOnScreen: boolean;
  dreams: Dream[];
  files: ShapeFile[];
}): DreamShapeRequest | null {
  const { projectId, words, useOnScreen, dreams, files } = args;
  if (projectId === null) return null;

  const request: DreamShapeRequest = { projectId };
  const trimmed = words.trim();
  if (trimmed !== '') request.words = trimmed;
  if (useOnScreen && dreams.length > 0) request.dreamIds = dreams.map((dream) => dream.id);
  if (files.length > 0) request.attachmentIds = files.map((file) => file.id);

  if (request.words === undefined && request.dreamIds === undefined && request.attachmentIds === undefined) return null;

  return request;
}

/** Why Shape cannot be pressed, or `null` when it can: the line the page puts
    beside the pill. In the order a person fixes them - no project, a file
    still going up, then nothing given. It is non-`null` whenever
    `shapeRequest` is `null`, and also while a file uploads, which the request
    does not know about. */
export function shapeBlocker(args: {
  projectId: number | null;
  projectCount: number;
  words: string;
  useOnScreen: boolean;
  dreams: Dream[];
  files: ShapeFile[];
  uploading: boolean;
}): string | null {
  const { projectId, projectCount, uploading } = args;
  if (projectId === null) {
    return projectCount === 0 ? 'There is no project to shape into yet.' : 'Pick a project above to shape.';
  }
  if (uploading) return 'A file is still uploading.';
  if (shapeRequest({ ...args, projectId }) === null) return 'Give some words, a dream or a file first.';

  return null;
}

/** The line that says whether the dreams go in. */
export const dreamsLine = (on: boolean): string =>
  on ? 'The dreams listed go in, in the order shown.' : 'No dreams go in.';

/** The sentence shown when the project changes under files already added: the
    route refuses a file linked to another project, so they are taken off. */
export const FILES_TAKEN_OFF = 'The project changed, so the files were taken off.';

export interface AddedFiles {
  rows: ShapeFile[];
  refusals: string[];
}

const isMedia = (type: string) => type.startsWith('image/') || type.startsWith('video/');

/** The bytes to send: an image the editor would also preview is redrawn
    smaller when over the limit, as a dropped image on a description is; a
    video, or any other image type, goes as it is. */
export const bytesToSend = (file: File): Promise<Blob> =>
  LOGO_ACCEPTED_TYPES.includes(file.type) ? shrink(file) : Promise.resolve(file);

/** Uploads `files` one after another. A file the server refuses becomes
    `name: <its sentence>` and adds no row, and the later files still go. A
    type that is neither an image nor a video is refused here, with no call. */
export async function addFiles(
  files: readonly File[],
  upload: (file: File) => Promise<Pick<Attachment, 'id' | 'size'>>,
): Promise<AddedFiles> {
  const rows: ShapeFile[] = [];
  const refusals: string[] = [];

  for (const file of files) {
    if (!isMedia(file.type)) {
      refusals.push(`${file.name} is not an image or a video.`);
      continue;
    }
    try {
      const stored = await upload(file);
      rows.push({ id: stored.id, name: file.name, size: stored.size });
    } catch (err) {
      refusals.push(`${file.name}: ${message(err)}`);
    }
  }

  return { rows, refusals };
}

/** `rows` without the one with `id`. */
export const withoutFile = (rows: readonly ShapeFile[], id: string): ShapeFile[] =>
  rows.filter((row) => row.id !== id);
