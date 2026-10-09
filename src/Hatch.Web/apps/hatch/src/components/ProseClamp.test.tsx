import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ProseClamp } from './ProseClamp';

// renderMarkdown's DOMPurify pair needs a real DOM to sanitize into, which
// this workspace's tests do not have (plain Node, no jsdom) - same reason
// IssuePeek.test.tsx stubs MarkdownEditor rather than let it touch
// window.matchMedia. The identity stub still lets these tests check what
// ProseClamp decided to show.
vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));

const LONG = 'one\n\ntwo\n\nthree\n\nfour';
const SHORT = 'one\n\ntwo';

describe('ProseClamp', () => {
  it('fades and offers a control when the source runs past the limit', () => {
    const html = renderToStaticMarkup(<ProseClamp source={LONG} limit={3} />);

    expect(html).toContain('hatch-prose-clamp--faded');
    expect(html).toContain('>More<');
    expect(html).not.toContain('four');
  });

  it('draws no fade and no control when the source fits the limit', () => {
    const html = renderToStaticMarkup(<ProseClamp source={SHORT} limit={3} />);

    expect(html).not.toContain('hatch-prose-clamp');
    expect(html).not.toContain('>More<');
    expect(html).toContain('two');
  });

  it('draws the whole source with no fade and no control when limit is null', () => {
    const html = renderToStaticMarkup(<ProseClamp source={LONG} limit={null} />);

    expect(html).not.toContain('hatch-prose-clamp');
    expect(html).not.toContain('>More<');
    expect(html).toContain('four');
  });

  it('appends an extra class onto the rendered markdown', () => {
    const html = renderToStaticMarkup(<ProseClamp source={SHORT} limit={3} className="hatch-question-body" />);

    expect(html).toContain('hatch-markdown hatch-question-body');
  });
});
