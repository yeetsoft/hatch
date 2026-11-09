/// <reference types="node" />
import { readdirSync, readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import ts from 'typescript';

/**
 * A native `title` shows over the border box of the element that carries it,
 * and the innermost titled element wins. A title on a span inside a button
 * therefore leaves the button's own padding, caret and gaps with no hint. This
 * walks every component and names the `file:line` of any titled element that
 * sits inside a control but is not a control itself.
 */

const controls = new Set(['button', 'a', 'Link', 'NavLink', 'Button']);
/** Elements whose own title is the hint for their own box, wherever they sit. */
const ownHint = new Set([...controls, 'option', 'label', 'select']);
/** A truncation reveal for one ellipsised line, not a hint for the link around it. */
const allowed = ['hatch-attention-files'];

const root = new URL('./../', import.meta.url);

function sources(dir: URL): URL[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const next = new URL(entry.name + (entry.isDirectory() ? '/' : ''), dir);
    if (entry.isDirectory()) return sources(next);
    return entry.name.endsWith('.tsx') && !/\.test\.tsx$/.test(entry.name) ? [next] : [];
  });
}

function attributes(node: ts.JsxOpeningLikeElement) {
  return node.attributes.properties.filter(ts.isJsxAttribute);
}

function violations(file: URL): string[] {
  const text = readFileSync(file, 'utf8');
  const source = ts.createSourceFile(file.pathname, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const found: string[] = [];

  const visit = (node: ts.Node, insideControl: boolean) => {
    let inside = insideControl;
    if (ts.isJsxElement(node) || ts.isJsxSelfClosingElement(node)) {
      const opening = ts.isJsxElement(node) ? node.openingElement : node;
      const name = opening.tagName.getText(source);
      const attrs = attributes(opening);
      const titled = attrs.some((a) => a.name.getText(source) === 'title');
      const className = attrs.find((a) => a.name.getText(source) === 'className')?.initializer?.getText(source) ?? '';
      if (titled && insideControl && !ownHint.has(name) && !allowed.some((c) => className.includes(c))) {
        const { line } = source.getLineAndCharacterOfPosition(opening.getStart(source));
        found.push(`${file.pathname.slice(root.pathname.length)}:${line + 1} <${name}> carries a title inside a control`);
      }
      if (controls.has(name)) inside = true;
    }
    ts.forEachChild(node, (child) => visit(child, inside));
  };
  visit(source, false);
  return found;
}

describe('hints on controls', () => {
  it('puts every title on the interactive element, not on something inside it', () => {
    const files = sources(root);
    expect(files.length).toBeGreaterThan(20);
    expect(files.flatMap(violations)).toEqual([]);
  });
});
