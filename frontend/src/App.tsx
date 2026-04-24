import { useCallback, useEffect, useMemo, useState } from 'react'
import { buildApiUrl } from './api'
import './App.css'

type ProjectLink = {
  label: string
  url: string
}

type ProjectSummary = {
  name: string
  status: string
  priority: string
  cadence: string
  summary: string
  links: ProjectLink[]
}

type PlannedWrite = {
  path: string
  exists: boolean
}

type CreateProjectResponse = {
  projectName: string
  folderPath: string
  registryPath: string
  registryRow: string
  plannedWrites: PlannedWrite[]
}

type ActivateSprintCardPreview = {
  id: string
  title: string
  eligible: boolean
  reason: string
  lane: string
}

type ActivateSprintResponse = {
  projectName: string
  backlogPath: string
  kanbanPath: string
  registryPath: string | null
  willUpdateCadence: boolean
  summary: string
  cards: ActivateSprintCardPreview[]
  plannedWrites: PlannedWrite[]
}

type ProjectStateResponse = {
  projectName: string
  status: string
  cadence: string
  registryPath: string
  summary: string
  updatedPaths: string[]
}

type ArchiveProjectResponse = {
  projectName: string
  sourceFolderPath: string
  archiveFolderPath: string
  registryPath: string
  summary: string
  plannedSteps: string[]
  registryNotesToRemove: string[]
}

type ActionSelection = 'Create Project' | 'Activate Sprint' | 'Archive Project'
type CarouselSlot = 'left' | 'center' | 'right'

type CarouselItem = {
  slot: CarouselSlot
  project: ProjectSummary
  selected: boolean
}

const actionOptions: ActionSelection[] = ['Create Project', 'Activate Sprint', 'Archive Project']
const allowedProjectLinkLabels = new Set(['Brief', 'Kanban', 'Backlog'])

function getCompactStatus(status: string) {
  return status.trim().split(/\s+/)[0] ?? status
}

function getPrimaryProjectLinks(project: ProjectSummary) {
  const order = ['Brief', 'Kanban', 'Backlog']

  return order
    .map((label) => project.links.find((link) => link.label === label))
    .filter((link): link is ProjectLink => Boolean(link && allowedProjectLinkLabels.has(link.label)))
}

function isProjectActive(project: ProjectSummary) {
  return project.status.trim().toLowerCase() === 'active'
}

