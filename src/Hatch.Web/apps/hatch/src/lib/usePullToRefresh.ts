import { useEffect, useRef, useState } from 'react';
import { matchesStandalone } from './viewport';
import {
  isScroller, isVerticalPull, mayRelease, mayStart, pullPhase,
} from './pullToRefresh';
import type { PullPhase } from './pullToRefresh';

export interface PullToRefreshState { distance: number; phase: PullPhase }
const IDLE: PullToRefreshState = { distance: 0, phase: 'idle' };

/** The nearest scrollable ancestor's own top, walking up from the touch
    target the way useAutoGrow.ts's scrollParent walks for the same reason,
    but through isScroller (adds 'overlay') and falling through to
    window.scrollY for the document itself rather than returning null. */
function atOwnTop(target: EventTarget | null): boolean {
  for (let node = target instanceof Element ? target : null; node; node = node.parentElement) {
    if (isScroller(getComputedStyle(node).overflowY)) return node.scrollTop === 0;
  }
  return window.scrollY === 0;
}

function dialogOpen(): boolean {
  return document.querySelector('.hatch-modal__overlay') !== null;
}

export function usePullToRefresh(): PullToRefreshState {
  const [state, setState] = useState<PullToRefreshState>(IDLE);

  // Read inside onTouchEnd instead of the setState updater it fires from:
  // React 18 Strict Mode double-invokes updaters, which would reload twice.
  const phaseRef = useRef<PullPhase>('idle');

  useEffect(() => {
    if (typeof window === 'undefined') return;

    // A plain object, not React state: read and written only inside these
    // four listeners, which is why a ref (not useState) is enough - the same
    // role useLoaded.ts's pausedRef plays for its own listeners.
    const gesture = { active: false, startX: 0, startY: 0 };

    const onTouchStart = (event: TouchEvent) => {
      if (event.touches.length !== 1) return;
      if (!mayStart({
        standalone: matchesStandalone(),
        atTop: atOwnTop(event.target),
        dialogOpen: dialogOpen(),
      })) return;
      const touch = event.touches[0];
      gesture.active = true;
      gesture.startX = touch.clientX;
      gesture.startY = touch.clientY;
      phaseRef.current = 'idle';
      setState(IDLE);
    };

    const onTouchMove = (event: TouchEvent) => {
      if (!gesture.active) return;
      // dnd-kit's TouchSensor (BoardPage.tsx) preventDefaults touchmove, on
      // the card element, once its own 300ms/8px activation constraint is
      // met - and that does not stop this document-level listener from
      // still seeing the event. Without this check a card dragged more than
      // PULL_THRESHOLD_PX would read 'ready' here and reload on release,
      // breaking AC8. Checked in node_modules/@dnd-kit/core's
      // AbstractPointerSensor.handleMove/attach.
      if (event.defaultPrevented) {
        gesture.active = false;
        phaseRef.current = 'idle';
        setState(IDLE);
        return;
      }
      const touch = event.touches[0];
      const deltaX = touch.clientX - gesture.startX;
      const deltaY = touch.clientY - gesture.startY;
      if (!isVerticalPull(deltaX, deltaY)) {
        gesture.active = false;
        phaseRef.current = 'idle';
        setState(IDLE);
        return;
      }
      const distance = Math.max(0, deltaY);
      const phase = pullPhase(distance);
      phaseRef.current = phase;
      setState({ distance, phase });
    };

    const onTouchEnd = () => {
      if (!gesture.active) return;
      gesture.active = false;
      if (mayRelease(phaseRef.current)) window.location.reload();
      phaseRef.current = 'idle';
      setState(IDLE);
    };

    const onTouchCancel = () => {
      gesture.active = false;
      phaseRef.current = 'idle';
      setState(IDLE);
    };

    document.addEventListener('touchstart', onTouchStart, { passive: true });
    document.addEventListener('touchmove', onTouchMove, { passive: true });
    document.addEventListener('touchend', onTouchEnd, { passive: true });
    document.addEventListener('touchcancel', onTouchCancel, { passive: true });
    return () => {
      document.removeEventListener('touchstart', onTouchStart);
      document.removeEventListener('touchmove', onTouchMove);
      document.removeEventListener('touchend', onTouchEnd);
      document.removeEventListener('touchcancel', onTouchCancel);
    };
  }, []);

  return state;
}
