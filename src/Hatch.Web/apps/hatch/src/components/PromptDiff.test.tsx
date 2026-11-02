import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PromptDiff } from './PromptDiff';

// lib/clientLogger reads `window` at import, and there is no jsdom here - the
// same stub PlaybookEditDialog.test.tsx uses.
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

describe('PromptDiff', () => {
  // Effects never run under renderToStaticMarkup, so what renders is the plain
  // pair of boxes every mount starts as, before the editor chunk lands.
  const html = renderToStaticMarkup(<PromptDiff defaultPrompt="Prescribed words." prompt="Operator words." />);

  it('labels both read-only boxes', () => {
    expect(html).toContain('aria-label="Prescribed text"');
    expect(html).toContain('aria-label="The operator&#x27;s own wording"');
    expect(html.match(/readOnly=""|readonly=""/gi)).toHaveLength(2);
  });

  it('puts each side in its own box', () => {
    expect(html).toContain('Prescribed words.');
    expect(html).toContain('Operator words.');
  });

  it('shows no failure notice until the chunk fails', () => {
    expect(html).not.toContain('The editor didn');
  });
});
