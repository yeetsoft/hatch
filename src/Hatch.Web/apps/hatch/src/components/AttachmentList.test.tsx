import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { AttachmentRows } from './AttachmentList';
import type { AttachmentRow } from '../lib/attachmentList';
import type { Attachment } from '../types';

const ID_A = '11111111-1111-1111-1111-111111111111';

function attachment(overrides: Partial<Attachment> = {}): Attachment {
  return { id: ID_A, fileName: 'file.pdf', contentType: 'application/pdf', size: 2048, createdAt: '2026-01-01', ...overrides };
}

describe('AttachmentRows', () => {
  it('draws a file row with its download link and formatted size', () => {
    const rows: AttachmentRow[] = [{ id: ID_A, state: 'file', attachment: attachment() }];
    const html = renderToStaticMarkup(<AttachmentRows rows={rows} deleting={new Set()} onDelete={() => {}} />);

    expect(html).toContain(`href="/api/hatch/attachments/${ID_A}"`);
    expect(html).toContain('download="file.pdf"');
    expect(html).toContain('2 KB');
  });

  it('escapes a file name carrying markup, never rendering it as an element', () => {
    const rows: AttachmentRow[] = [{ id: ID_A, state: 'file', attachment: attachment({ fileName: '<b>x</b> [y](z).pdf' }) }];
    const html = renderToStaticMarkup(<AttachmentRows rows={rows} deleting={new Set()} onDelete={() => {}} />);

    expect(html).toContain('&lt;b&gt;');
    expect(html).not.toContain('<b>');
  });

  it('draws a deleted row with no link', () => {
    const rows: AttachmentRow[] = [{ id: ID_A, state: 'deleted' }];
    const html = renderToStaticMarkup(<AttachmentRows rows={rows} deleting={new Set()} onDelete={() => {}} />);

    expect(html).toContain('(deleted)');
    expect(html).not.toContain('<a');
  });

  it('draws a loading row with no link', () => {
    const rows: AttachmentRow[] = [{ id: ID_A, state: 'loading' }];
    const html = renderToStaticMarkup(<AttachmentRows rows={rows} deleting={new Set()} onDelete={() => {}} />);

    expect(html).toContain('Loading…');
    expect(html).not.toContain('<a');
  });

  it('draws an unavailable row with the download link still on the id', () => {
    const rows: AttachmentRow[] = [{ id: ID_A, state: 'unavailable' }];
    const html = renderToStaticMarkup(<AttachmentRows rows={rows} deleting={new Set()} onDelete={() => {}} />);

    expect(html).toContain('(unavailable)');
    expect(html).toContain(`href="/api/hatch/attachments/${ID_A}"`);
  });

  it('draws nothing for no rows', () => {
    expect(renderToStaticMarkup(<AttachmentRows rows={[]} deleting={new Set()} onDelete={() => {}} />)).toBe('');
  });

  it('draws a disabled Delete button for a file row whose id is deleting', () => {
    const rows: AttachmentRow[] = [{ id: ID_A, state: 'file', attachment: attachment() }];
    const html = renderToStaticMarkup(
      <AttachmentRows rows={rows} deleting={new Set([ID_A])} onDelete={() => {}} />,
    );

    expect(html).toContain('disabled=""');
  });

  it('draws no Delete button for a deleted, a loading or an unavailable row', () => {
    const rows: AttachmentRow[] = [
      { id: ID_A, state: 'deleted' },
      { id: '22222222-2222-2222-2222-222222222222', state: 'loading' },
      { id: '33333333-3333-3333-3333-333333333333', state: 'unavailable' },
    ];
    const html = renderToStaticMarkup(<AttachmentRows rows={rows} deleting={new Set()} onDelete={() => {}} />);

    expect(html).not.toContain('>Delete<');
  });
});
