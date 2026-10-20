import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ReleaseNote } from './ReleaseNote';

// Same reasoning as ProseClamp.test.tsx and IssueSection.test.tsx: a static
// render with no DOM, so the editor's own dependencies need the same stubs.
vi.mock('./MarkdownEditor', () => ({ MarkdownEditor: () => null }));
vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));

afterEach(() => vi.unstubAllGlobals());

function stubStorage() {
  vi.stubGlobal('window', {
    localStorage: {
      getItem: () => null,
      setItem: () => undefined,
    },
  });
}

const onSave = async () => true;

describe('ReleaseNote', () => {
  it('renders nothing for a child issue with no note', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      <ReleaseNote issue={{ parentKey: 'HA-1', releaseNote: null }} onSave={onSave} />,
    );

    expect(html).toBe('');
  });

  it('shows the section with the empty state and Edit alone on a top-level issue with none', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      <ReleaseNote issue={{ parentKey: null, releaseNote: null }} onSave={onSave} />,
    );

    expect(html).toContain('Release note');
    expect(html).toContain('No release note yet.');
    expect(html).toContain('>Edit<');
    expect(html).not.toContain('>Save<');
  });

  it('renders the note as markdown', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      <ReleaseNote issue={{ parentKey: null, releaseNote: 'Ships **today**' }} onSave={onSave} />,
    );

    expect(html).toContain('hatch-markdown');
    expect(html).toContain('Ships **today**');
    expect(html).not.toContain('No release note yet.');
  });

  it('still shows a re-parented child that already has a note', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      <ReleaseNote issue={{ parentKey: 'HA-1', releaseNote: 'Ships **today**' }} onSave={onSave} />,
    );

    expect(html).toContain('Release note');
    expect(html).toContain('Ships **today**');
  });

  it('shows a refusal', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      <ReleaseNote
        issue={{ parentKey: null, releaseNote: null }}
        onSave={onSave}
        error="a release note is at most 2000 characters"
      />,
    );

    expect(html).toContain('text-danger');
    expect(html).toContain('a release note is at most 2000 characters');
  });
});
