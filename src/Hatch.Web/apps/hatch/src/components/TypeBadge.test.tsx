import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { TypeBadge } from './TypeBadge';
import { ISSUE_TYPES } from '../types';

describe('TypeBadge', () => {
  it.each(ISSUE_TYPES)('wears the shared face for %s', (type) => {
    const html = renderToStaticMarkup(<TypeBadge type={type} />);
    const classes = /class="([^"]*)"/.exec(html)![1].split(' ');
    expect(classes).toContain('hatch-type-badge');
    expect(classes).toContain(type);
    expect(html).toContain(`>${type}</span>`);
  });
});
