# Life OS Architecture

## Scope

- Scope is a server-side data isolation boundary.
- Scope is NOT a workspace, organization, tenant, or user-switching feature.
- Current scope types are:
  - Owner
  - Guest
- There is exactly one Owner Scope for the current system.
- Multiple Owner scopes are not part of the current architecture.
- Client must never choose or submit an arbitrary ScopeId.
- The server resolves the current Scope from trusted authentication/session context.

## Owner

- The Owner is an ASP.NET Core Identity user.
- The Owner has one private Owner Scope.
- Owner Scope is associated with the Owner identity.
- Owner data is private and must never be accessible from Guest context.

## Guest

- Guest is anonymous and is NOT an Identity user.
- A Guest session represents one browser/device context.
- Different browsers/devices receive separate Guest sessions and separate Guest scopes.
- Guest identity is established through a server-trusted persistent cookie.
- The client must not use or submit ScopeId as its Guest identity.

## Guest Sandbox Lifecycle

- Anonymous visitors do not automatically receive a sandbox.
- A Guest sandbox is created only when the visitor intentionally enters the demo/testing experience.
- Creating a Guest sandbox creates: GuestSession + Guest Scope.
- Existing valid Guest sessions are reused instead of creating a new sandbox.
- Guest persistence is based on inactivity, tracked through LastActivityAt.
- Expiration/cleanup infrastructure is intentionally deferred until there is a real need.

## Domain Architecture

- Owner and Guest use the same Life OS/domain business logic.
- Do not create separate OwnerFinanceService / GuestFinanceService style implementations.
- Domain logic operates against the server-resolved current Scope.
- Finance and future domains should follow the same isolation model.
