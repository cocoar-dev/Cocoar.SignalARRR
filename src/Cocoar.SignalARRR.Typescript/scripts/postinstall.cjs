'use strict';
/**
 * Runs automatically after `npm install` in a consuming project.
 * Mirrors the MSBuild buildTransitive targets behaviour:
 *   - Only copies if .claude / .github already exists (no folder pollution)
 *   - Copies SKILL.md  → {agentDir}/skills/signalarrr/
 *   - Copies docs/     → {agentDir}/skills/signalarrr/references/
 */
const { existsSync, mkdirSync, cpSync } = require('node:fs');
const { join } = require('node:path');

const pkgRoot = join(__dirname, '..');

// INIT_CWD is set by npm to the directory where `npm install` was invoked.
const projectRoot = process.env.INIT_CWD ?? process.env.npm_config_local_prefix;

// Guard: not a consuming project install (e.g. `npm install` run inside this package itself)
if (!projectRoot || projectRoot === pkgRoot) process.exit(0);

const skillsSrc = join(pkgRoot, 'skills', 'signalarrr');
const docsSrc   = join(pkgRoot, 'docs');

// Guard: assets were not bundled (dev build without prepack)
if (!existsSync(skillsSrc)) process.exit(0);

function copySkills(agentDir) {
  if (!existsSync(agentDir)) return;

  const skillsDst = join(agentDir, 'skills', 'signalarrr');
  const refsDst   = join(agentDir, 'skills', 'signalarrr', 'references');

  mkdirSync(skillsDst, { recursive: true });
  mkdirSync(refsDst,   { recursive: true });

  cpSync(skillsSrc, skillsDst, { recursive: true, force: true });
  cpSync(docsSrc,   refsDst,   { recursive: true, force: true });

  console.log(`[signalarrr] Skills copied to ${skillsDst}`);
}

copySkills(join(projectRoot, '.claude'));
copySkills(join(projectRoot, '.github'));
