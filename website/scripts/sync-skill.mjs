// Generates the Agent Skill that ships inside the Cocoar.SignalARRR.Server package from the docs.
//
// The skill used to be a hand-maintained summary of the documentation. It drifted: it still
// described 4.0 when 5.1 shipped, and it linked reference files that no longer existed. Now the
// documentation is the only source. Every page under website/guide and website/reference becomes
// a reference file of the skill, and the page's frontmatter description — the same line that
// feeds llms.txt on the docs site — becomes its entry in the skill's index. The only hand-written
// part is website/skill/SKILL.header.md: name, trigger description, package table, the gotchas.
//
//   node website/scripts/sync-skill.mjs          regenerate skills/signalarrr/
//   node website/scripts/sync-skill.mjs --check  exit 1 if skills/signalarrr/ is out of date (CI)
//
// Agent Skills are progressive: an agent loads only the skill's name and description at startup,
// SKILL.md when a task matches, and a reference file when the index says it is relevant. That is
// why the index carries the descriptions and why the pages stay separate files.

import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const websiteDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repoDir = path.resolve(websiteDir, '..')
const skillDir = path.join(repoDir, 'skills', 'signalarrr')
const headerFile = path.join(websiteDir, 'skill', 'SKILL.header.md')
const docsBaseUrl = 'https://docs.cocoar.dev/signalarrr'
const check = process.argv.includes('--check')

// Mirrors the site's sidebar. Listing pages explicitly is deliberate: a docs page that is neither
// here nor in EXCLUDED fails the run, so a new page cannot silently stay out of the skill.
const SECTIONS = [
  { title: 'Introduction', pages: ['guide/getting-started.md'] },
  {
    title: 'Server',
    pages: [
      'guide/server/hub-setup.md',
      'guide/server/server-methods.md',
      'guide/server/authorization.md',
      'guide/server/client-manager.md',
      'guide/server/contracts-wire-names.md',
      'guide/server/backplane.md',
    ],
  },
  {
    title: '.NET client',
    pages: ['guide/dotnet-client/connection-setup.md', 'guide/dotnet-client/typed-methods.md', 'guide/dotnet-client/server-to-client.md'],
  },
  { title: 'TypeScript client', pages: ['guide/typescript-client/setup.md', 'guide/typescript-client/server-methods.md'] },
  { title: 'Swift client', pages: ['guide/swift-client/setup.md', 'guide/swift-client/typed-proxies.md'] },
  { title: 'Item streaming', pages: ['guide/streaming/server-to-client.md', 'guide/streaming/client-to-server.md'] },
  { title: 'Advanced', pages: ['guide/advanced/http-streams.md', 'guide/advanced/proxy-generation.md', 'guide/advanced/cancellation.md'] },
  { title: 'Migration', pages: ['guide/migration/from-v4.md', 'guide/migration/from-v2.md'] },
  {
    title: 'Reference',
    pages: ['reference/packages.md', 'reference/client-comparison.md', 'reference/api.md', 'reference/wire-protocol.md'],
  },
]

// Pages that exist for readers deciding whether to use the library, not for an agent already using it.
const EXCLUDED = new Set(['guide/why-signalarrr.md', 'guide/comparison.md'])

// --- Collect the pages ---

const listed = new Set(SECTIONS.flatMap(s => s.pages))
const onDisk = [...walk(path.join(websiteDir, 'guide')), ...walk(path.join(websiteDir, 'reference'))]
  .map(f => path.relative(websiteDir, f).split(path.sep).join('/'))
  .filter(f => f.endsWith('.md'))

const unaccounted = onDisk.filter(f => !listed.has(f) && !EXCLUDED.has(f))
const missing = [...listed].filter(f => !onDisk.includes(f))
if (unaccounted.length || missing.length) {
  if (unaccounted.length) console.error(`sync-skill: pages not in SECTIONS or EXCLUDED: ${unaccounted.join(', ')}`)
  if (missing.length) console.error(`sync-skill: pages in SECTIONS that do not exist: ${missing.join(', ')}`)
  process.exit(1)
}

