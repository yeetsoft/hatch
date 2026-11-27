import { useState } from 'react';
import { Modal } from '@hatch/ui';
import { shapeProject } from '../lib/ambitionShape';
import type { Project } from '../types';
import { AmbitionShape } from './AmbitionShape';
import { ProjectChooser } from './ProjectChooser';

/**
 * The Ambitions page's door to shaping: the Dreams page's form (words, images
 * or a video, Shape) in a dialog, with its own project picker and no dreams.
 *
 * `Modal` draws nothing while closed, so each opening starts blank; a refusal
 * leaves the dialog mounted, so the words stay.
 */
export function NewAmbitionDialog({
  open,
  projects,
  onClose,
}: {
  open: boolean;
  projects: Project[];
  onClose: () => void;
}) {
  return (
    <Modal open={open} onClose={onClose} title="New ambition">
      <NewAmbitionForm projects={projects} />
    </Modal>
  );
}

/** The picker and the form, holding the pick. Its own component so the pick
    goes with the dialog's contents when it closes. */
export function NewAmbitionForm({ projects }: { projects: Project[] }) {
  const [picked, setPicked] = useState<number | null>(null);
  const projectId = shapeProject(picked, projects);

  return (
    <div className="hatch-new-ambition">
      {projects.length > 0 && <ProjectChooser projects={projects} value={projectId} onChange={setPicked} legend="Project" />}
      <AmbitionShape projectId={projectId} projectCount={projects.length} useOnScreen={false} dreams={[]} inDialog />
    </div>
  );
}
