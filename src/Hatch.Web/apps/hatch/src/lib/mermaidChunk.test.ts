/// <reference types="node" />
/* This is HA-1057's guard: it builds the real app once with Vite's JS API and
   reads the chunk graph the build actually produced, because the bug it
   holds is a property of Rolldown's code-splitting, not of vite.config.ts's
   text - the same reason `editorChunk.test.ts` builds for real rather than
   reading the config.

   mermaid's own facade chunk is named after its own module by default, same
   as Monaco's - no `codeSplitting` group needed for mermaid itself. The risk
   here is narrower: the entry already imports `dompurify` eagerly (via
   lib/markdown.ts), and mermaid depends on a compatible version that npm
   hoists to the same copy. Left alone, Rolldown would inline that shared
   copy into the entry chunk and give every mermaid-side chunk that needs it
   an import edge back to the entry's own hashed name - so a deploy that
   touches only the entry (nearly every deploy) renames the mermaid chunk
   too, though mermaid itself did not change. The `dompurify` codeSplitting
   group in vite.config.ts breaks that; this is what keeps it from
   regressing silently. This is deliberately not a copy of
   `editorChunk.test.ts`'s `expect(chunk.imports).toHaveLength(1)`: mermaid's
   own facade statically imports a number of mermaid's own internal shared
   chunks, auto-named with no semantic meaning, and hardcoding that full list
   would fail on every routine mermaid version bump for no reason this
   ticket cares about. What this ticket cares about is narrower and asserted
   directly below: the mermaid chunk must never import the entry chunk.

   Nothing calls `import('../lib/mermaid')` yet - that is the next task's job
   ("Not in this task" in HA-1057's brief) - so there is no real callsite for
   this build to discover on its own; an unreached module is simply absent
   from Rolldown's output. `probePlugin` below stands in for that future
   callsite with a virtual module built as a second input alongside the real
   index.html, reached only by its own dynamic `import()` of mermaid.ts, so
   the chunk graph this test inspects is shaped exactly as it will be once
   that callsite is real.

   `write: false` and `emptyOutDir: false` matter: the config's `outDir` is
   Hatch.Api's own wwwroot, and a test must never write to it, let alone
   empty it. A build here takes a few seconds, so the result is built once
   and shared. */
import { fileURLToPath } from 'node:url';
import { build, type Plugin } from 'vite';
import { describe, expect, it } from 'vitest';

/** The fields this test reads off a built chunk. Kept local, rather than
    imported from rolldown's own (unlisted, transitive) types, so this test
    depends on nothing this app does not already declare. */
interface Chunk {
  type: string;
  fileName: string;
  moduleIds: string[];
  imports: string[];
}

const PROBE_ID = 'virtual:mermaid-chunk-probe';
const RESOLVED_PROBE_ID = `\0${PROBE_ID}`;
const mermaidPath = fileURLToPath(new URL('./mermaid.ts', import.meta.url));
const indexHtmlPath = fileURLToPath(new URL('../../index.html', import.meta.url));

function probePlugin(): Plugin {
  return {
    name: 'mermaid-chunk-probe',
    resolveId(id) {
      if (id === PROBE_ID) return RESOLVED_PROBE_ID;
    },
    load(id) {
      // A bare top-level `import()`, not wrapped in a function: wrapped and
      // left uncalled, Rolldown finds the whole probe module's export
      // unused and tree-shakes the dynamic import away before it ever
      // reaches the chunk graph this test inspects.
      if (id === RESOLVED_PROBE_ID) return `import(${JSON.stringify(mermaidPath)});`;
    },
  };
}

let cached: Promise<{ chunk: Chunk; entry: Chunk }> | null = null;

function built(): Promise<{ chunk: Chunk; entry: Chunk }> {
  cached ??= build({
    configFile: fileURLToPath(new URL('../../vite.config.ts', import.meta.url)),
    logLevel: 'silent',
    plugins: [probePlugin()],
    build: {
      write: false,
      emptyOutDir: false,
      rolldownOptions: { input: { main: indexHtmlPath, probe: PROBE_ID } },
    },
  }).then((result) => {
    // No watcher here (`build: { watch: undefined }` is the default), so
    // `build` resolves to a single output rather than an array of them.
    const { output } = result as { output: Chunk[] };
    const chunk = output.find(
      (item) => item.type === 'chunk' && item.moduleIds.some((id) => id.endsWith('src/lib/mermaid.ts')),
    );
    if (!chunk) throw new Error('no output chunk has src/lib/mermaid.ts among its modules');
    const entry = output.find(
      (item) => item.type === 'chunk' && item.moduleIds.some((id) => id.endsWith('src/main.tsx')),
    );
    if (!entry) throw new Error('no output chunk has src/main.tsx among its modules');
    return { chunk, entry };
  });
  return cached;
}

describe('the mermaid chunk', () => {
  it('finds the chunk it reads', async () => {
    const { chunk } = await built();
    expect(chunk.fileName).toMatch(/^assets\/mermaid-/);
  });

  it('is never pulled into the entry chunk', async () => {
    const { entry } = await built();
    expect(entry.moduleIds.some((id) => id.includes('node_modules/mermaid'))).toBe(false);
  });

  it('does not import the entry chunk, so a deploy that never touched it cannot rename it', async () => {
    const { chunk, entry } = await built();
    expect(chunk.imports).not.toContain(entry.fileName);
  });

  it('does not carry its own copy of dompurify into the entry chunk', async () => {
    const { entry } = await built();
    expect(entry.moduleIds.some((id) => id.includes('node_modules/dompurify'))).toBe(false);
  });
});