const pages = new Map()
for (const rel of listed) {
  // Line endings are normalized on the way in: the pages are edited on Windows and Linux alike,
  // and the generated files must not differ by that. Git normalizes them on the way out.
  const raw = normalizeNewlines(fs.readFileSync(path.join(websiteDir, rel), 'utf8'))
  const { description, body } = splitFrontmatter(raw, rel)
  const title = (body.match(/^# (.+)$/m) || [])[1]
  if (!title) fail(`${rel}: no H1 title`)
  pages.set(rel, { rel, title, description, body })
}

// --- Generate ---

const output = new Map() // skill-relative path -> content

for (const page of pages.values()) {
  const target = `references/${page.rel}`
  output.set(target, renderReference(page, target))
}

output.set('SKILL.md', renderSkill())

// --- Write or check ---

if (check) {
  const problems = []
  for (const [rel, content] of output) {
    const file = path.join(skillDir, rel)
    if (!fs.existsSync(file)) problems.push(`missing: ${rel}`)
    else if (normalizeNewlines(fs.readFileSync(file, 'utf8')) !== content) problems.push(`outdated: ${rel}`)
  }
  for (const existing of walk(skillDir)) {
    const rel = path.relative(skillDir, existing).split(path.sep).join('/')
    if (!output.has(rel)) problems.push(`stale: ${rel}`)
  }
  if (problems.length) {
    console.error('sync-skill: skills/signalarrr is out of date with the docs. Run `node website/scripts/sync-skill.mjs` and commit the result.')
    for (const p of problems) console.error(`  ${p}`)
    process.exit(1)
  }
  console.log(`sync-skill: skills/signalarrr is up to date (${output.size} files)`)
} else {
  fs.rmSync(skillDir, { recursive: true, force: true })
  for (const [rel, content] of output) {
    const file = path.join(skillDir, rel)
    fs.mkdirSync(path.dirname(file), { recursive: true })
    fs.writeFileSync(file, content)
  }
  console.log(`sync-skill: wrote ${output.size} files to skills/signalarrr`)
}

// --- Rendering ---

function renderSkill() {
  if (!fs.existsSync(headerFile)) fail(`${headerFile} not found`)
  const header = normalizeNewlines(fs.readFileSync(headerFile, 'utf8')).replace(/\s+$/, '')

  const lines = [
    header,
    '',
    '<!-- Everything below is generated by website/scripts/sync-skill.mjs from the docs frontmatter. Edit the docs, not this file. -->',
    '',
    '## Reference documentation',
    '',
    'Each file under `references/` is one page of the documentation, copied verbatim. Read the one',
    'whose description matches the task; they are independent of each other.',
    '',
  ]

  for (const section of SECTIONS) {
    lines.push(`### ${section.title}`, '')
    for (const rel of section.pages) {
      const page = pages.get(rel)
      lines.push(`- [${page.title}](references/${rel}) — ${page.description}`)
    }
    lines.push('')
  }

  lines.push(`The same content is online at ${docsBaseUrl}/ (index for LLMs: ${docsBaseUrl}/llms.txt).`, '')
  return lines.join('\n')
}

function renderReference(page, target) {
  const banner =
    `<!-- Generated from website/${page.rel} by website/scripts/sync-skill.mjs. Do not edit; edit the docs page. -->\n\n`

  let body = rewriteLinks(page.body, target)
  body = rewriteContainers(body)
  return banner + body.replace(/\s+$/, '') + '\n'
}

// `](/guide/x/y)`, `](/guide/x/y#frag)`, `](/reference/x.md)` → a relative link to the sibling
// reference file when the page is in the skill, the docs URL otherwise.
function rewriteLinks(body, target) {
  return body.replace(/\]\((\/[^)\s#]+)(#[^)\s]*)?\)/g, (match, sitePath, fragment = '') => {
    const rel = sitePath.replace(/^\//, '').replace(/\.(md|html)$/, '') + '.md'
    if (pages.has(rel)) {
      const from = path.posix.dirname(target)
      let relative = path.posix.relative(from, `references/${rel}`)
      if (!relative.startsWith('.')) relative = `./${relative}`
      return `](${relative}${fragment})`
    }
    return `](${docsBaseUrl}${sitePath.replace(/\.md$/, '')}.html${fragment})`
  })
}

// VitePress custom containers. `::: code-group` only groups the code blocks that follow, whose
// info strings already carry their labels, so the container lines simply go. The admonitions
// become a block quote with the kind in bold, which reads the same without the plugin.
function rewriteContainers(body) {
  const lines = body.split('\n')
  const out = []
  let admonition = null

  for (const line of lines) {
    const open = line.match(/^::: ?(code-group|info|tip|warning|danger|details)\s*(.*)$/)
    if (open) {
      const [, kind, title] = open
      if (kind === 'code-group') {
        admonition = 'code-group'
      } else {
        admonition = kind
        const label = kind.charAt(0).toUpperCase() + kind.slice(1)
        out.push(`> **${label}${title ? `: ${title.trim()}` : ''}**`, '>')
      }
      continue
    }
    if (line.trim() === ':::' && admonition) {
      admonition = null
      continue
    }
    if (admonition && admonition !== 'code-group') {
      out.push(line.trim() === '' ? '>' : `> ${line}`)
      continue
    }
    out.push(line)
  }

  return out.join('\n')
}

// --- Helpers ---

function splitFrontmatter(raw, rel) {
  const m = raw.match(/^---\n([\s\S]*?)\n---\n/)
  if (!m) fail(`${rel}: no frontmatter — every docs page needs a description`)
  const desc = m[1].match(/^description:\s*(.+)$/m)
  if (!desc) fail(`${rel}: frontmatter has no description`)
  let description = desc[1].trim()
  if ((description.startsWith('"') && description.endsWith('"')) || (description.startsWith("'") && description.endsWith("'"))) {
    description = description.slice(1, -1).replace(/\\"/g, '"').replace(/\\\\/g, '\\')
  }
  return { description, body: raw.slice(m[0].length).replace(/^\n+/, '') }
}

// The pages are edited on Windows and Linux alike; the generated files must not differ by that.
// Git normalizes line endings on the way out (.gitattributes: text=auto).
function normalizeNewlines(text) {
  return text.replace(/\r\n/g, '\n')
}

function walk(dir) {
  if (!fs.existsSync(dir)) return []
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(dir, entry.name)
    return entry.isDirectory() ? walk(full) : [full]
  })
}

function fail(message) {
  console.error(`sync-skill: ${message}`)
  process.exit(1)
}
