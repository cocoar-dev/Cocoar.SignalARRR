import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import llmstxt from 'vitepress-plugin-llms'

export const baseConfig = defineConfig({
  title: 'Cocoar.SignalARRR',
  description: 'Typed bidirectional RPC over ASP.NET Core SignalR',

  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/logo_light.svg' }],
    ['link', { rel: 'alternate', type: 'text/plain', href: '/llms.txt', title: 'LLM documentation (summary)' }],
    ['link', { rel: 'alternate', type: 'text/plain', href: '/llms-full.txt', title: 'LLM documentation (full)' }],
  ],

  vite: {
    plugins: [llmstxt({
      excludeUnnecessaryFiles: false,
      ignoreFiles: ['changelog.md'],
    })],
  },

  themeConfig: {
    logo: {
      light: '/logo_light.svg',
      dark: '/logo_dark.svg',
    },

    siteTitle: 'Cocoar.SignalARRR v4',

    nav: [
      { text: 'Guide', link: '/guide/getting-started' },
      { text: 'Reference', link: '/reference/packages' },
      { text: 'Roadmap', link: '/roadmap/status' },
      { text: 'Changelog', link: '/changelog' },
      { text: 'LLM Docs', link: '/llms-full.txt', target: '_blank' },
      { text: 'NuGet', link: 'https://www.nuget.org/packages/Cocoar.SignalARRR.Server' },
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Introduction',
          items: [
            { text: 'Getting Started', link: '/guide/getting-started' },
            { text: 'Why SignalARRR?', link: '/guide/why-signalarrr' },
            { text: 'vs. gRPC vs. REST', link: '/guide/comparison' },
          ],
        },
        {
          text: 'Server',
          items: [
            { text: 'Hub Setup', link: '/guide/server/hub-setup' },
            { text: 'Server Methods', link: '/guide/server/server-methods' },
            { text: 'Authorization', link: '/guide/server/authorization' },
            { text: 'Client Manager', link: '/guide/server/client-manager' },
            { text: 'Contract Wire Names', link: '/guide/server/contracts-wire-names' },
            { text: 'Backplane & Clustering', link: '/guide/server/backplane' },
          ],
        },
        {
          text: '.NET Client',
          items: [
            { text: 'Connection Setup', link: '/guide/dotnet-client/connection-setup' },
            { text: 'Typed Methods', link: '/guide/dotnet-client/typed-methods' },
            { text: 'Server-to-Client Handlers', link: '/guide/dotnet-client/server-to-client' },
          ],
        },
        {
          text: 'TypeScript Client',
          items: [
            { text: 'Setup & Usage', link: '/guide/typescript-client/setup' },
            { text: 'Server Method Handlers', link: '/guide/typescript-client/server-methods' },
          ],
        },
        {
          text: 'Swift Client',
          items: [
            { text: 'Setup & Usage', link: '/guide/swift-client/setup' },
            { text: 'Typed Proxies & Server Methods', link: '/guide/swift-client/typed-proxies' },
          ],
        },
        {
          text: 'Item Streaming',
          items: [
            { text: 'Server-to-Client', link: '/guide/streaming/server-to-client' },
            { text: 'Client-to-Server <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/streaming/client-to-server' },
          ],
        },
        {
          text: 'Advanced',
          items: [
            { text: 'HTTP Stream References', link: '/guide/advanced/http-streams' },
            { text: 'Proxy Generation <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/advanced/proxy-generation' },
            { text: 'Cancellation Propagation <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/advanced/cancellation' },
          ],
        },
        {
          text: 'Migration',
          collapsed: true,
          items: [
            { text: 'Migration from v2.x', link: '/guide/migration/from-v2' },
          ],
        },
      ],
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Packages', link: '/reference/packages' },
            { text: 'Client Comparison', link: '/reference/client-comparison' },
            { text: 'API Overview', link: '/reference/api' },
            { text: 'Wire Protocol <span class="badge-adv" title="Advanced topic"></span>', link: '/reference/wire-protocol' },
          ],
        },
      ],
      '/roadmap/': [
        {
          text: 'Roadmap',
          items: [
            { text: 'Feature Status', link: '/roadmap/status' },
            { text: 'Test Coverage', link: '/roadmap/test-coverage' },
          ],
        },
        {
          text: 'Open Items',
          items: [
            { text: 'HTTP Stream References', link: '/roadmap/http-streams' },
            { text: 'MessagePack Protocol', link: '/roadmap/messagepack' },
          ],
        },
      ],
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/cocoar-dev/Cocoar.SignalARRR' },
    ],

    search: {
      provider: 'local',
    },

    footer: {
      message: 'Released under the Apache-2.0 License.',
      copyright: 'Copyright 2025-present Cocoar',
    },
  },

  mermaid: {},

  mermaidPlugin: {
    class: 'mermaid',
  },
})

export default withMermaid(
  defineConfig({
    ...baseConfig,
    themeConfig: {
      ...baseConfig.themeConfig,
      nav: [
        ...(baseConfig.themeConfig?.nav ?? []),
        { text: 'Dev Notes', link: '/dev-notes/' },
      ],
      sidebar: {
        ...(baseConfig.themeConfig?.sidebar ?? {}),
        '/dev-notes/': [
          {
            text: 'Engineering Notes',
            items: [
              { text: 'Overview', link: '/dev-notes/' },
              { text: 'CI Build Bottleneck', link: '/dev-notes/ci/integration-test-server-build-bottleneck' },
            ],
          },
        ],
      },
    },
  }),
)
