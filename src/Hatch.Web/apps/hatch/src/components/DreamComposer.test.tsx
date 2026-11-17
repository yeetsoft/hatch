import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { DreamComposer } from './DreamComposer';

// There is no jsdom here, so the editor is a stub that records what the
// composer hands it. What a press or a post does is tested in
// lib/dreamComposer.test.ts.
const seen = vi.hoisted(() => [] as Record<string, unknown>[]);
vi.mock('./MarkdownEditor', () => ({
  MarkdownEditor: (props: Record<string, unknown>) => {
    seen.push(props);
    return <textarea aria-label={String(props.ariaLabel)} placeholder={String(props.placeholder)} />;
  },
}));

function render() {
  seen.length = 0;
  return renderToStaticMarkup(<DreamComposer onPost={() => Promise.resolve()} />);
}

describe('DreamComposer', () => {
  it('asks what you are dreaming about, in a borderless box of its own class', () => {
    render();

    expect(seen[0]).toMatchObject({
      placeholder: 'What are you dreaming about?',
      className: 'hatch-dream-box',
      ariaLabel: 'Dream',
      deferred: true,
    });
  });

  it('is no card', () => {
    expect(render()).not.toContain('hatch-card');
  });

  it('opens with Post disabled, as the gradient pill, under the box', () => {
    const html = render();

    expect(html).toMatch(/<button[^>]*class="hatch-btn hatch-btn--primary hatch-gradient-fill"[^>]*disabled=""[^>]*>Post<\/button>/);
    expect(html.indexOf('hatch-dream-composer__box')).toBeLessThan(html.indexOf('hatch-dream-composer__actions'));
  });

  it('folds Source behind a control, with no input until it is pressed', () => {
    const html = render();

    expect(html).toContain('aria-expanded="false"');
    expect(html).not.toContain('aria-label="Source"');
  });

  it('shows no refusal and no ghost at rest', () => {
    const html = render();

    expect(html).not.toContain('role="alert"');
    expect(html).not.toContain('hatch-dream-composer__ghost');
  });
});
