/* Small regex helpers over built HTML text, shared by the tests that hold
   pages to their markup. There is no DOM in this runner. */
/// <reference types="node" />

export const withoutComments = (html: string): string => html.replace(/<!--[\s\S]*?-->/g, '')

export const tagsOf = (html: string, name: string): string[] => html.match(new RegExp(String.raw`<${name}\b[^>]*>`, 'g')) ?? []

export const attr = (tag: string, name: string): string | undefined =>
  tag.match(new RegExp(String.raw`\s${name}="([^"]*)"`))?.[1]

export const attrValues = (html: string, name: string): string[] =>
  [...html.matchAll(new RegExp(String.raw`\s${name}="([^"]*)"`, 'g'))].map((match) => match[1])

export const ids = (html: string): string[] => attrValues(html, 'id')

export const hrefs = (html: string): string[] => attrValues(html, 'href')
