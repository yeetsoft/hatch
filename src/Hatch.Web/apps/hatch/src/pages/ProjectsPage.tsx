import { useState } from 'react';
import { Button, Card, EmptyState, Field, Modal, PageHeader, Table } from '@hatch/ui';
import { createProject, deleteProject, getProjects } from '../api/client';
import { ProjectEditDialog } from '../components/ProjectEditDialog';
import { ProjectRow } from '../components/ProjectRow';
import { message } from '../lib/errors';
import { useLoaded } from '../lib/useLoaded';
import type { Project } from '../types';

export function ProjectsPage() {
  const { data: projects, error, setError, reload } = useLoaded<Project[]>(getProjects);
  const [key, setKey] = useState('');
  const [name, setName] = useState('');
  const [saving, setSaving] = useState(false);
  const [editing, setEditing] = useState<Project | null>(null);
  const [deleting, setDeleting] = useState<Project | null>(null);
  const [busy, setBusy] = useState(false);

  async function create() {
    setSaving(true);
    try {
      await createProject({ key, name });
      setKey('');
      setName('');
      setError(null);
      await reload();
    } catch (err) {
      setError(message(err));
    } finally {
      setSaving(false);
    }
  }

  async function remove(project: Project) {
    setBusy(true);
    try {
      await deleteProject(project.id);
      setError(null);
    } catch (err) {
      setError(message(err));
    } finally {
      setBusy(false);
      setDeleting(null);
      await reload();
    }
  }

  return (
    <div className="hatch-page">
      <PageHeader
        title="Projects"
        description="A project is a key namespace. Its key can be changed, at a price this page states before it lets you."
      />

      {error && <p className="text-danger">{error}</p>}

      <Card>
        <h2 className="hatch-section-title">New project</h2>
        <div className="hatch-field-grid">
          <Field label="Key" hint="Two to six characters, e.g. AER.">
            <input value={key} onChange={(e) => setKey(e.target.value)} />
          </Field>
          <Field label="Name" hint="What it is called out loud.">
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
        </div>
        <div className="hatch-form-actions">
          <Button variant="primary" loading={saving} disabled={!key || !name} onClick={() => void create()}>
            Add project
          </Button>
        </div>
      </Card>

      {projects?.length === 0 && <EmptyState message="No projects yet." />}

      {projects && projects.length > 0 && (
        <Card flush>
          <Table>
            <thead>
              <tr>
                <th>Key</th>
                <th>Name</th>
                <th>Issues</th>
                <th>Repositories</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {projects.map((project) => (
                <ProjectRow
                  key={project.id}
                  project={project}
                  onEdit={() => setEditing(project)}
                  onDelete={() => setDeleting(project)}
                />
              ))}
            </tbody>
          </Table>
        </Card>
      )}

      <ProjectEditDialog project={editing} onClose={() => setEditing(null)} onSaved={() => void reload()} />

      <Modal open={deleting !== null} onClose={() => setDeleting(null)} title={`Delete ${deleting?.key ?? ''}?`}>
        {deleting && (
          <div className="hatch-claim-clear">
            <p>This removes the project outright. There is no undo.</p>
            <div className="hatch-form-actions">
              <Button onClick={() => setDeleting(null)}>Leave it</Button>
              <Button variant="danger" loading={busy} onClick={() => void remove(deleting)}>
                Delete
              </Button>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
