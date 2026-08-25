# Release notes, authored here and published at release

One file per release: `<component>-<version>.json`. The file is written
alongside the code that changed, so it lands in the same PR and gets reviewed
with it. It is **published separately**, when the version actually reaches
users:

```bash
./scripts/changelog.sh publish changelog/server-1.1.0.json
```

Authoring and publishing are deliberately separate steps. At the moment a
feature is written nothing is released yet — the PR is still open — so
publishing then would show readers notes for a build they do not have, and
there is no undo: rows are never deleted.

The database is the source of truth for everything clients display. These files
are only the authoring format, the same way a migration is written in the repo
and applied separately.

## Format

```json
{
  "component": "server",
  "version": "1.1.0",
  "releasedAt": "2026-08-25T12:00:00Z",
  "items": [
    { "changeType": "added", "summaryEn": "…", "summaryEs": "…" },
    { "changeType": "fixed", "summaryEn": "…" }
  ]
}
```

- `component` — `server` here; `web` and `ios` live in their own repos
- `version` — must match what the component actually reports. For the server
  that is `internal/version/version.go`
- `releasedAt` — optional, defaults to now
- `changeType` — `added`, `fixed` or `changed`
- `summaryEs` — optional; Spanish readers fall back to the English text
- Write for a **user**, not a reviewer: what they can now do, not which
  function changed

A version can only be published once — `(component, version)` is unique, so a
second attempt fails loudly rather than listing the release twice.

See `docs/features/changelog.md` in the workspace root for the full spec.
