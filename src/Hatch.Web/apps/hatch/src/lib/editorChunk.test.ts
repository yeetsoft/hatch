/// <reference types="node" />
/* This is HA-105's guard: it builds the real app once with Vite's JS API and
   reads the chunk graph the build actually produced, because the bug it holds
   is a property of Rolldown's code-splitting, not of vite.config.ts's text.
   Left unguarded, the Monaco chunk's only import is Vite's preload helper
   reached through the entry chunk's hashed name - so a deploy that touches
   only the entry (nearly every deploy) renames a chunk that never itself
   changed, and a tab left open across it asks for a file that is gone. The
   `codeSplitting` group in vite.config.ts gives the helper a chunk of its own;
   this is what keeps that from regressing silently.

   `write: false` and `emptyOutDir: false` matter: the config's `outDir` is
   Hatch.Api's own wwwroot, and a test must never write to it, let alone empty
   it. A build here takes about 1.5s, so the result is built once and shared. */
import { fileURLToPath } from 'node:url';
import { build } from 'vite';
import { describe, expect, it } from 'vitest';

/** The two fields this test reads off a built chunk. Kept local, rather than
    imported from rolldown's own (unlisted, transitive) types, so this test
    depends on nothing this app does not already declare. */
interface Chunk {
  type: string;
  fileName: string;
  moduleIds: string[];
  imports: string[];
}

let cached: Promise<{ chunk: Chunk; entry: Chunk }> | null = null;

function monacoChunk(): Promise<{ chunk: Chunk; entry: Chunk }> {
  cached ??= build({
    configFile: fileURLToPath(new URL('../../vite.config.ts', import.meta.url)),
    logLevel: 'silent',
    build: { write: false, emptyOutDir: false },
  }).then((result) => {
    // No multi-output config here, so `build` resolves to a single output
    // rather than an array of them or a watcher.
    const { output } = result as { output: Chunk[] };
    const chunk = output.find(
      (item) => item.type === 'chunk' && item.moduleIds.some((id) => id.endsWith('src/lib/monaco.ts')),
    );
    if (!chunk) throw new Error('no output chunk has src/lib/monaco.ts among its modules');
    const entry = output.find(
      (item) => item.type === 'chunk' && item.moduleIds.some((id) => id.endsWith('src/main.tsx')),
    );
    if (!entry) throw new Error('no output chunk has src/main.tsx among its modules');
    return { chunk, entry };
  });
  return cached;
}

describe('the Monaco chunk', () => {
  it('finds the chunk it reads', async () => {
    const { chunk } = await monacoChunk();
    expect(chunk.fileName).toMatch(/^assets\/monaco-/);
  });

  // Not `toHaveLength(1)` any more (HA-1058): once something dynamically
  // imports lib/mermaid.ts for real, cssColor.ts - shared by editorTheme.ts
  // and mermaidTheme.ts - becomes reachable from two separate lazy chunks and
  // earns a hashed chunk of its own, which Monaco's chunk now also imports.
  // That chunk's name tracks only cssColor.ts's own content, same as the
  // preload helper's does - what this test actually guards is that neither
  // import traces back to the entry's own hashed name.
  it("imports the preload helper's own chunk, and never the entry's", async () => {
    const { chunk, entry } = await monacoChunk();
    expect(chunk.imports.some((f) => /^assets\/vite-preload-/.test(f))).toBe(true);
    expect(chunk.imports).not.toContain(entry.fileName);
  });
});
