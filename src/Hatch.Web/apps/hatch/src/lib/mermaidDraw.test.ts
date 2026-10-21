/// <reference types="node" />
/* The runner has no DOM (DocsPage.mermaid.test.tsx renders to static markup
   for the same reason), so the containers, their source `<pre>` and the two
   elements `mermaidDraw` creates are small hand-written fakes that answer only
   what it asks of them. `./mermaid` is mocked; the real render needs a
   browser. */
import { readFileSync } from 'node:fs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('dompurify', () => ({ default: { sanitize: (markup: string) => markup } }));

interface FakeNode {
  tag: string;
  className: string;
  textContent: string;
  hidden: boolean;
  parent: FakeContainer | null;
  remove(): void;
  replaceWith(next: FakeNode): void;
}

interface FakeContainer {
  children: FakeNode[];
  querySelector(selector: string): FakeNode | null;
  insertBefore(node: FakeNode, before: FakeNode): void;
  appendChild(node: FakeNode): void;
}

function fakeNode(tag: string, className = '', textContent = ''): FakeNode {
  const node: FakeNode = {
    tag,
    className,
    textContent,
    hidden: false,
    parent: null,
    remove() {
      if (node.parent) node.parent.children = node.parent.children.filter((c) => c !== node);
      node.parent = null;
    },
    replaceWith(next) {
      const parent = node.parent;
      if (!parent) return;
      parent.children = parent.children.map((c) => (c === node ? next : c));
      next.parent = parent;
      node.parent = null;
    },
  };
  return node;
}

function fakeContainer(sourceText: string): { container: FakeContainer; source: FakeNode } {
  const source = fakeNode('pre', 'hatch-docs-mermaid-source', sourceText);
  const container: FakeContainer = {
    children: [source],
    querySelector(selector) {
      const match = (n: FakeNode) =>
        selector.startsWith('.') ? n.className === selector.slice(1) : n.tag === selector;
      return container.children.find(match) ?? null;
    },
    insertBefore(node, before) {
      node.parent = container;
      container.children.splice(container.children.indexOf(before), 0, node);
    },
    appendChild(node) {
      node.parent = container;
      container.children.push(node);
    },
  };
  source.parent = container;
  return { container, source };
}

const svgsIn = (c: FakeContainer) => c.children.filter((n) => n.tag === 'svg');
const errorsIn = (c: FakeContainer) => c.children.filter((n) => n.className === 'hatch-docs-mermaid-error');
// A mocked dynamic import settles over more than one macrotask on a loaded
// machine, so a pass is given a moment rather than a single turn.
const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 25));

const ONE_LINE = 'This diagram could not be drawn.';

function stubDocument() {
  vi.stubGlobal('document', {
    createElement(tag: string) {
      if (tag !== 'div') return fakeNode(tag);
      // Setting markup yields the one element a parse of it would.
      const wrapper = { firstElementChild: null as FakeNode | null, innerHTML: '' };
      return Object.defineProperty(wrapper, 'innerHTML', {
        set(markup: string) {
          wrapper.firstElementChild = fakeNode('svg', '', markup);
        },
      });
    },
  });
}

/** A fresh copy of the module, so its chunk cache starts empty, beside the
    mocked `./mermaid` that copy will reach. */
async function load() {
  const applyMermaidTheme = vi.fn();
  const render = vi.fn<(source: string) => Promise<string>>();
  vi.resetModules();
  vi.doMock('./mermaid', () => ({ applyMermaidTheme, renderMermaidDiagram: render }));
  const { drawMermaid } = await import('./mermaidDraw');
  return { drawMermaid, applyMermaidTheme, render };
}

const rootOf = (...containers: FakeContainer[]) =>
  ({ querySelectorAll: () => containers }) as unknown as ParentNode;

