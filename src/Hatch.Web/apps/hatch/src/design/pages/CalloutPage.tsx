import type { CSSProperties, ReactNode, RefObject } from 'react';
import { Callout, useCallout } from '@hatch/ui';
import { GalleryPage, GallerySection } from '../components/Gallery';

/* Every callout here is the real Callout, driven by the real useCallout. */

interface AnchorProps {
  heading: string;
  className?: string;
  style?: CSSProperties;
  children: ReactNode;
}

/* useCallout()'s anchorRef is RefObject<HTMLElement | null> - deliberately
   generic, since an anchor on the board is not always a button - so it needs
   a cast to sit in a <button>'s own ref, which wants RefObject<HTMLButtonElement
   | null>. */
function Anchor({ heading, className, style, children }: AnchorProps) {
  const tip = useCallout();
  return (
    <>
      <button
        type="button"
        className={className}
        style={style}
        ref={tip.anchorRef as RefObject<HTMLButtonElement | null>}
        aria-describedby={tip.describedBy}
        {...tip.hover}
        {...tip.focus}
      >
        {heading}
      </button>
      <Callout id={tip.id} anchor={tip.anchorRef} open={tip.open} heading={heading}>
        {children}
      </Callout>
    </>
  );
}

export function CalloutPage() {
  return (
    <GalleryPage
      title="Callout"
      blurb="A small popover that points at its anchor and stays on screen: opened by hover or keyboard focus, and edged in a colour the caller chooses."
    >
      <GallerySection
        title="Hover and focus"
        note="Opens when a pointer that can hover rests on the anchor, and when keyboard focus reaches it (:focus-visible). Closes on pointer-leave, blur, Escape, or a press elsewhere."
      >
        <div className="design-stage-page design-callout-stage">
          <Anchor heading="Hover or focus me">
            Opens on hover, when the pointer can hover, and when focus reaches this button from the keyboard.
          </Anchor>
        </div>
      </GallerySection>

      <GallerySection
        title="A caller's own edge colour"
        note="The edge colour is the caller's to set: --callout-edge, on the callout or on anything above it. With none set it falls back to --primary."
      >
        <div className="design-stage-page design-callout-stage">
          <div style={{ '--callout-edge': 'var(--danger)' } as CSSProperties}>
            <Anchor heading="Edged in --danger">
              Set on an ancestor div, not on the Callout itself - CalloutProps carries no style prop.
            </Anchor>
          </div>
        </div>
      </GallerySection>

      <GallerySection
        title="One open at a time"
        note="A Callout.Group keeps at most one callout open: opening another in the same group switches at once between anchors rather than stacking."
      >
        <div className="design-stage-page design-callout-stage">
          <Callout.Group>
            <Anchor heading="First">Only one callout in this group is ever open.</Anchor>
            <Anchor heading="Second">Opening this one closes the first - it does not stack.</Anchor>
          </Callout.Group>
        </div>
      </GallerySection>

      <GallerySection
        title="Every corner of the viewport"
        note="Each anchor is position: fixed to the real browser viewport, not to a stage box. Resize the window and every callout still lands wholly on screen, because placeCallout clamps against the viewport."
      >
        <Anchor heading="Top left" className="design-callout-corner design-callout-corner--top-left">
          Clamped inside the top-left corner at any window size.
        </Anchor>
        <Anchor heading="Top right" className="design-callout-corner design-callout-corner--top-right">
          Clamped inside the top-right corner at any window size.
        </Anchor>
        <Anchor heading="Bottom left" className="design-callout-corner design-callout-corner--bottom-left">
          Clamped inside the bottom-left corner at any window size.
        </Anchor>
        <Anchor heading="Bottom right" className="design-callout-corner design-callout-corner--bottom-right">
          Clamped inside the bottom-right corner at any window size.
        </Anchor>
      </GallerySection>
    </GalleryPage>
  );
}
