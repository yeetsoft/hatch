---
title: The design gallery
section: Concepts
order: 95
summary: What Design is for, how a component joins it, and the one rule left for it to keep.
---

**Design**, under Manage, is every token and component in `@hatch/ui`
rendered in both themes and in the states an app never shows you - color,
type, spacing, radius, elevation and motion, the categorical series ramp, and
every component the board is built out of.

It is a workbench, not a product surface. Its purpose is that a component
built after it exists is developed *there* - against its own disabled,
loading, error, empty and long-content states - rather than inside a page of
the board where only the happy path is visible.

## Adding a page

One entry in `apps/hatch/src/design/sections.ts`:

```ts
{ group: 'Components', slug: 'toolbar', title: 'Toolbar', Page: ToolbarPage }
```

That puts it on the rail, on a route and behind a deep link at
`/design/toolbar`. Nothing else enumerates the sections - the rail's groups
are derived from the entries in first-appearance order, and `/design` and any
unknown slug redirect to the first one.

A page is built from the helpers in
`apps/hatch/src/design/components/Gallery.tsx`: `<GalleryPage>` (title, one
line saying what the group is for, the specimens), `<GallerySection>` (a
named run, whose `note` carries the rule the run exists to state), and
`<TokenName>`/`<TokenValue>`. A specimen shown at phone width uses
`<PhoneFrame>`, which loads the same page inside an iframe narrower than the
window behind it - the only way to show a real `@media` breakpoint firing
rather than a narrowed box pretending to.

## The one rule left

The gallery holds no component CSS of its own. It shows a component by
rendering it, never by restating its styles - a gallery whose copy of a
component's CSS can drift from the component is a gallery that lies. The only
rules in the "Design gallery" block of `apps/hatch/src/App.css` are the
gallery's own chrome and the frames its specimens sit in, every one of them
carrying a `design-` prefix so a gallery rule can never reach a board page by
accident.

Two things that used to be true of the gallery are not any more, now that it
is a page of the board rather than an app of its own: a render error goes
through the board's own error boundary, the same as any other page's, rather
than to a boundary of its own; and the gallery wears the board's own bar
above it, rather than one of its own.