beforeEach(stubDocument);

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('drawMermaid', () => {
  it('hides the source and shows the SVG for a diagram that drew', async () => {
    const { drawMermaid, applyMermaidTheme, render } = await load();
    render.mockResolvedValue('<svg>drawn</svg>');
    const { container, source } = fakeContainer('graph TD\n  A --> B');

    drawMermaid(rootOf(container));
    await flush();

    expect(applyMermaidTheme).toHaveBeenCalledTimes(1);
    expect(render).toHaveBeenCalledWith('graph TD\n  A --> B');
    expect(source.hidden).toBe(true);
    expect(svgsIn(container).map((n) => n.textContent)).toEqual(['<svg>drawn</svg>']);
    expect(errorsIn(container)).toHaveLength(0);
  });

  it('replaces the previous SVG on a second pass rather than stacking one', async () => {
    const { drawMermaid, render } = await load();
    render.mockResolvedValueOnce('<svg>one</svg>').mockResolvedValueOnce('<svg>two</svg>');
    const { container } = fakeContainer('graph TD');

    drawMermaid(rootOf(container));
    await flush();
    drawMermaid(rootOf(container));
    await flush();

    expect(svgsIn(container).map((n) => n.textContent)).toEqual(['<svg>two</svg>']);
  });

  it('shows the source under one line for a diagram mermaid refused, and tells onError', async () => {
    const { drawMermaid, render } = await load();
    const refusal = new Error('parse error');
    render.mockRejectedValue(refusal);
    const { container, source } = fakeContainer('not a diagram');
    const onError = vi.fn();

    drawMermaid(rootOf(container), onError);
    await flush();
    drawMermaid(rootOf(container), onError);
    await flush();

    expect(source.hidden).toBe(false);
    expect(svgsIn(container)).toHaveLength(0);
    expect(errorsIn(container).map((n) => n.textContent)).toEqual([ONE_LINE]);
    expect(onError).toHaveBeenCalledWith('A mermaid diagram failed to draw', refusal);
  });

  it('draws one container even when its neighbour is refused', async () => {
    const { drawMermaid, render } = await load();
    render.mockResolvedValueOnce('<svg>good</svg>').mockRejectedValueOnce(new Error('bad'));
    const good = fakeContainer('graph TD');
    const bad = fakeContainer('nonsense');

    drawMermaid(rootOf(good.container, bad.container));
    await flush();

    expect(good.source.hidden).toBe(true);
    expect(svgsIn(good.container)).toHaveLength(1);
    expect(bad.source.hidden).toBe(false);
    expect(errorsIn(bad.container).map((n) => n.textContent)).toEqual([ONE_LINE]);
  });

  it('shows every container its source under the one line when the chunk fails to load, and retries after', async () => {
    vi.resetModules();
    const failure = new Error('chunk 404');
    vi.doMock('./mermaid', () => {
      throw failure;
    });
    const { drawMermaid } = await import('./mermaidDraw');
    const a = fakeContainer('graph TD');
    const b = fakeContainer('graph LR');
    const onError = vi.fn();

    drawMermaid(rootOf(a.container, b.container), onError);
    await vi.waitFor(() => expect(onError).toHaveBeenCalledTimes(1));
    await flush();

    for (const { container, source } of [a, b]) {
      expect(source.hidden).toBe(false);
      expect(errorsIn(container).map((n) => n.textContent)).toEqual([ONE_LINE]);
    }
    expect(onError).toHaveBeenCalledTimes(1);
    expect(onError.mock.calls[0]?.[0]).toBe('The mermaid chunk failed to load');

    // The rejection cleared the cache, so the next pass asks for the chunk again.
    const render = vi.fn().mockResolvedValue('<svg>back</svg>');
    vi.doMock('./mermaid', () => ({ applyMermaidTheme: vi.fn(), renderMermaidDiagram: render }));
    drawMermaid(rootOf(a.container, b.container), onError);
    await vi.waitFor(() => expect(render).toHaveBeenCalledTimes(2));
    await flush();

    for (const { container, source } of [a, b]) {
      expect(source.hidden).toBe(true);
      expect(svgsIn(container)).toHaveLength(1);
      expect(errorsIn(container)).toHaveLength(0);
    }
  });

  it('writes nothing when cancelled before the draw resolves', async () => {
    const { drawMermaid, render } = await load();
    let resolveSvg: (svg: string) => void = () => {};
    render.mockReturnValue(new Promise<string>((resolve) => (resolveSvg = resolve)));
    const { container, source } = fakeContainer('graph TD');

    const cancel = drawMermaid(rootOf(container));
    await flush();
    expect(render).toHaveBeenCalledTimes(1);
    cancel();
    resolveSvg('<svg>late</svg>');
    await flush();

    expect(source.hidden).toBe(false);
    expect(svgsIn(container)).toHaveLength(0);
    expect(errorsIn(container)).toHaveLength(0);
  });

  it('draws nothing when cancelled before the chunk arrives', async () => {
    const { drawMermaid, applyMermaidTheme, render } = await load();
    const { container, source } = fakeContainer('graph TD');

    drawMermaid(rootOf(container))();
    await flush();

    expect(applyMermaidTheme).not.toHaveBeenCalled();
    expect(render).not.toHaveBeenCalled();
    expect(source.hidden).toBe(false);
    expect(container.children).toEqual([source]);
  });

  it('asks for nothing under a root with no diagram', async () => {
    const { drawMermaid, applyMermaidTheme } = await load();

    drawMermaid(rootOf())();
    await flush();

    expect(applyMermaidTheme).not.toHaveBeenCalled();
  });
});

describe('mermaidDraw.ts imports', () => {
  const text = readFileSync(new URL('./mermaidDraw.ts', import.meta.url), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/\/\/.*$/gm, '');

  it('names only dompurify and ./mermaid, so a page with no React can import it', () => {
    const specifiers = [
      ...[...text.matchAll(/\bfrom\s+'([^']+)'/g)].map((m) => m[1]),
      ...[...text.matchAll(/\bimport\(\s*'([^']+)'\s*\)/g)].map((m) => m[1]),
      ...[...text.matchAll(/^\s*import\s+'([^']+)'/gm)].map((m) => m[1]),
    ];
    expect([...new Set(specifiers)].sort()).toEqual(['./mermaid', 'dompurify']);
  });

  it('reaches ./mermaid only by type or by dynamic import', () => {
    const statics = [...text.matchAll(/\bimport\b[^;]*?\bfrom\s+'\.\/mermaid'/g)].map((m) => m[0]);
    expect(statics.length).toBeGreaterThan(0);
    for (const statement of statics) expect(statement).toMatch(/^import\s+type\b/);
  });
});
