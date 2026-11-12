import type { ReactNode } from 'react';
import { PageHeader } from '@hatch/ui';
import { appHref } from '../../lib/basename';

/** A gallery page: its title, one line saying what the group is for, and the
    specimens. Nothing else - the page is a frame around the thing being shown. */
export function GalleryPage({ title, blurb, children }: { title: string; blurb: string; children: ReactNode }) {
  return (
    <article className="design-gallery-page">
      <PageHeader title={title} description={blurb} />
      {children}
    </article>
  );
}

/** A named run of specimens inside a page. `note` carries the rule that the
    run exists to state - "a radius written as a number is a bug" - because the
    rule is the part a designer needs and the swatch is only the evidence. */
export function GallerySection({ title, note, children }: { title: string; note?: string; children: ReactNode }) {
  return (
    <section className="design-gallery-section">
      <h2>{title}</h2>
      {note ? <p className="design-gallery-note">{note}</p> : null}
      {children}
    </section>
  );
}

/** A token's name, set in the mono face so `--sp-1-5` and `--sp-15` are not the
    same shape at a glance. */
export function TokenName({ name }: { name: string }) {
  return <code className="design-token-name">{name}</code>;
}

/** The value the browser resolved. Renders nothing until it has one rather than
    holding a dash: an em dash next to a token reads as "this token is unset". */
export function TokenValue({ value }: { value: string | undefined }) {
  if (!value) return null;
  return <span className="design-token-value">{value}</span>;
}

/** A genuine phone-width viewport for a specimen, not a narrowed box - an
    @media rule reads the browsing context's own width, and only a frame has
    one narrower than the window behind it. Renders nothing while itself
    framed: without that guard a phone specimen loads a page that holds the
    same frame, nested without end. */
export function PhoneFrame({ slug, title }: { slug: string; title: string }) {
  if (window.self !== window.top) return null;
  return <iframe className="design-gallery-phone-frame" src={appHref(`/design/${slug}`)} title={title} />;
}
