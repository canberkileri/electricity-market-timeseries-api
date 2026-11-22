# Architecture decisions

The main design decisions, in a short ADR format: decision, why, what was rejected, and consequences. In a team repository I would keep one file per decision, so each one can be reviewed and superseded on its own. For this assignment they are collected on one page.

Background on the domain is in [domain-notes.md](domain-notes.md), and the exact API behaviour in [api.md](api.md).

---

## 1. Clean Architecture with four thin projects

**Decision:** Four projects: Domain (entities, value objects, rules; no dependencies), Application (use cases, DTOs, validation, `ITimeSeriesRepository`), Infrastructure (repository implementation) and Api (controllers, error mapping, OpenAPI). Dependencies point inward only.

**Why:** The API is small, but the domain has real rules (alignment, flow direction, overlap). Separate projects let the compiler enforce that the rules never depend on ASP.NET Core or storage, and make them testable on their own.

**Rejected:** one project with folders (boundaries are only conventions); CQRS with MediatR and AutoMapper (too much indirection for five endpoints, and both now need commercial licences).

**Consequences:** mapping between DTOs and domain objects is written by hand: a little more code, but explicit. No generic repository and no extra layers.

## 2. A series is a header plus start, resolution and values

**Decision:** A series has a header (`type`, `biddingZone`, `toBiddingZone` for flows, `unit`, `resolution`) and data as `start` plus an ordered list of `values`, one per period. `end` is calculated as `start + values.Count * resolution`. Responses also return `points` with explicit timestamps.

**Why:** This is the ENTSO-E model in a simpler form: a point's time is `start + (position - 1) * resolution`. Gaps and duplicate timestamps are impossible by design, so validation stays small.

**Rejected:** a list of `{ timestamp, value }` in requests (needs checks for duplicates, order and spacing); one record per point (loses the idea of a series).

**Consequences:** missing values cannot be represented yet; nullable values would add that without changing the shape. Mapping to ENTSO-E / CIM documents later is straightforward.

## 3. UTC everywhere, half-open intervals

**Decision:** All timestamps are UTC; a non-UTC offset is rejected with `400`. `start` must be aligned to the resolution. All intervals are half-open `[from, to)`.

**Why:** A local CET/CEST day has 92, 96 or 100 quarter-hours, and one hour repeats in October; UTC removes that ambiguity. With half-open intervals, adjacent periods join without gaps or double counting. ENTSO-E uses the same convention, written as `[02:00, 02:30[` ([ENTSO-E time series guide](https://eepublicdownloads.entsoe.eu/clean-documents/EDI/Library/Introduction_of_different_time_series_v1.4.1.pdf)).

**Rejected:** accepting any offset and converting (two representations of one instant); local market time (23 and 25 hour days); closed intervals (a boundary point belongs to two periods).

**Consequences:** overlap and clipping are one rule: `a.Start < b.End && b.Start < a.End`. Converting to local time is a presentation concern for clients.

## 4. In-memory storage behind a repository interface

**Decision:** `ITimeSeriesRepository` lives in Application; `InMemoryTimeSeriesRepository` in Infrastructure uses a `ConcurrentDictionary<Guid, TimeSeries>`. Development starts with seed data.

**Why:** The brief allows it, and reviewers can run and test the API with only the .NET SDK.

**Rejected:** SQLite (EF Core and migrations for little gain); SQL Server or MongoDB in Docker (more setup than the task needs).

**Consequences:** data is lost on restart and only one instance can run. Moving to a database is a new repository and one DI registration; domain, use cases and API stay the same. In production I would use SQL Server: a series table, a points table keyed on `(SeriesId, Position)`, and an index on `(BiddingZone, Type, Start)` for search. MongoDB fits if series are always read whole, with one document per series per day to stay under the 16 MB document limit.

## 5. Reject overlapping series with the same identity

**Decision:** A series is identified by type, bidding zone, to-zone and resolution. A create or update that overlaps an existing series with the same identity returns `409 Conflict`. Adjacent series are allowed.

**Why:** Consumers should never have to choose between two values for the same zone and time. Corrections go through `PUT` on the existing series.

**Rejected:** latest wins (hidden data loss); automatic merging (surprising, hard to audit).

**Consequences:** the identity is fixed once a series exists: an update changes its period and values, never what it describes, so all versions of an id stay comparable. The overlap check and the write must be atomic: a lock in memory, a transaction or exclusion constraint in a database. Market data is often revised, so keeping revisions is a natural next step.

## 6. Optimistic concurrency with ETag and If-Match

**Decision:** Each series has a `version`, starting at 1 and increased on every update, returned as the `ETag`. `PUT` and `DELETE` accept an optional `If-Match`; if it does not match, the API returns `412 Precondition Failed` and changes nothing.

**Why:** Prevents lost updates when two clients edit the same series, using standard HTTP and without holding locks between requests.

**Rejected:** last write wins (silent lost updates); pessimistic locking (lock management and timeouts, a poor fit for stateless REST).

**Consequences:** clients re-read and retry on `412`; simple clients can still update without the header. Making `If-Match` mandatory (`428 Precondition Required`) is a small change if stricter rules are preferred. In a database, `version` maps to a row version.

## 7. Problem Details for all errors, results for business outcomes

**Decision:** Every error is RFC 9457 Problem Details (`AddProblemDetails()`). Validation errors are `400` with an `errors` entry per field. Not found, conflict and version mismatch are returned by use cases as an `OperationResult` and mapped to `404`, `409` and `412` in one place in the Api layer. Unexpected exceptions go to the built-in `UseExceptionHandler()`, which returns a generic `500` Problem Details response and logs the exception server-side; a custom `IExceptionHandler` would add code without changing the response.

**Why:** One error shape for every endpoint, possible outcomes visible in method signatures, and no stack traces leaked to clients.

**Rejected:** a custom error format (clients learn something new); exceptions for every error (exceptions as normal control flow); a validation library such as FluentValidation (the same rules would be written twice, in a validator and in the domain).

**Consequences:** validation has two layers and no library. ASP.NET Core model validation checks the request shape (required fields, known enum names). `TimeSeries.Create` checks the business rules and returns every broken rule at once as a `Result`; its constructor is private, so no path can create an invalid series. Exceptions are left for programming errors. Every Problem Details response includes a `traceId`, so a client error can be matched with server logs.
