import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import fs from 'node:fs'
import path from 'node:path'
import { baseConfig } from './config'

export default withMermaid(defineConfig({
  ...baseConfig,
  vite: {},
  head: (baseConfig.head ?? []).filter((entry) => entry[0] !== 'link' || entry[1]?.href !== '/llms.txt' && entry[1]?.href !== '/llms-full.txt'),
  srcExclude: ['dev-notes/**'],
  themeConfig: {
    ...baseConfig.themeConfig,
    nav: (baseConfig.themeConfig?.nav ?? []).filter((item) => item.text !== 'LLM Docs'),
  },
  buildEnd(siteConfig) {
    const devNotesDir = path.join(siteConfig.outDir, 'dev-notes')
    if (fs.existsSync(devNotesDir)) {
      fs.rmSync(devNotesDir, { recursive: true, force: true })
    }
    const devNotesHtml = path.join(siteConfig.outDir, 'dev-notes.html')
    if (fs.existsSync(devNotesHtml)) {
      fs.unlinkSync(devNotesHtml)
    }
  },
}))
