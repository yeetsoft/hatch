import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { refusedModelsDraft, refusedModelsRefusal, refusedModelsRequest } from './runnerModels';

const SERVER_REFUSAL =
  'a model is one of haiku, sonnet, opus, fable, or a pinned id of a supported agent ' +
  '(claude-opus-5 for claude; gpt-6-luna for codex; grok-4.7 for grok; qwen3-coder:30b for ollama) - not "';

describe('refusedModelsDraft and refusedModelsRequest', () => {
  it('draws one model per line and reads them back', () => {
    expect(refusedModelsDraft(['fable', 'claude-opus-5'])).toBe('fable\nclaude-opus-5');
    expect(refusedModelsDraft([])).toBe('');
    expect(refusedModelsRequest(refusedModelsDraft(['fable', 'claude-opus-5']))).toEqual(['fable', 'claude-opus-5']);
  });

  it('trims each line and drops the blank ones', () => {
    expect(refusedModelsRequest(' fable \n\n  \nhaiku\n')).toEqual(['fable', 'haiku']);
    expect(refusedModelsRequest('')).toEqual([]);
  });
});

describe('refusedModelsRefusal', () => {
  it('passes an empty list, the aliases in any case, and a pinned id of each family', () => {
    expect(refusedModelsRefusal([], null)).toBeNull();
    expect(refusedModelsRefusal(['haiku', 'Sonnet', 'OPUS', 'fAbLe'], 'claude')).toBeNull();
    expect(refusedModelsRefusal(['claude-opus-5', 'gpt-6-luna', 'grok-4.7', 'qwen3-coder:30b'], null)).toBeNull();
  });

  it('leaves dropping a repeat to the server', () => {
    expect(refusedModelsRefusal(['fable', 'fable'], 'claude')).toBeNull();
  });

  it('refuses more than a hundred, before looking at any entry', () => {
    const many = Array.from({ length: 101 }, (_, i) => `not a model ${i}`);
    expect(refusedModelsRefusal(many, null)).toBe('a runner advertises at most 100 models');
    expect(refusedModelsRefusal(Array.from({ length: 100 }, () => 'fable'), null)).toBeNull();
  });

  it('refuses an entry that is neither an alias nor a pinned id, naming it', () => {
    expect(refusedModelsRefusal(['fable', 'gizmo'], 'claude')).toBe(`${SERVER_REFUSAL}gizmo"`);
    expect(refusedModelsRefusal(['claude-'], null)).toBe(`${SERVER_REFUSAL}claude-"`);
    expect(refusedModelsRefusal(['Claude-Opus-5'], null)).toBe(`${SERVER_REFUSAL}Claude-Opus-5"`);
  });

  it('refuses an ollama id outside its length bounds', () => {
    expect(refusedModelsRefusal(['a:b'], null)).toBeNull();
    expect(refusedModelsRefusal([`a:${'b'.repeat(58)}`], null)).toBeNull();
    expect(refusedModelsRefusal([`a:${'b'.repeat(59)}`], null)).not.toBeNull();
  });

  it("refuses another agent's pinned id when the agent is known, with an or an a", () => {
    expect(refusedModelsRefusal(['gpt-6-luna'], 'claude')).toBe(
      '"gpt-6-luna" is a codex model - this runner\'s agent is claude',
    );
    expect(refusedModelsRefusal(['qwen3-coder:30b'], 'grok')).toBe(
      '"qwen3-coder:30b" is an ollama model - this runner\'s agent is grok',
    );
  });

  it('skips only that check for a missing, blank or unknown agent', () => {
    for (const agent of [null, '', '  ', 'gemini']) {
      expect(refusedModelsRefusal(['gpt-6-luna'], agent)).toBeNull();
      expect(refusedModelsRefusal(['gizmo'], agent)).toBe(`${SERVER_REFUSAL}gizmo"`);
    }
  });

  it('judges an alias by its lowercase, so it is never another agent\'s', () => {
    expect(refusedModelsRefusal(['Fable'], 'codex')).toBeNull();
  });
});

describe('the port against ModelFamilies.cs', () => {
  // The web holds a copy of the server's patterns so a bad entry is read in
  // the card; this is what notices the copy and the original drifting apart.
  const cs = readFileSync(new URL('../../../../../Hatch.Contracts/ModelFamilies.cs', import.meta.url), 'utf8')
    .replace(/\\z/g, '$');

  it('carries every pattern and example the server does', () => {
    for (const pattern of [
      '^claude-[a-z0-9][a-z0-9.-]{0,48}$',
      '^gpt-[a-z0-9][a-z0-9.-]{0,48}$',
      '^grok-[a-z0-9][a-z0-9.-]{0,48}$',
      '^(?=.{3,60}$)[A-Za-z0-9][A-Za-z0-9._/-]*:[A-Za-z0-9][A-Za-z0-9._-]*$',
    ]) {
      expect(cs).toContain(`"${pattern}"`);
    }
    for (const example of ['claude-opus-5', 'gpt-6-luna', 'grok-4.7', 'qwen3-coder:30b']) {
      expect(cs).toContain(`"${example}"`);
    }
    expect(cs).toContain('["haiku", "sonnet", "opus", "fable"]');
  });

  it('has no family the port lacks', () => {
    expect(cs.match(/new ModelFamily\(/g)).toHaveLength(4);
  });
});
