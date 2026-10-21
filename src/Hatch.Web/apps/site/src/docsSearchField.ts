/* The docs sidebar's search field, inserted by this script at the top of the
   desk `<nav class="site-docs-sidebar">` and the phone
   `<details class="site-docs-sidebar-phone">`'s own nav (renderSidebar,
   vite-plugin-docs.ts:96) - plain DOM, since the site ships no React. Every
   search and keyboard decision is lib/docsSearch.ts's (HA-1063); DocsSearch.tsx
   is the same behaviour, in React - this only wires it to the DOM, twice, each
   instance with its own id prefix so a listbox's aria-activedescendant always
   names one of its own rows.

   The index (HA-1279's docs/search-index.json) is fetched once a search
   actually runs - the first keystroke, or an address that already carries
   `?q=` - not at load, and the one promise is shared by both instances. A
   failed fetch clears it so the next search retries: loadMermaid's shape
   (mermaidDraw.ts:23-28). */

import {
  docSearchStatus,
  docTarget,
  keepDocHighlight,
  moveDocHighlight,
  resultId,
  searchDocs,
  withDocQuery,
  type DocResult,
} from '../../hatch/src/lib/docsSearch'

const DEBOUNCE_MS = 300

type DocEntries = Parameters<typeof searchDocs>[1]

const INDEX_URL = `${import.meta.env.BASE_URL}docs/search-index.json`

let indexPromise: Promise<DocEntries> | null = null

function loadIndex(): Promise<DocEntries> {
  indexPromise ??= fetch(INDEX_URL).then((response) => {
    if (!response.ok) throw new Error(`search index: ${response.status}`)
    return response.json() as Promise<DocEntries>
  })
  indexPromise.catch(() => {
    indexPromise = null
  })
  return indexPromise
}

const pageHref = (slug: string): string => `${import.meta.env.BASE_URL}docs/${slug}/`

interface Instance {
  prefix: string
  input: HTMLInputElement
  list: HTMLUListElement
  status: HTMLElement
  query: string
  phase: 'idle' | 'loading' | 'ready' | 'error'
  results: DocResult[]
  total: number
  highlight: string | null
  urlTimer: number | undefined
}

function statusText(inst: Instance): string {
  if (inst.phase === 'loading') return 'Searching…'
  if (inst.phase === 'error') return 'The search index could not be loaded.'
  return docSearchStatus(inst.query, inst.results.length, inst.total)
}

/** The text with its matched runs lit - Marked's shape (DocsSearch.tsx:35),
    built from DOM nodes rather than JSX. */
function appendMarked(el: HTMLElement, text: string, marks: DocResult['marks']): void {
  let at = 0
  for (const mark of marks) {
    if (mark.start > at) el.appendChild(document.createTextNode(text.slice(at, mark.start)))
    const markEl = document.createElement('mark')
    markEl.textContent = text.slice(mark.start, mark.end)
    el.appendChild(markEl)
    at = mark.end
  }
  if (at < text.length) el.appendChild(document.createTextNode(text.slice(at)))
}

function renderRow(inst: Instance, result: DocResult): HTMLLIElement {
  const id = resultId(result)
  const isActive = id === inst.highlight

  const li = document.createElement('li')
  li.id = `${inst.prefix}-${id}`
  li.setAttribute('role', 'option')
  li.setAttribute('aria-selected', String(isActive))
  li.className = `site-docs-search-row${isActive ? ' is-active' : ''}`

  const a = document.createElement('a')
  a.href = docTarget(result, new URLSearchParams(location.search), inst.query, pageHref)
  a.tabIndex = -1
  // The press must not take focus off the field - keepDocHighlight's reason
  // (DocsSearch.tsx:159) applies here too.
  a.addEventListener('mousedown', (e) => e.preventDefault())

  const title = document.createElement('span')
  title.className = 'site-docs-search-title'
  title.textContent = result.title
  a.appendChild(title)

  if (result.heading !== null) {
    const heading = document.createElement('span')
    heading.className = 'site-docs-search-heading'
    heading.textContent = result.heading
    a.appendChild(heading)
  }

  const snippet = document.createElement('span')
  snippet.className = 'site-docs-search-snippet'
  appendMarked(snippet, result.snippet, result.marks)
  a.appendChild(snippet)

  li.appendChild(a)
  return li
}

