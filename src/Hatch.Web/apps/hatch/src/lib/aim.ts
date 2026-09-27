/* Which column, and which slot in it, a drag over the board is pointing at.

   dnd-kit's own algorithms score by rectangle geometry, not the pointer: a
   column runs the full height of the board, so its two bottom corners sit far
   below a card-sized drag preview, and a card one column over at the same
   height reads as closer - usually the dragged card's own original slot,
   which dnd-kit then reports as "dropped on itself" (lib/place.ts) and
   nothing moves. This is the fix, read out of dnd-kit's own
   multiple-containers recipe: `pointerWithin` to find the column the pointer
   is actually inside, narrowed to that column's own cards, then
   `closestCenter` to name the nearest of them. */

import { closestCenter, closestCorners, pointerWithin } from '@dnd-kit/core';
import type { ClientRect, Collision, CollisionDetection } from '@dnd-kit/core';
import type { IssueCard } from '../types';
import { COLUMN, targetStatusId } from './place';

export function aimAt(cards: IssueCard[]): CollisionDetection {
  return (args) => {
    const { pointerCoordinates, droppableContainers, droppableRects } = args;

    // A keyboard drag has no pointer to read - dnd-kit only derives one from a
    // mouse or touch event - so it keeps the answer it always had.
    if (!pointerCoordinates) return closestCorners(args);

    const columns = droppableContainers.filter((c) => String(c.id).startsWith(COLUMN));
    const [column]: Collision[] = pointerWithin({ ...args, droppableContainers: columns });
    if (!column) return [];

    const statusId = targetStatusId(String(column.id), cards);
    const inColumn = droppableContainers.filter(
      (c) => !String(c.id).startsWith(COLUMN) && targetStatusId(String(c.id), cards) === statusId,
    );
    if (inColumn.length === 0) return [column];

    const onCard = pointerWithin({ ...args, droppableContainers: inColumn });
    if (onCard.length > 0) return onCard;

    // Below the lowest card - or anywhere in the empty space under it - is the
    // column itself, which place() reads as the bottom of it. The nearest
    // card's own slot would land one short.
    const lowestBottom = Math.max(...inColumn.map((c) => droppableRects.get(c.id)?.bottom ?? -Infinity));
    if (pointerCoordinates.y > lowestBottom) return [column];

    // Anywhere else in the column - the heading, or the gap between two cards
    // - names the nearest card by the pointer alone, a single point rather
    // than the dragged preview's own corners.
    const atPointer: ClientRect = {
      top: pointerCoordinates.y,
      bottom: pointerCoordinates.y,
      left: pointerCoordinates.x,
      right: pointerCoordinates.x,
      width: 0,
      height: 0,
    };

    return closestCenter({ ...args, collisionRect: atPointer, droppableContainers: inColumn });
  };
}
