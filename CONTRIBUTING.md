# Contributing

Thanks for your interest in contributing to Cocoar.SignalARRR!

## Getting started
- Fork the repository and create a feature branch.
- Use .NET 10 SDK. You can verify with `dotnet --info`.
- Run the test suite locally before opening a PR.

## Coding guidelines
- Prefer small, focused PRs.
- Keep public APIs stable; consider extension methods for additive APIs.
- Add or update tests for all behavior changes.
- Follow existing code style and conventions.

## Testing
- Run all tests with `dotnet test` from the `src/` directory.
- Ensure all tests pass before submitting.
- Add tests for new features and bug fixes.

## Commit/PR
- Reference related issues in the PR description.
- Describe user-facing changes and migration notes if any.
- Use conventional commit messages (feat:, fix:, docs:, etc.).

## Releasing
Releases — stable and prerelease alike — are made by publishing a GitHub Release. Nothing else
triggers publication: a bare `git push --tags` does not, and there is no manual publish workflow.

1. Stamp the date on the version's heading in `CHANGELOG.md` and `website/changelog.md`.
2. Publish a GitHub Release on `develop` with a tag of the form `vX.Y.Z`, or `vX.Y.Z-suffix` for a
   prerelease (`v5.1.0-beta.1`, `v5.1.0-rc.2`).
3. For a prerelease, tick **Set as a pre-release**.

The tag is the single source of the version — it is what the NuGet and npm packages are built with.
The pre-release flag is what decides the rest, and the workflow refuses to publish if the two
disagree, in either direction:

| | stable | prerelease |
|---|---|---|
| npm dist-tag | `latest` | `prerelease` |
| Docs version on Shelf | `vX.Y` (the major.minor line) | `vX.Y.Z-suffix` (its own version) |
| NuGet | resolved by default | resolved only when prereleases are allowed |

A prerelease therefore publishes its documentation without overwriting the stable line's, and Shelf
keeps "latest" on the highest stable version, so the preview shows up in the version list without
becoming the default.

Pushes to `develop` publish nothing. CI packs every green build with a GitVersion-derived number and
uploads it as a workflow artifact (7 days), which is the way to try an unreleased state without
putting anything on a public feed.

## License
By contributing, you agree that your contributions will be licensed under the Apache License 2.0.