'use strict';
/**
 * Runs at `prepack` (before npm pack / npm publish).
 * Copies the repo-root skills/ and docs/ into the package directory so they
 * are included in the published tarball.
 * Safe to run outside the repo — exits silently if sources are not found.
 */
const { existsSync, mkdirSync, cpSync, rmSync } = require('node:fs');
const { join } = require('node:path');

const pkgRoot   = join(__dirname, '..');
const repoRoot  = join(__dirname, '../../..');

const skillsSrc = join(repoRoot, 'skills', 'signalarrr');
const docsSrc   = join(repoRoot, 'docs');
const skillsDst = join(pkgRoot,  'skills', 'signalarrr');
const docsDst   = join(pkgRoot,  'docs');

if (!existsSync(skillsSrc) || !existsSync(docsSrc)) {
  console.log('[signalarrr] prepack: source assets not found — skipping copy');
  process.exit(0);
}

// Clean and copy
if (existsSync(skillsDst)) rmSync(skillsDst, { recursive: true, force: true });
if (existsSync(docsDst))   rmSync(docsDst,   { recursive: true, force: true });

mkdirSync(skillsDst, { recursive: true });
mkdirSync(docsDst,   { recursive: true });

cpSync(skillsSrc, skillsDst, { recursive: true });
cpSync(docsSrc,   docsDst,   { recursive: true });

console.log('[signalarrr] prepack: assets copied');
