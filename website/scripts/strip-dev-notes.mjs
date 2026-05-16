import fs from 'node:fs'
import path from 'node:path'

const outDirArg = process.argv[2]
if (!outDirArg) {
  console.error('Usage: node scripts/strip-dev-notes.mjs <outDir>')
  process.exit(1)
}

const outDir = path.resolve(process.cwd(), outDirArg)

const pathsToRemove = [
  path.join(outDir, 'dev-notes'),
  path.join(outDir, 'dev-notes.html'),
  path.join(outDir, 'llms.txt'),
  path.join(outDir, 'llms-full.txt'),
]

for (const target of pathsToRemove) {
  if (!fs.existsSync(target)) {
    continue
  }

  const stat = fs.statSync(target)
  if (stat.isDirectory()) {
    fs.rmSync(target, { recursive: true, force: true })
  } else {
    fs.unlinkSync(target)
  }
}
