import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { DescriptionEditor } from './DescriptionEditor';

// Same reasoning as ReleaseNote.test.tsx: a static render with no jsdom, and
// MarkdownEditor reads `window.matchMedia` on render. DescriptionEditor also
// opens in preview, not edit, so MarkdownEditor never mounts here without a
// press this environment cannot fire - NewIssueDialog.test.tsx's own capture
// covers the one call site that reaches it unconditionally. AttachmentDropZone
// wraps the whole body regardless of preview or edit, so it is what this file
// can check the wiring against.
vi.mock('./MarkdownEditor', () => ({ MarkdownEditor: () => null }));

const seen = vi.hoisted(() => [] as { projectId?: number | null }[]);
vi.mock('./AttachmentDropZone', () => ({
  AttachmentDropZone: (props: { projectId?: number | null }) => {
    seen.push(props);
    return null;
  },
}));

const onSave = async () => true;

describe('DescriptionEditor', () => {
  beforeEach(() => {
    seen.length = 0;
  });

  it('passes a given projectId to the drop zone', () => {
    renderToStaticMarkup(<DescriptionEditor title={null} value="" onSave={onSave} projectId={7} />);

    expect(seen).toHaveLength(1);
    expect(seen[0].projectId).toBe(7);
  });

  it('leaves the drop zone projectId undefined when none is given', () => {
    renderToStaticMarkup(<DescriptionEditor title={null} value="" onSave={onSave} />);

    expect(seen[0].projectId).toBeUndefined();
  });
});
