// Copies the repository's CHANGELOG.md to website/changelog.md before the docs are built.
//
// These two used to be maintained separately, and they drifted: the site's copy sat empty through an
// entire release cycle while the real one filled up, so the published changelog showed none of it.
// A copy step costs nothing and removes the whole class of problem.

import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const websiteDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const source = path.join(websiteDir, '..', 'CHANGELOG.md')
const target = path.join(websiteDir, 'changelog.md')

if (!fs.existsSync(source)) {
  console.error(`sync-changelog: ${source} not found`)
  process.exit(1)
}

const content = fs.readFileSync(source, 'utf8')
const previous = fs.existsSync(target) ? fs.readFileSync(target, 'utf8') : null

if (previous === content) {
  console.log('sync-changelog: already up to date')
} else {
  fs.writeFileSync(target, content)
  console.log('sync-changelog: changelog.md updated from CHANGELOG.md')
}