function App() {
  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api'

  const [projects, setProjects] = useState<ProjectSummary[]>([])
  const [projectsLoading, setProjectsLoading] = useState(true)
  const [projectsError, setProjectsError] = useState<string | null>(null)
  const [selectedProjectName, setSelectedProjectName] = useState<string | null>(null)
  const [selectedAction, setSelectedAction] = useState<ActionSelection>('Create Project')

  const [projectStateRunningName, setProjectStateRunningName] = useState<string | null>(null)
  const [projectStateError, setProjectStateError] = useState<string | null>(null)
  const [projectStateSuccess, setProjectStateSuccess] = useState<string | null>(null)

  const [createProjectName, setCreateProjectName] = useState('')
  const [createProjectSummary, setCreateProjectSummary] = useState('')
  const [createProjectPreview, setCreateProjectPreview] = useState<CreateProjectResponse | null>(null)
  const [createProjectError, setCreateProjectError] = useState<string | null>(null)
  const [createProjectRunning, setCreateProjectRunning] = useState(false)
  const [createProjectCreated, setCreateProjectCreated] = useState(false)

  const [activateSprintPreview, setActivateSprintPreview] = useState<ActivateSprintResponse | null>(null)
  const [activateSprintError, setActivateSprintError] = useState<string | null>(null)
  const [activateSprintRunning, setActivateSprintRunning] = useState(false)
  const [activateSprintExecuted, setActivateSprintExecuted] = useState(false)
  const [activateSprintUpdateCadence, setActivateSprintUpdateCadence] = useState(true)

  const [archiveProjectError, setArchiveProjectError] = useState<string | null>(null)
  const [archiveProjectRunning, setArchiveProjectRunning] = useState(false)
  const [archiveProjectExecuted, setArchiveProjectExecuted] = useState(false)

  const loadProjects = useCallback(async (signal?: AbortSignal) => {
    setProjectsLoading(true)
    setProjectsError(null)

    try {
      const response = await fetch(buildApiUrl(apiBaseUrl, '/projects'), { signal })

      if (!response.ok) {
        throw new Error(`API returned ${response.status}`)
      }

      const payload = (await response.json()) as ProjectSummary[]
      setProjects(payload)
      return payload
    } catch (err) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        return null
      }

      setProjectsError(err instanceof Error ? err.message : 'Unknown error')
      return null
    } finally {
      setProjectsLoading(false)
    }
  }, [apiBaseUrl])

  useEffect(() => {
    const controller = new AbortController()
    void loadProjects(controller.signal)
    return () => controller.abort()
  }, [loadProjects])

  useEffect(() => {
    if (!projects.length) {
      setSelectedProjectName(null)
      return
    }

    if (!selectedProjectName || !projects.some((project) => project.name === selectedProjectName)) {
      setSelectedProjectName(projects[0].name)
    }
  }, [projects, selectedProjectName])

  useEffect(() => {
    setProjectStateError(null)
    setProjectStateSuccess(null)
  }, [selectedProjectName])

  useEffect(() => {
    if (!projectStateSuccess) {
      return
    }

    const timeoutId = window.setTimeout(() => {
      setProjectStateSuccess(null)
    }, 4500)

    return () => window.clearTimeout(timeoutId)
  }, [projectStateSuccess])

  useEffect(() => {
    setCreateProjectPreview(null)
    setCreateProjectError(null)
    setCreateProjectCreated(false)
    setActivateSprintPreview(null)
    setActivateSprintError(null)
    setActivateSprintExecuted(false)
    setArchiveProjectError(null)
    setArchiveProjectExecuted(false)
  }, [selectedAction])

  useEffect(() => {
    setCreateProjectPreview(null)
    setCreateProjectError(null)
    setCreateProjectCreated(false)
  }, [createProjectName, createProjectSummary])

  useEffect(() => {
    setActivateSprintPreview(null)
    setActivateSprintError(null)
    setActivateSprintExecuted(false)
    setArchiveProjectError(null)
    setArchiveProjectExecuted(false)
  }, [selectedProjectName, activateSprintUpdateCadence])

  const selectedProjectIndex = useMemo(
    () => projects.findIndex((project) => project.name === selectedProjectName),
    [projects, selectedProjectName],
  )

  const selectedProject = useMemo(
    () => (selectedProjectIndex >= 0 ? projects[selectedProjectIndex] : null),
    [projects, selectedProjectIndex],
  )

  const carouselItems = useMemo<CarouselItem[]>(() => {
    if (!projects.length || selectedProjectIndex < 0) {
      return []
    }

    if (projects.length === 1) {
      return [{ slot: 'center', project: projects[selectedProjectIndex], selected: true }]
    }

    if (projects.length === 2) {
      return selectedProjectIndex === 0
        ? [
            { slot: 'center', project: projects[0], selected: true },
            { slot: 'right', project: projects[1], selected: false },
          ]
        : [
            { slot: 'left', project: projects[0], selected: false },
            { slot: 'center', project: projects[1], selected: true },
          ]
    }

    const previousIndex = selectedProjectIndex === 0 ? projects.length - 1 : selectedProjectIndex - 1
    const nextIndex = selectedProjectIndex === projects.length - 1 ? 0 : selectedProjectIndex + 1

    return [
      { slot: 'left', project: projects[previousIndex], selected: false },
      { slot: 'center', project: projects[selectedProjectIndex], selected: true },
      { slot: 'right', project: projects[nextIndex], selected: false },
    ]
  }, [projects, selectedProjectIndex])

  const canPreviewCreateProject =
    createProjectName.trim().length > 0 && !createProjectRunning

  const canExecuteCreateProject =
    canPreviewCreateProject && createProjectPreview !== null && !createProjectCreated

  const canPreviewActivateSprint =
    selectedProject !== null && isProjectActive(selectedProject) && !activateSprintRunning

  const canExecuteActivateSprint =
    canPreviewActivateSprint && activateSprintPreview !== null && !activateSprintExecuted

  const canExecuteArchiveProject =
    selectedProject !== null && !archiveProjectRunning && !archiveProjectExecuted

  async function previewCreateProject() {
    setCreateProjectRunning(true)
    setCreateProjectError(null)
    setCreateProjectCreated(false)

    try {
      const response = await fetch(buildApiUrl(apiBaseUrl, '/actions/create-project/preview'), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          projectName: createProjectName,
          summary: createProjectSummary,
        }),
      })

      if (!response.ok) {
        const message = await response.text()
        throw new Error(message || `API returned ${response.status}`)
      }

      const payload = (await response.json()) as CreateProjectResponse
      setCreateProjectPreview(payload)
    } catch (err) {
      setCreateProjectPreview(null)
      setCreateProjectError(err instanceof Error ? err.message : 'Unknown error')
    } finally {
      setCreateProjectRunning(false)
    }
  }

  async function executeCreateProject() {
    setCreateProjectRunning(true)
    setCreateProjectError(null)

    try {
      const response = await fetch(buildApiUrl(apiBaseUrl, '/actions/create-project'), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          projectName: createProjectName,
          summary: createProjectSummary,
        }),
      })

      if (!response.ok) {
        const message = await response.text()
        throw new Error(message || `API returned ${response.status}`)
      }

      const payload = (await response.json()) as CreateProjectResponse
      setCreateProjectPreview(payload)
      setCreateProjectCreated(true)

      const refreshedProjects = await loadProjects()
      if (refreshedProjects?.some((project) => project.name === payload.projectName)) {
        setSelectedProjectName(payload.projectName)
      }
    } catch (err) {
      setCreateProjectError(err instanceof Error ? err.message : 'Unknown error')
    } finally {
      setCreateProjectRunning(false)
    }
  }

  async function previewActivateSprint() {
    if (!selectedProject || !isProjectActive(selectedProject)) {
      return
    }

    setActivateSprintRunning(true)
    setActivateSprintError(null)
    setActivateSprintExecuted(false)

    try {
      const response = await fetch(buildApiUrl(apiBaseUrl, '/actions/activate-sprint/preview'), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          projectName: selectedProject.name,
          updateCadence: activateSprintUpdateCadence,
        }),
      })

      if (!response.ok) {
        const message = await response.text()
        throw new Error(message || `API returned ${response.status}`)
      }

      const payload = (await response.json()) as ActivateSprintResponse
      setActivateSprintPreview(payload)
    } catch (err) {
      setActivateSprintPreview(null)
      setActivateSprintError(err instanceof Error ? err.message : 'Unknown error')
    } finally {
      setActivateSprintRunning(false)
    }
  }

  async function executeActivateSprint() {
    if (!selectedProject || !isProjectActive(selectedProject)) {
      return
    }

    setActivateSprintRunning(true)
    setActivateSprintError(null)

    try {
      const response = await fetch(buildApiUrl(apiBaseUrl, '/actions/activate-sprint'), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          projectName: selectedProject.name,
          updateCadence: activateSprintUpdateCadence,
        }),
      })

      if (!response.ok) {
        const message = await response.text()
        throw new Error(message || `API returned ${response.status}`)
      }

      const payload = (await response.json()) as ActivateSprintResponse
      setActivateSprintPreview(payload)
      setActivateSprintExecuted(true)
      await loadProjects()
    } catch (err) {
      setActivateSprintError(err instanceof Error ? err.message : 'Unknown error')
    } finally {
      setActivateSprintRunning(false)
    }
  }

  async function toggleProjectActiveState(project: ProjectSummary) {
    setProjectStateRunningName(project.name)
    setProjectStateError(null)
    setProjectStateSuccess(null)
    setActivateSprintPreview(null)
    setActivateSprintError(null)
    setActivateSprintExecuted(false)

    try {
      const response = await fetch(buildApiUrl(apiBaseUrl, '/actions/set-project-active-state'), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          projectName: project.name,
          isActive: !isProjectActive(project),
        }),
      })

      if (!response.ok) {
        const message = await response.text()
        throw new Error(message || `API returned ${response.status}`)
      }

      const payload = (await response.json()) as ProjectStateResponse
      setProjectStateSuccess(payload.summary)
      await loadProjects()
      setSelectedProjectName(payload.projectName)
    } catch (err) {
      setProjectStateError(err instanceof Error ? err.message : 'Unknown error')
    } finally {
      setProjectStateRunningName(null)
    }
  }

  async function executeArchiveProject() {
    if (!selectedProject) {
      return
    }

    if (!window.confirm(`Are you sure you want to archive ${selectedProject.name}?`)) {
      return
    }

    setArchiveProjectRunning(true)
    setArchiveProjectError(null)

    try {
      const response = await fetch(buildApiUrl(apiBaseUrl, '/actions/archive-project'), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          projectName: selectedProject.name,
        }),
      })

      if (!response.ok) {
        const message = await response.text()
        throw new Error(message || `API returned ${response.status}`)
      }

      await response.json() as ArchiveProjectResponse
      setArchiveProjectExecuted(true)
      await loadProjects()
    } catch (err) {
      setArchiveProjectError(err instanceof Error ? err.message : 'Unknown error')
    } finally {
      setArchiveProjectRunning(false)
    }
  }

  function selectPreviousProject() {
    if (!projects.length || selectedProjectIndex < 0) {
      return
    }

    const nextIndex = selectedProjectIndex === 0 ? projects.length - 1 : selectedProjectIndex - 1
    setSelectedProjectName(projects[nextIndex].name)
  }

  function selectNextProject() {
    if (!projects.length || selectedProjectIndex < 0) {
      return
    }

    const nextIndex = selectedProjectIndex === projects.length - 1 ? 0 : selectedProjectIndex + 1
    setSelectedProjectName(projects[nextIndex].name)
  }

  function renderProjectStateToggle(project: ProjectSummary) {
    const active = isProjectActive(project)
    const running = projectStateRunningName === project.name

    return (
      <button
        type="button"
        className={active ? 'project-state-toggle active' : 'project-state-toggle inactive'}
        onClick={() => void toggleProjectActiveState(project)}
        disabled={running}
        aria-label={active ? `Set ${project.name} inactive` : `Set ${project.name} active`}
        title={active ? 'Deactivate this project' : 'Reactivate this project'}
      >
        <span className="project-state-dot" aria-hidden="true" />
        <span>{running ? 'Saving…' : active ? 'Active' : 'Inactive'}</span>
      </button>
    )
  }

  return (
    <main className="app-shell">
      <section className="stage-shell">
        <header className="page-header">
          <div>
            <p className="eyebrow">Mission Control</p>
            <h1>Projects</h1>
          </div>
        </header>

        <section className="projects-panel">
          {projectsError && <p className="error-banner">Could not load projects: {projectsError}</p>}
          {projectStateError ? <p className="error-banner">{projectStateError}</p> : null}
          {projectStateSuccess ? <p className="success-banner">{projectStateSuccess}</p> : null}

          {!projectsLoading && !projectsError && !projects.length && (
            <div className="empty-panel">
              <h2>No projects found</h2>
              <p>Create one below to get started.</p>
            </div>
          )}

          {!projectsError && !!carouselItems.length && (
            <>
              <div className="project-carousel" aria-label="Project carousel">
                {carouselItems.map(({ project, slot, selected }) =>
                  selected ? (
                    <article key={`${slot}-${project.name}`} className={`project-card carousel-card slot-${slot} selected`}>
                      <div className="project-card-head">
                        <div>
                          <h2>{project.name}</h2>
                          <p className="project-meta-line">
                            {renderProjectStateToggle(project)}
                            <span title={project.cadence}>{getCompactStatus(project.cadence)}</span>
                          </p>
                        </div>
                      </div>

                      <p className="project-summary selected-summary">{project.summary}</p>

                      <div className="project-links" aria-label={`${project.name} links`}>
                        {getPrimaryProjectLinks(project).map((link) => (
                          <a
                            key={`${project.name}-${link.label}`}
                            className="project-link"
                            href={link.url}
                          >
                            {link.label}
                          </a>
                        ))}
                      </div>
                    </article>
                  ) : (
                    <article key={`${slot}-${project.name}`} className={`project-card carousel-card slot-${slot} selectable`}>
                      <div className="project-card-head">
                        <div>
                          <h3>{project.name}</h3>
                          <p className="project-meta-line">
                            {renderProjectStateToggle(project)}
                            <span title={project.cadence}>{getCompactStatus(project.cadence)}</span>
                          </p>
                        </div>
                      </div>

                      <p className="project-summary side-summary">{project.summary}</p>

                      <div className="project-card-footer">
                        <span className="queue-label">{slot === 'left' ? 'Previous' : 'Next'}</span>
                        <button
                          type="button"
                          className="select-card-button"
                          onClick={() => setSelectedProjectName(project.name)}
                        >
                          Select
                        </button>
                      </div>
                    </article>
                  ),
                )}
              </div>

              <div className="carousel-controls">
                <button type="button" className="nav-button" onClick={selectPreviousProject}>
                  Previous
                </button>
                <span className="carousel-status">
                  {selectedProject ? `${selectedProjectIndex + 1} of ${projects.length}` : 'No project selected'}
                </span>
                <button type="button" className="nav-button" onClick={selectNextProject}>
                  Next
                </button>
              </div>
            </>
          )}
        </section>

        <section className="actions-strip" aria-label="Project actions">
          {actionOptions.map((action) => (
            <button
              key={action}
              type="button"
              className={selectedAction === action ? 'action-toggle active' : 'action-toggle'}
              onClick={() => setSelectedAction(action)}
            >
              {action}
            </button>
          ))}
        </section>

        <section className="workspace-panel">
          <header className="workspace-header">
            <div>
              <div className="title-with-tooltip">
                <h2>{selectedAction}</h2>
                {selectedAction === 'Create Project' ? (
                  <span
                    className="info-tooltip"
                    title="Creates a new project folder with Brief, Kanban, Backlog, Operating Notes, and a registry row."
                    aria-label="Create Project help"
                  >
                    ?
                  </span>
                ) : null}
                {selectedAction === 'Activate Sprint' ? (
                  <span
                    className="info-tooltip"
                    title="Moves eligible backlog cards into Ready and can update the registry cadence when the match is clean."
                    aria-label="Activate Sprint help"
                  >
                    ?
                  </span>
                ) : null}
                {selectedAction === 'Archive Project' ? (
                  <span
                    className="info-tooltip"
                    title="Moves the project into 99 Archive/Project Archive, removes its registry row, and asks for confirmation before the archive write."
                    aria-label="Archive Project help"
                  >
                    ?
                  </span>
                ) : null}
              </div>
            </div>
          </header>

          {selectedAction === 'Create Project' && (
            <div className="workspace-stack">
              <div className="form-stack">
                <label>
                  <span>Project name</span>
                  <input
                    value={createProjectName}
                    onChange={(event) => setCreateProjectName(event.target.value)}
                    placeholder="Mission Control Next"
                  />
                </label>
                <label>
                  <span>Summary</span>
                  <textarea
                    value={createProjectSummary}
                    onChange={(event) => setCreateProjectSummary(event.target.value)}
                    placeholder="Short project summary"
                    rows={3}
                  />
                </label>
              </div>

              <div className="action-controls">
                <button
                  type="button"
                  className="primary-button"
                  onClick={() => void previewCreateProject()}
                  disabled={!canPreviewCreateProject}
                >
                  {createProjectRunning ? 'Preparing preview…' : 'Preview'}
                </button>
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => void executeCreateProject()}
                  disabled={!canExecuteCreateProject || createProjectRunning}
                >
                  {createProjectRunning ? 'Creating…' : 'Create project'}
                </button>
              </div>

              {createProjectError ? <p className="error-banner">{createProjectError}</p> : null}
              {createProjectCreated ? <p className="success-banner">Project created.</p> : null}

              {createProjectPreview && !createProjectCreated ? (
                <div className="preview-surface">
                  <div className="preview-section">
                    <h3>Registry row</h3>
                    <pre>{createProjectPreview.registryRow}</pre>
                  </div>
                  <div className="preview-section">
                    <h3>Planned writes</h3>
                    <ul className="stack-list">
                      {createProjectPreview.plannedWrites.map((write) => (
                        <li key={write.path}>
                          <code>{write.path}</code>
                          <span>{write.exists ? 'Already exists' : 'New file'}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              ) : null}
            </div>
          )}

          {selectedAction === 'Activate Sprint' && (
            <div className="workspace-stack">
              {!selectedProject ? (
                <div className="empty-panel compact-empty">
                  <h3>Pick a project first</h3>
                  <p>Choose one from the carousel above.</p>
                </div>
              ) : !isProjectActive(selectedProject) ? (
                <div className="empty-panel compact-empty">
                  <h3>Reactivate this project first</h3>
                  <p>Inactive projects cannot activate a sprint.</p>
                </div>
              ) : (
                <>
                  <label className="checkbox-row">
                    <input
                      type="checkbox"
                      checked={activateSprintUpdateCadence}
                      onChange={(event) => setActivateSprintUpdateCadence(event.target.checked)}
                    />
                    <span>Set cadence to every-heartbeat when the registry row matches cleanly.</span>
                  </label>

                  <div className="action-controls">
                    <button
                      type="button"
                      className="primary-button"
                      onClick={() => void previewActivateSprint()}
                      disabled={!canPreviewActivateSprint}
                    >
                      {activateSprintRunning ? 'Preparing preview…' : 'Preview'}
                    </button>
                    <button
                      type="button"
                      className="secondary-button"
                      onClick={() => void executeActivateSprint()}
                      disabled={!canExecuteActivateSprint || activateSprintRunning}
                    >
                      {activateSprintRunning ? 'Activating…' : 'Activate sprint'}
                    </button>
                  </div>
                </>
              )}

              {activateSprintError ? <p className="error-banner">{activateSprintError}</p> : null}
              {activateSprintExecuted ? <p className="success-banner">Sprint activated.</p> : null}

              {activateSprintPreview && !activateSprintExecuted ? (
                <div className="preview-surface">
                  <p className="preview-summary">{activateSprintPreview.summary}</p>

                  <div className="preview-section">
                    <h3>Cards</h3>
                    <ul className="stack-list cards-list">
                      {activateSprintPreview.cards.map((card) => (
                        <li key={card.id} className={card.eligible ? 'eligible' : 'ineligible'}>
                          <div>
                            <strong>{card.id}</strong>
                            <p>{card.title}</p>
                          </div>
                          <span>{card.eligible ? 'Eligible' : card.reason}</span>
                        </li>
                      ))}
                    </ul>
                  </div>

                  <div className="preview-section">
                    <h3>Planned writes</h3>
                    <ul className="stack-list">
                      {activateSprintPreview.plannedWrites.map((write) => (
                        <li key={write.path}>
                          <code>{write.path}</code>
                          <span>{write.exists ? 'Update existing' : 'Create new'}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              ) : null}
            </div>
          )}

          {selectedAction === 'Archive Project' && (
            <div className="workspace-stack">
              {!selectedProject ? (
                <div className="empty-panel compact-empty">
                  <h3>Pick a project first</h3>
                  <p>Choose one from the carousel above.</p>
                </div>
              ) : (
                <>
                  <div className="warning-panel compact-empty">
                    <p>Archive moves the project to <code>99 Archive/Project Archive</code>, removes the live registry row, and asks for one confirmation before it runs.</p>
                  </div>

                  <div className="action-controls">
                    <button
                      type="button"
                      className="danger-button"
                      onClick={() => void executeArchiveProject()}
                      disabled={!canExecuteArchiveProject || archiveProjectRunning}
                    >
                      {archiveProjectRunning ? 'Archiving…' : 'Archive project'}
                    </button>
                  </div>
                </>
              )}

              {archiveProjectError ? <p className="error-banner">{archiveProjectError}</p> : null}
              {archiveProjectExecuted ? <p className="success-banner">Project archived.</p> : null}

            </div>
          )}
        </section>
      </section>
    </main>
  )
}

export default App
