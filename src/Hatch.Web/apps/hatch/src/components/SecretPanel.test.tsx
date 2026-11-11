import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { SecretPanel } from './SecretPanel';

describe('SecretPanel', () => {
  it('draws the heading, both commands in order, and Done', () => {
    const html = renderToStaticMarkup(
      <SecretPanel secret={{ name: 'Quill', secret: 'hatch_ak_abc', rotated: false }} onDone={() => {}} />,
    );
    expect(html).toContain('New secret for Quill');
    expect(html).toMatch(/hatch config --origin[\s\S]*hatch config --key hatch_ak_abc/);
    expect(html).toContain('>Done</button>');
    expect(html).not.toContain('The old secret stopped working');
  });

  it('draws the rotated sentence only when rotated', () => {
    const html = renderToStaticMarkup(
      <SecretPanel secret={{ name: 'Quill', secret: 'hatch_ak_new', rotated: true }} onDone={() => {}} />,
    );
    expect(html).toContain('The old secret stopped working when this was made.');
  });
});
