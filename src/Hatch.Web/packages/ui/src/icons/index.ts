import type { ComponentType } from 'react';
import { AnchorIcon } from './AnchorIcon';
import { BikeIcon } from './BikeIcon';
import { BookIcon } from './BookIcon';
import { BoxIcon } from './BoxIcon';
import { BusIcon } from './BusIcon';
import { CameraIcon } from './CameraIcon';
import { CarIcon } from './CarIcon';
import { CircleIcon } from './CircleIcon';
import { ClockIcon } from './ClockIcon';
import { CloudIcon } from './CloudIcon';
import { CompassIcon } from './CompassIcon';
import { DropletIcon } from './DropletIcon';
import { GiftIcon } from './GiftIcon';
import { HammerIcon } from './HammerIcon';
import { HexagonIcon } from './HexagonIcon';
import { KeyIcon } from './KeyIcon';
import { LeafIcon } from './LeafIcon';
import { LockIcon } from './LockIcon';
import { MapIcon } from './MapIcon';
import { MountainIcon } from './MountainIcon';
import { PaintbrushIcon } from './PaintbrushIcon';
import { PlaneIcon } from './PlaneIcon';
import { PuzzleIcon } from './PuzzleIcon';
import { RocketIcon } from './RocketIcon';
import { RulerIcon } from './RulerIcon';
import { ScissorsIcon } from './ScissorsIcon';
import { SquareIcon } from './SquareIcon';
import { SunIcon } from './SunIcon';
import { TriangleIcon } from './TriangleIcon';
import { TruckIcon } from './TruckIcon';
import { UmbrellaIcon } from './UmbrellaIcon';
import { WrenchIcon } from './WrenchIcon';

/** One entry in the closed set a project may draw instead of its letters.
    `slug` is the only part the server ever sees - it stores it and validates
    it against `EfHatchProject.IconPattern` (`^[a-z0-9-]{1,40}$`) without
    knowing what it draws, so the drawing is this list's problem alone. */
export interface ProjectIcon {
  slug: string;
  title: string;
  Icon: ComponentType<{ className?: string }>;
}

/**
 * The closed set of stock icons a project may draw, vendored from Lucide
 * (LICENSE beside this file) rather than drawn in-house, and curated to
 * generic subjects - tools, shapes, nature, objects, vehicles - and nothing
 * that is a brand or a company (docs/ethos.md: a project mark that could only
 * mean one real-world company is a fact true of exactly one installation's
 * taste, not a stock glyph).
 *
 * Grouped here in the order the gallery's grid and a picker built on it would
 * want to show them, not alphabetically.
 */
export const PROJECT_ICONS: ProjectIcon[] = [
  { slug: 'hammer', title: 'Hammer', Icon: HammerIcon },
  { slug: 'wrench', title: 'Wrench', Icon: WrenchIcon },
  { slug: 'scissors', title: 'Scissors', Icon: ScissorsIcon },
  { slug: 'paintbrush', title: 'Paintbrush', Icon: PaintbrushIcon },
  { slug: 'ruler', title: 'Ruler', Icon: RulerIcon },
  { slug: 'circle', title: 'Circle', Icon: CircleIcon },
  { slug: 'square', title: 'Square', Icon: SquareIcon },
  { slug: 'triangle', title: 'Triangle', Icon: TriangleIcon },
  { slug: 'hexagon', title: 'Hexagon', Icon: HexagonIcon },
  { slug: 'leaf', title: 'Leaf', Icon: LeafIcon },
  { slug: 'mountain', title: 'Mountain', Icon: MountainIcon },
  { slug: 'sun', title: 'Sun', Icon: SunIcon },
  { slug: 'cloud', title: 'Cloud', Icon: CloudIcon },
  { slug: 'droplet', title: 'Droplet', Icon: DropletIcon },
  { slug: 'anchor', title: 'Anchor', Icon: AnchorIcon },
  { slug: 'book', title: 'Book', Icon: BookIcon },
  { slug: 'box', title: 'Box', Icon: BoxIcon },
  { slug: 'camera', title: 'Camera', Icon: CameraIcon },
  { slug: 'clock', title: 'Clock', Icon: ClockIcon },
  { slug: 'compass', title: 'Compass', Icon: CompassIcon },
  { slug: 'gift', title: 'Gift', Icon: GiftIcon },
  { slug: 'key', title: 'Key', Icon: KeyIcon },
  { slug: 'lock', title: 'Lock', Icon: LockIcon },
  { slug: 'map', title: 'Map', Icon: MapIcon },
  { slug: 'umbrella', title: 'Umbrella', Icon: UmbrellaIcon },
  { slug: 'puzzle', title: 'Puzzle', Icon: PuzzleIcon },
  { slug: 'bike', title: 'Bike', Icon: BikeIcon },
  { slug: 'car', title: 'Car', Icon: CarIcon },
  { slug: 'truck', title: 'Truck', Icon: TruckIcon },
  { slug: 'rocket', title: 'Rocket', Icon: RocketIcon },
  { slug: 'plane', title: 'Plane', Icon: PlaneIcon },
  { slug: 'bus', title: 'Bus', Icon: BusIcon },
];

/** `PROJECT_ICONS` keyed by slug, for the lookup `<ProjectMark>` does on
    every render - built once rather than a `.find()` per project on a board
    full of them. */
export const PROJECT_ICONS_BY_SLUG: Record<string, ProjectIcon> = Object.fromEntries(
  PROJECT_ICONS.map((icon) => [icon.slug, icon]),
);
