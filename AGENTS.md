## Agent skills

### Issue tracker

Issues and specs for this repo live as GitHub issues, operated via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage roles map 1:1 to the label strings `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: one `CONTEXT.md` plus `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Security

Security guidance, check items, and the four hard rules live in `docs/security/README.md` (decisions in ADR-0019–0023). Before merging: new endpoints must state who may call them (writing no `RequireAuthorization()` means public), internal endpoints must live under `/internal/*`, and new cross-service calls must carry a service-identity token over an encrypted channel.
