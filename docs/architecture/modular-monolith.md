# Secure Client Portal Modular Monolith

The backend should stay as a single deployable application for now, with business capabilities separated into modules inside the existing layered projects:

- `SecureClientPortal.Api`
- `SecureClientPortal.Application`
- `SecureClientPortal.Domain`
- `SecureClientPortal.Infrastructure`

## Modules

The canonical module list is:

- `Auth`
- `UsersRoles`
- `Clients`
- `Assignments`
- `Documents`
- `MonthlyPacks`
- `ReviewQueue`
- `Requests`
- `Notifications`
- `Compliance`
- `AuditLogs`
- `Reports`

## Layer responsibilities

### Api

Module entry points, controllers, request/response shaping, authorization policies, and endpoint composition.

### Application

Use cases, commands, queries, DTOs, validation, and interfaces that coordinate domain behavior.

### Domain

Entities, value objects, policies, domain services, and domain events.

### Infrastructure

Persistence, EF configurations, file storage, external integrations, and concrete service implementations.

## Expected shape inside a module

The folder names do not need to be identical in every module, but each module should generally evolve toward:

- `Api/Modules/<ModuleName>/`
- `Application/Modules/<ModuleName>/`
- `Domain/Modules/<ModuleName>/`
- `Infrastructure/Modules/<ModuleName>/`

When adding new code:

1. Put it in the matching module first.
2. Keep cross-module calls explicit through application interfaces.
3. Avoid direct data access into another module's persistence concerns.
4. Extract to separate services later only when scale or team ownership demands it.

## Transition note

The current solution already contains feature folders such as `Identity`, `FirmManagement`, `Platform`, and existing `Documents`, `Requests`, and `Compliance` areas. Those remain valid while we gradually move new work into the new `Modules` structure.
