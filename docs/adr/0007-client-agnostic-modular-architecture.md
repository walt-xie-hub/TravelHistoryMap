# Client-agnostic modular architecture

## Status

Accepted

## Context

The web client had a `map` feature containing travel records, map rendering, image access, and travel API calls. The backend already separates the User and Travel bounded contexts into independent services, but the API layer of the Travel service also accessed repositories and file storage directly. Production routing did not expose the Travel service through the same public host as the User service.

The same backend contract must support the browser client and future mobile clients. A client should not need to know internal service addresses or infrastructure implementations.

## Decision

- Organize the Angular client by business capability: `auth`, `profile`, and `travel`; keep `core` for cross-cutting runtime concerns, `shared` for business-neutral UI, and `layout` for the authenticated shell.
- Treat map rendering as a presentation capability of the `travel` feature, not as the name of the business module.
- Keep feature API clients in `data-access` and expose relative public API paths in production.
- Keep User and Travel as separate backend bounded contexts. Do not share domain entities or application DTOs between them.
- Define media storage and media use cases in the Travel Application layer. Infrastructure implements storage ports; the API layer maps HTTP input and output only.
- Route `/api/travels/*` to travel-service and other `/api/*` paths to user-service. Clients use one public API host.
- Keep authentication based on bearer tokens and user ownership derived from the authenticated identity, so the contract is usable by non-browser clients.

## Consequences

The web and future mobile clients share stable resource URLs, ownership rules, pagination, and error semantics without depending on deployment topology. The gateway and deployment configuration must provision both backend services. Replacing local file storage with object storage changes Infrastructure configuration and implementation, not API endpoints or client modules.
