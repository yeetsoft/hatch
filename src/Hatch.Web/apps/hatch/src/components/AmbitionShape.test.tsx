import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { AmbitionShapeView, type AmbitionShapeViewProps } from './AmbitionShape';
import type { Dream } from '../types';

const props: AmbitionShapeViewProps = {
  projectId: 1,
  projectCount: 2,
  useOnScreen: false,
  dreams: [],
  words: 'a thing',
  files: [],
  refusals: [],
  changed: false,
  uploading: false,
  pressing: false,
  error: null,
  dragging: false,
  onWords: () => {},
  onPick: () => {},
  onTakeOff: () => {},
  onPress: () => {},
};

const draw = (over: Partial<AmbitionShapeViewProps> = {}) =>
  renderToStaticMarkup(<AmbitionShapeView {...props} {...over} />);

/** The Shape button's opening tag. */
const pill = (html: string) => /<button[^>]*hatch-gradient-fill[^>]*>/.exec(html)?.[0] ?? '';

describe('AmbitionShapeView', () => {
  it('draws the heading, the lede, the words box and the picker', () => {
    const html = draw();

    expect(html).toContain('Shape an ambition');
    expect(html).toContain('Files a draft ambition and takes you to it.');
    expect(html).toContain('hatch-dream-weave hatch-dream-shape');
    expect(html).toContain('aria-label="Words"');
    expect(html).toContain('multiple');
    expect(html).toContain('accept="image/*,video/*"');
    expect(html).toContain('aria-label="Add images or videos"');
  });

  it('leaves the pill pressable and says nothing when a request can be made', () => {
    const html = draw();

    expect(pill(html)).not.toContain('disabled');
    expect(html).not.toContain('hatch-dream-shape-why');
  });

  it('dims the pill and says why with no project picked', () => {
    const html = draw({ projectId: null });

    expect(pill(html)).toContain('disabled');
    expect(pill(html)).toContain('aria-describedby="hatch-dream-shape-why"');
    expect(html).toContain('Pick a project above to shape.');
  });

  it('dims the pill and says why when nothing was given', () => {
    const html = draw({ words: '' });

    expect(pill(html)).toContain('disabled');
    expect(html).toContain('Give some words, a dream or a file first.');
  });

  it('dims the pill and says why while a file uploads', () => {
    const html = draw({ uploading: true });

    expect(pill(html)).toContain('disabled');
    expect(html).toContain('A file is still uploading.');
  });

  it('says which of the dreams go in, both ways', () => {
    expect(draw({ useOnScreen: true, dreams: [{ id: 1 } as Dream] })).toContain(
      'The dreams listed go in, in the order shown.',
    );
    expect(draw()).toContain('No dreams go in.');
  });

  it('counts dreams as given only with the switch on', () => {
    const dreams = [{ id: 1 } as Dream];

    expect(pill(draw({ words: '', useOnScreen: true, dreams }))).not.toContain('disabled');
    expect(pill(draw({ words: '', useOnScreen: false, dreams }))).toContain('disabled');
  });

  it('draws a row for each file with its name, its size and a control naming it', () => {
    const html = draw({ files: [{ id: 'a', name: 'cat.png', size: 2048 }] });

    expect(html).toContain('cat.png');
    expect(html).toContain('2 KB');
    expect(html).toContain('aria-label="Take off cat.png"');
  });

  it('draws a refusal beside the picker and no row', () => {
    const html = draw({ refusals: ['clip.mov: That file is too large.'] });

    expect(html).toContain('clip.mov: That file is too large.');
    expect(html).not.toContain('Take off');
  });

  it('says the files were taken off when the project changed', () => {
    expect(draw({ changed: true })).toContain('The project changed, so the files were taken off.');
    expect(draw()).not.toContain('The project changed');
  });

  it('draws a refusal of the press beside the pill', () => {
    expect(draw({ error: 'no playbook row shapes an ambition' })).toContain('no playbook row shapes an ambition');
  });

  it('draws the drop overlay only while a file is dragged over', () => {
    expect(draw({ dragging: true })).toContain('Drop to attach');
    expect(draw()).not.toContain('Drop to attach');
  });

  it('moves the heading while pressing', () => {
    expect(draw({ pressing: true })).toContain('hatch-dream-weave__title--filing');
    expect(draw()).not.toContain('hatch-dream-weave__title--filing');
  });

  it('in the dialog leaves out the heading, the lede and the dreams line', () => {
    const html = draw({ inDialog: true, useOnScreen: true, dreams: [{ id: 1 } as Dream] });

    expect(html).not.toContain('<h2');
    expect(html).not.toContain('Files a draft ambition and takes you to it.');
    expect(html).not.toContain('No dreams go in.');
    expect(html).not.toContain('dreams listed');
    expect(html).toContain('aria-label="Shape an ambition"');
    expect(html).not.toContain('aria-labelledby');
    expect(html).toContain('hatch-dream-weave hatch-dream-shape');
  });

  it('in the dialog still draws the words box, the picker and Shape', () => {
    const html = draw({ inDialog: true });

    expect(html).toContain('aria-label="Words"');
    expect(html).toContain('aria-label="Add images or videos"');
    expect(pill(html)).not.toContain('disabled');
  });

  it('in the dialog names no dream in the blocker', () => {
    const html = draw({ inDialog: true, words: '' });

    expect(pill(html)).toContain('disabled');
    expect(html).toContain('Give some words or a file first.');
    expect(html).not.toContain('a dream');
  });
});
