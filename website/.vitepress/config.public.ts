import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import llmstxt from 'vitepress-plugin-llms'
import fs from 'node:fs'
import path from 'node:path'
import { baseConfig } from './config'

export default withMermaid(defineConfig({
  ...baseConfig,
  vite: {
    plugins: [llmstxt({
      excludeUnnecessaryFiles: false,
      ignoreFiles: ['changelog.md', 'dev-notes/**'],
    })],
  },
  srcExclude: ['dev-notes/**'],
  buildEnd(siteConfig) {
    const devNotesDir = path.join(siteConfig.outDir, 'dev-notes')
    if (fs.existsSync(devNotesDir)) {
      fs.rmSync(devNotesDir, { recursive: true, force: true })
    }
    const devNotesHtml = path.join(siteConfig.outDir, 'dev-notes.html')
    if (fs.existsSync(devNotesHtml)) {
      fs.unlinkSync(devNotesHtml)
    }
    const devNotesMd = path.join(siteConfig.outDir, 'dev-notes.md')
    if (fs.existsSync(devNotesMd)) {
      fs.unlinkSync(devNotesMd)
    }
  },
}))