function paint(inst: Instance): void {
  const open = inst.query.trim() !== ''
  inst.input.setAttribute('aria-expanded', String(open))
  if (open && inst.highlight !== null) inst.input.setAttribute('aria-activedescendant', `${inst.prefix}-${inst.highlight}`)
  else inst.input.removeAttribute('aria-activedescendant')

  inst.list.hidden = !open
  inst.list.replaceChildren(...inst.results.map((result) => renderRow(inst, result)))

  inst.status.textContent = statusText(inst)
}

/** Runs (or re-runs) the search for `query`, against the index once it has
    arrived - synchronously for an empty query, since that needs no index at
    all. A result that lands after the field has moved on to a different query
    is dropped, so a slow first fetch can never overwrite a later keystroke. */
function search(inst: Instance, query: string): void {
  inst.query = query

  if (query.trim() === '') {
    inst.phase = 'idle'
    inst.results = []
    inst.total = 0
    inst.highlight = null
    paint(inst)
    return
  }

  inst.phase = 'loading'
  paint(inst)

  loadIndex().then(
    (entries) => {
      if (inst.input.value !== query) return
      const { results, total } = searchDocs(query, entries)
      inst.phase = 'ready'
      inst.results = results
      inst.total = total
      inst.highlight = keepDocHighlight(results, inst.highlight)
      paint(inst)
    },
    () => {
      if (inst.input.value !== query) return
      inst.phase = 'error'
      inst.results = []
      inst.total = 0
      inst.highlight = null
      paint(inst)
    },
  )
}

function writeQuery(q: string): void {
  const next = withDocQuery(new URLSearchParams(location.search), q)
  const qs = next.toString()
  history.replaceState(history.state, '', `${location.pathname}${qs ? `?${qs}` : ''}${location.hash}`)
}

function scheduleUrlWrite(inst: Instance): void {
  window.clearTimeout(inst.urlTimer)
  inst.urlTimer = window.setTimeout(() => writeQuery(inst.input.value), DEBOUNCE_MS)
}

function onKeyDown(inst: Instance, e: KeyboardEvent): void {
  if (e.key === 'ArrowDown') {
    e.preventDefault() // so the caret does not jump to the end of the field
    inst.highlight = moveDocHighlight(inst.results, inst.highlight, 1)
    paint(inst)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    inst.highlight = moveDocHighlight(inst.results, inst.highlight, -1)
    paint(inst)
  } else if (e.key === 'Enter') {
    e.preventDefault()
    if (inst.highlight === null) return
    document.getElementById(`${inst.prefix}-${inst.highlight}`)?.querySelector('a')?.click()
  } else if (e.key === 'Escape') {
    // type="text", not type="search": Escape inside a search input clears it
    // in WebKit and Blink before the keydown reaches us (OmniBar.tsx:155).
    e.preventDefault()
    inst.input.value = ''
    scheduleUrlWrite(inst)
    search(inst, '')
  }
}

function mount(nav: Element, prefix: string): void {
  const wrapper = document.createElement('div')
  wrapper.className = 'site-docs-search'

  const input = document.createElement('input')
  input.type = 'text'
  input.className = 'site-docs-search-input'
  input.autocomplete = 'off'
  input.autocapitalize = 'off'
  input.spellcheck = false
  input.setAttribute('aria-label', 'Search docs')
  input.setAttribute('role', 'combobox')
  input.setAttribute('aria-expanded', 'false')
  input.setAttribute('aria-autocomplete', 'list')
  input.setAttribute('aria-controls', `${prefix}-list`)

  const list = document.createElement('ul')
  list.className = 'site-docs-search-list'
  list.id = `${prefix}-list`
  list.setAttribute('role', 'listbox')
  list.setAttribute('aria-label', 'Docs search results')
  list.hidden = true

  const status = document.createElement('span')
  status.className = 'site-docs-search-status'
  status.setAttribute('aria-live', 'polite')

  wrapper.append(input, list, status)
  nav.prepend(wrapper)

  const inst: Instance = {
    prefix,
    input,
    list,
    status,
    query: '',
    phase: 'idle',
    results: [],
    total: 0,
    highlight: null,
    urlTimer: undefined,
  }

  input.addEventListener('input', () => {
    inst.highlight = null
    scheduleUrlWrite(inst)
    search(inst, input.value)
  })
  input.addEventListener('keydown', (e) => onKeyDown(inst, e))

  const initialQuery = new URLSearchParams(location.search).get('q') ?? ''
  input.value = initialQuery
  search(inst, initialQuery)
}

const desk = document.querySelector('.site-docs-sidebar')
if (desk) mount(desk, 'site-docs-search-desk')

const phoneNav = document.querySelector('.site-docs-sidebar-phone nav')
if (phoneNav) mount(phoneNav, 'site-docs-search-phone')
