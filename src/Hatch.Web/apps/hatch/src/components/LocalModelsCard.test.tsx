import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { LocalModelForm, LocalModelsTable } from './LocalModelsCard';
import { EMPTY_LOCAL_MODEL_DRAFT } from '../lib/localModels';
import type { LocalModelRow } from '../types';

const model = (over: Partial<LocalModelRow>): LocalModelRow => ({
  id: 1,
  ollamaId: 'fake:1b',
  contextLengthTokens: 8192,
  memoryFootprintBytes: 2 * 1024 ** 3,
  createdBy: 'a',
  createdAt: '2026-01-01T00:00:00Z',
  ...over,
});

describe('LocalModelsTable', () => {
  it('draws nothing while the read is out', () => {
    expect(renderToStaticMarkup(<LocalModelsTable models={null} onDelete={() => {}} />)).toBe('');
  });

  it('says so for an empty table', () => {
    expect(renderToStaticMarkup(<LocalModelsTable models={[]} onDelete={() => {}} />)).toContain('No local models yet');
  });

  it('draws the rows in the order given, with id, context and memory, and a Delete each', () => {
    const second = model({
      id: 2,
      ollamaId: 'fake:7b',
      contextLengthTokens: 32768,
      memoryFootprintBytes: 4.5 * 1024 ** 3,
    });
    const html = renderToStaticMarkup(<LocalModelsTable models={[model({ id: 1 }), second]} onDelete={() => {}} />);
    expect(html.indexOf('fake:1b')).toBeLessThan(html.indexOf('fake:7b'));
    expect(html).toContain('8,192 tokens');
    expect(html).toContain('2GB');
    expect(html).toContain('32,768 tokens');
    expect(html).toContain('4.5GB');
    expect(html.match(/Delete/g)).toHaveLength(2);
  });
});

describe('LocalModelForm', () => {
  const draw = (over: Partial<Parameters<typeof LocalModelForm>[0]> = {}) =>
    renderToStaticMarkup(
      <LocalModelForm
        draft={EMPTY_LOCAL_MODEL_DRAFT}
        error={null}
        busy={false}
        onChange={() => {}}
        onAdd={() => {}}
        {...over}
      />,
    );

  it('draws the three fields, with Add disabled until the draft could be sent', () => {
    const html = draw();
    expect(html).toContain('Ollama id');
    expect(html).toContain('Context length');
    expect(html).toContain('Memory footprint');
    expect(html).toMatch(/<button[^>]*disabled[^>]*>Add model/);
  });

  it('enables Add for a draft that could be sent, and keeps what was typed', () => {
    const html = draw({ draft: { ollamaId: 'fake:1b', contextLength: '8192', footprintGigabytes: '2' } });
    expect(html).not.toMatch(/<button[^>]*disabled[^>]*>Add model/);
    expect(html).toContain('value="fake:1b"');
  });

  it("shows the server's sentence beside the form", () => {
    const html = draw({ error: 'there is already a local model "fake:1b"' });
    expect(html).toContain('text-danger');
    expect(html).toContain('there is already a local model');
  });
});
