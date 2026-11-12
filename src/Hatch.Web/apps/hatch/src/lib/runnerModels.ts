/** What the refused models field shows: one model per line, blank for none. */
export function refusedModelsDraft(models: string[]): string {
  return models.join('\n');
}

/** The field, split into models for the wire: one per line, trimmed, blanks dropped. */
export function refusedModelsRequest(draft: string): string[] {
  return draft
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l !== '');
}

/** At most this many, the same cap `RunnerModels.MaxCount` holds on the server. */
const MAX_COUNT = 100;

/** The aliases the CLI resolves to whatever is current: `ModelFamilies.Aliases`. */
const ALIASES = ['haiku', 'sonnet', 'opus', 'fable'];

/** One agent's pinned-id shape and the example a refusal shows: a port of
    `ModelFamilies.Default`, in its order. The ollama pattern's `\z` is `$`
    here, which without the `m` flag is the same end-of-input. */
const FAMILIES = [
  { agent: 'claude', pattern: /^claude-[a-z0-9][a-z0-9.-]{0,48}$/, example: 'claude-opus-5' },
  { agent: 'codex', pattern: /^gpt-[a-z0-9][a-z0-9.-]{0,48}$/, example: 'gpt-6-luna' },
  { agent: 'grok', pattern: /^grok-[a-z0-9][a-z0-9.-]{0,48}$/, example: 'grok-4.7' },
  {
    agent: 'ollama',
    pattern: /^(?=.{3,60}$)[A-Za-z0-9][A-Za-z0-9._/-]*:[A-Za-z0-9][A-Za-z0-9._-]*$/,
    example: 'qwen3-coder:30b',
  },
];

const article = (word: string): string => ('aeiou'.includes(word[0]) ? 'an' : 'a');

/**
 * The sentence refusing a refused-models list, or null where it would pass - a
 * port of `RunnerModels.Normalise`, so a bad entry is read in the card before
 * either request is sent. The server stays the authority: it checks the
 * agent it knows, the beat's or else a live session's, and this checks
 * `runner.agent`, so a missing agent skips only that check, as it does there.
 * Blank lines never reach here (`refusedModelsRequest` drops them), and
 * dropping a repeat is the server's.
 */
export function refusedModelsRefusal(models: string[], agent: string | null): string | null {
  if (models.length > MAX_COUNT) return `a runner advertises at most ${MAX_COUNT} models`;

  const agentKnown = !!agent?.trim() && FAMILIES.some((f) => f.agent === agent);
  for (const raw of models) {
    const id = raw.trim();
    if (ALIASES.includes(id.toLowerCase())) continue;

    const owner = FAMILIES.find((f) => f.pattern.test(id))?.agent;
    if (owner === undefined) {
      const examples = FAMILIES.map((f) => `${f.example} for ${f.agent}`).join('; ');
      return `a model is one of ${ALIASES.join(', ')}, or a pinned id of a supported agent (${examples}) - not "${id}"`;
    }

    if (agentKnown && owner !== agent) {
      return `"${id}" is ${article(owner)} ${owner} model - this runner's agent is ${agent}`;
    }
  }

  return null;
}
