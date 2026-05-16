#!/usr/bin/env node
import fs from 'node:fs'
import path from 'node:path'

const outDirArg = process.argv[2]
if (!outDirArg) {
  console.error('Usage: strip-dev-notes.mjs <build-output-dir>')
  process.exit(1)
}

const outDir = path.resolve(outDirArg)
if (!fs.existsSync(outDir)) {
  console.warn(`strip-dev-notes: target dir does not exist: ${outDir}`)
  process.exit(0)
}

const pathsToRemove = [
  path.join(outDir, 'dev-notes'),
  path.join(outDir, 'dev-notes.html'),
  path.join(outDir, 'dev-notes.md'),
]

for (const target of pathsToRemove) {
  if (!fs.existsSync(target)) {
    continue
  }

  const stat = fs.statSync(target)
  if (stat.isDirectory()) {
    fs.rmSync(target, { recursive: true, force: true })
    console.log(`strip-dev-notes: removed ${target}`)
  } else {
    fs.unlinkSync(target)
    console.log(`strip-dev-notes: removed ${target}`)
  }
}

sanitizeLlmsTxt(path.join(outDir, 'llms.txt'))
sanitizeLlmsFullTxt(path.join(outDir, 'llms-full.txt'))

function sanitizeLlmsTxt(filePath) {
  if (!fs.existsSync(filePath)) {
    return
  }

  const content = fs.readFileSync(filePath, 'utf8')
  const titleUrlRegex = /^- \[(.+?)\]\(\/dev-notes\b/gm
  const devNotesTitles = new Set()
  let match
  while ((match = titleUrlRegex.exec(content)) !== null) {
    devNotesTitles.add(match[1].trim())
  }

  const sanitized = content
    .split('\n')
    .filter(line => !line.includes('(/dev-notes'))
    .join('\n')
    .replace(/\n### Engineering Notes\n(?:\n)+/g, '\n')

  if (sanitized !== content) {
    fs.writeFileSync(filePath, sanitized, 'utf8')
    console.log(`strip-dev-notes: sanitized llms.txt (${devNotesTitles.size} dev-notes entries removed)`)
  }
}

function sanitizeLlmsFullTxt(filePath) {
  if (!fs.existsSync(filePath)) {
    return
  }

  const content = fs.readFileSync(filePath, 'utf8').replace(/\r\n/g, '\n')
  const pages = content.split(/(?=^---\nurl: )/m)
  const keptPages = pages.filter(page => !/^---\nurl: \/dev-notes(?:\.md|\/)/.test(page))
  const removedCount = pages.length - keptPages.length

  if (removedCount > 0) {
    fs.writeFileSync(filePath, keptPages.join(''), 'utf8')
    console.log(`strip-dev-notes: sanitized llms-full.txt (${removedCount} dev-notes pages removed)`)
  }
}
