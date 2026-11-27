import { describe, expect, it } from 'vitest';
import {
  addFiles,
  dreamsLine,
  shapeBlocker,
  shapeProject,
  shapeRequest,
  withoutFile,
  type ShapeFile,
} from './ambitionShape';
import type { Dream, Project } from '../types';

const dreams = [{ id: 3 } as Dream, { id: 1 } as Dream, { id: 2 } as Dream];
const file = (id: string): ShapeFile => ({ id, name: `${id}.png`, size: 10 });

const ready = {
  projectId: 1,
  projectCount: 2,
  words: 'a thing',
  useOnScreen: false,
  dreams: [] as Dream[],
  files: [] as ShapeFile[],
  uploading: false,
};

const fileOf = (name: string, type: string) => ({ name, type }) as File;

describe('shapeRequest', () => {
  it('is null with no project', () => {
    expect(shapeRequest({ ...ready, projectId: null })).toBeNull();
  });

  it('is null when nothing was given, words once trimmed', () => {
    expect(shapeRequest({ ...ready, words: '  \n ' })).toBeNull();
  });

  it('sends the trimmed words alone, leaving the rest out', () => {
    expect(shapeRequest({ ...ready, words: '  a thing  ' })).toEqual({ projectId: 1, words: 'a thing' });
  });

  it('sends the dreams in list order with the switch on', () => {
    expect(shapeRequest({ ...ready, words: '', useOnScreen: true, dreams })).toEqual({
      projectId: 1,
      dreamIds: [3, 1, 2],
    });
  });

  it('sends no dreams with the switch off, and counts them as nothing given', () => {
    expect(shapeRequest({ ...ready, words: '', dreams })).toBeNull();
    expect(shapeRequest({ ...ready, dreams })).toEqual({ projectId: 1, words: 'a thing' });
  });

  it('sends no dreams with the switch on and an empty list', () => {
    expect(shapeRequest({ ...ready, useOnScreen: true })).toEqual({ projectId: 1, words: 'a thing' });
  });

  it('sends the files in row order, and a file alone is enough', () => {
    expect(shapeRequest({ ...ready, words: '', files: [file('b'), file('a')] })).toEqual({
      projectId: 1,
      attachmentIds: ['b', 'a'],
    });
  });
});

describe('shapeBlocker', () => {
  it('says there is no project to shape into when there are none', () => {
    expect(shapeBlocker({ ...ready, projectId: null, projectCount: 0 })).toBe('There is no project to shape into yet.');
  });

  it('asks for a project first, ahead of every other reason', () => {
    const none = { ...ready, projectId: null, words: '', uploading: true };
    expect(shapeBlocker(none)).toBe('Pick a project above to shape.');
  });

  it('names a file still uploading ahead of nothing given', () => {
    expect(shapeBlocker({ ...ready, words: '', uploading: true })).toBe('A file is still uploading.');
  });

  it('asks for something when nothing was given', () => {
    expect(shapeBlocker({ ...ready, words: ' ' })).toBe('Give some words, a dream or a file first.');
    expect(shapeBlocker({ ...ready, words: '', dreams })).toBe('Give some words, a dream or a file first.');
  });

  it('is null when a request can be made and nothing is uploading', () => {
    expect(shapeBlocker(ready)).toBeNull();
    expect(shapeBlocker({ ...ready, words: '', useOnScreen: true, dreams })).toBeNull();
    expect(shapeBlocker({ ...ready, words: '', files: [file('a')] })).toBeNull();
  });
});

describe('dreamsLine', () => {
  it('says which', () => {
    expect(dreamsLine(true)).toBe('The dreams listed go in, in the order shown.');
    expect(dreamsLine(false)).toBe('No dreams go in.');
  });
});

describe('addFiles', () => {
  it('adds a row for each file, in order, with the stored size', async () => {
    const { rows, refusals } = await addFiles([fileOf('a.png', 'image/png'), fileOf('b.mp4', 'video/mp4')], (f) =>
      Promise.resolve({ id: `id-${f.name}`, size: 42 }),
    );

    expect(rows).toEqual([
      { id: 'id-a.png', name: 'a.png', size: 42 },
      { id: 'id-b.mp4', name: 'b.mp4', size: 42 },
    ]);
    expect(refusals).toEqual([]);
  });

  it('names a refused file with the server sentence, adds no row and goes on', async () => {
    const { rows, refusals } = await addFiles(
      [fileOf('a.png', 'image/png'), fileOf('big.mp4', 'video/mp4'), fileOf('c.png', 'image/png')],
      (f) =>
        f.name === 'big.mp4'
          ? Promise.reject(new Error('That file is too large.'))
          : Promise.resolve({ id: f.name, size: 1 }),
    );

    expect(refusals).toEqual(['big.mp4: That file is too large.']);
    expect(rows.map((r) => r.id)).toEqual(['a.png', 'c.png']);
  });

  it('refuses a type that is not an image or a video, with no call', async () => {
    const calls: string[] = [];
    const { rows, refusals } = await addFiles([fileOf('notes.pdf', 'application/pdf')], (f) => {
      calls.push(f.name);
      return Promise.resolve({ id: 'x', size: 1 });
    });

    expect(calls).toEqual([]);
    expect(rows).toEqual([]);
    expect(refusals).toEqual(['notes.pdf is not an image or a video.']);
  });
});

describe('withoutFile', () => {
  it('drops the one row and keeps the order of the rest', () => {
    expect(withoutFile([file('a'), file('b'), file('c')], 'b').map((r) => r.id)).toEqual(['a', 'c']);
  });
});

describe('shapeBlocker without dreams', () => {
  const base = { projectId: 1, projectCount: 1, words: '', useOnScreen: false, dreams: [], files: [], uploading: false };

  it('names no dream when nothing was given', () => {
    expect(shapeBlocker({ ...base, withDreams: false })).toBe('Give some words or a file first.');
  });

  it('keeps the dreams line by default', () => {
    expect(shapeBlocker(base)).toBe('Give some words, a dream or a file first.');
  });
});

describe('shapeProject', () => {
  const projects = [{ id: 1 }, { id: 2 }] as Project[];

  it('keeps a pick that is one of the projects', () => {
    expect(shapeProject(2, projects)).toBe(2);
  });

  it('takes the only project when none is picked', () => {
    expect(shapeProject(null, [{ id: 7 }] as Project[])).toBe(7);
  });

  it('is null with several projects and no pick, or a pick that is gone', () => {
    expect(shapeProject(null, projects)).toBeNull();
    expect(shapeProject(9, projects)).toBeNull();
    expect(shapeProject(null, [])).toBeNull();
  });
});
