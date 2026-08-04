# Coding Standards

These are C#-level conventions for this codebase, distinct from the
architectural rules in [AGENTS.md](../AGENTS.md) (which govern how components
relate to each other) and the domain vocabulary in
[domain-model.md](domain-model.md) (which governs what things are called).

## Exceptions are not control flow

Reserve `Exception`-derived types for conditions that indicate a bug or an
environment/infrastructure failure: a caller violated an API's contract (e.g.
tried to change an immutable field), an external dependency is unreachable, a
precondition that should never be false is false. Do not throw or catch
exceptions to signal an outcome a normal, well-formed caller can trigger
through legitimate use - a validation failure, a duplicate/conflict, "not
found," a business rule violation. Model those as part of the method's return
type instead, typically a C# `union` of the success type and one or more
failure types (see [Model outcomes as union types](#model-outcomes-as-union-types)
below).

**Why:** An exception's possible types and meanings are not part of a method's
signature, so understanding what a method can fail with means reading its
implementation (and everything it calls) rather than its declaration. A caller
that forgets to catch one specific exception type doesn't fail to compile - it
fails at run time, in production, usually as a generic error page or an
unhandled fault. A return type that names every outcome is checked by the
compiler: an exhaustive `switch` over it won't compile if a new case is added
and a caller doesn't handle it. This is deliberately closer to Java's checked
exceptions in spirit (the method's declared surface tells you what can go
wrong) without the exception-based control flow.

**How to apply:**
- Before adding a `throw` for something a UI action or API caller can
  legitimately trigger, ask whether it belongs in the return type instead.
- A method that already returns `Task<T>` and can fail in an anticipated way
  should return `Task<TResult>` where `TResult` is a union of `T` and the
  failure case(s), not `Task<T>` with a `throw`.
- Genuine contract violations (calling code passing something the API
  explicitly disallows, not reachable through the normal UI) can still throw
  `ArgumentException`/`InvalidOperationException` - those indicate a bug in
  the *calling* code, not a result to present to a user.
- There is no standing exception to this rule for a "lower layer" (e.g. a
  repository wrapping a database SDK) that throws internally and expects
  whatever happens to call it today to catch and translate the exception -
  see [Translate at the external-library boundary](#translate-at-the-external-library-boundary)
  below for where that translation actually belongs. `IInvoiceConfigurationRepository`'s
  `CreateAsync`/`ReplaceAsync` were brought into line with this: they return
  `InvoiceConfigurationWriteResult` (a union) instead of throwing
  `DuplicateInvoiceConfigurationException`/`InvoiceConfigurationConflictException`,
  which no longer exist. Any other class in this codebase still doing the
  "lower layer throws, an upstream caller catches" thing is tech debt to fix
  when touched, not a sanctioned pattern to extend - see
  [#93](https://github.com/omnics/InvoiceManager/issues/93) for the broader
  sweep to find remaining instances.

## Translate at the external-library boundary

When a type or method from an external library (a client SDK, a database
driver, anything outside this codebase) behaves differently from these
standards - throws exceptions for outcomes we make functional decisions on,
or returns a primitive where we have a domain type - put the translation as
close to that library's boundary as possible: in the method of *the class
that directly calls the library*, not one layer further up in whichever class
happens to call that class today.

That class's *public* interface must conform to these standards regardless of
what the library underneath does. Its private/internal methods may still use
the library's own exceptions or raw types freely as implementation
plumbing - the rule applies to the public surface a caller programs against,
not to every line of code.

Two concrete shapes this takes:

- **A library throws for an outcome we act on.** If a client library's method
  throws for something we're not going to let bubble up to top-level error
  handling - i.e. we branch on which exception it was - catch it inside the
  method that calls the library, and return a discriminated union from that
  method instead (see [Model outcomes as union types](#model-outcomes-as-union-types)).
  Do not let the library's exception type escape that method and rely on a
  caller further up to catch it; that caller is not the boundary and has no
  way to know the exception type is part of its contract.
- **A library returns a primitive where we have a domain type.** If a domain
  concept has its own type in this codebase (a typed ID, `Money`, ...) and the
  external library only knows about it as a plain type (e.g. a bare `string`),
  convert to the domain type before it leaves the method that calls the
  library. Do not return the library's primitive and leave the conversion to
  callers.

**Why:** A translation added anywhere other than the direct boundary is an
implicit contract between whichever two classes happen to be adjacent today -
it isn't documented, isn't enforced by the compiler, and silently breaks the
moment a third class starts calling the lower one, or the existing caller is
refactored away. Doing the translation in the class that owns the library
call means every consumer of that class, present and future, gets the
already-translated contract for free.

**How to apply:** When adding or reviewing a class that calls an external
library, check its public methods against both shapes above.
`CosmosInvoiceConfigurationRepository.CreateAsync`/`ReplaceAsync` are a
worked example: a `TransactionalBatchResponse`'s per-operation status codes
are inspected right there and translated into `InvoiceConfigurationWriteResult`
cases (`DuplicateInvoiceConfigurationId`, `InvoiceConfigurationConflict`,
`ValidationSentinelConflict`) before the method returns - no Cosmos exception
or status code crosses the repository's public surface. See
[#93](https://github.com/omnics/InvoiceManager/issues/93) for the sweep to
find other classes that still don't.

## Avoid null to represent absence of a value

Do not use `null` or nullable value types (`T?`) to represent "no value here,"
except where forced by a framework method, serializer, or external dependency.
Use `Option<T>` (this repo's own zero-dependency C# `union` of a value and
`None`, in `InvoiceManager.Core`) for a value that may legitimately be absent.

**Why:** `null` pushes the "is this actually there?" check onto every call
site with no compiler enforcement, and doesn't say anything about *why* a
value might be missing. `Option<T>` makes absence part of the type, and a
`switch` over it won't compile if a case is missed.

**How to apply:** Default to `Option<T>` for any return value, property, or
field that can be legitimately absent. Prefer overloads over optional/nullable
parameters on public methods where that reads more clearly - a case-by-case
judgment call.

## Model outcomes as union types

Where a method's result is genuinely one of several distinct shapes - not just
success/failure but "which kind" - model it as a C# `union` with one case per
outcome, each carrying exactly the data that outcome has. See
`src/InvoiceManager.Core/DueInvoiceProcessingResult.cs` and
`src/InvoiceManager.Core/InvoiceConfigurationMutationResult.cs` for two
worked examples: a multi-outcome processing result, and a
success-or-one-of-several-anticipated-failures result for a mutating service
call (the concrete case that motivated this standard - see the "Exceptions are
not control flow" section above).

This is also how workflow/entity *state* should be modelled: a union with one
case per state, each carrying only the fields valid in that state - never a
status enum plus a bag of independently-optional fields that only make sense
in combination. See
[domain-model.md#invoice-workflow-state](domain-model.md#invoice-workflow-state)
for why (`SavedToOneDrive` with no actual invoice date is a state that should
be unrepresentable, not a state guarded by a runtime check).

**Why:** A flat status-plus-optional-fields shape lets code construct
combinations that don't make sense (active with no OneDrive folder, "found"
with no actual details) and only catches it, if at all, with a scattered
runtime check. A union with one case per valid combination makes the invalid
ones impossible to construct, and an exhaustive `switch` over the cases is
checked by the compiler when a new case is added.

**How to apply:** When a new outcome, state, or provider-specific variant is
needed, add a new union case with whatever payload it requires, rather than
adding a nullable field to an existing shape. Match with exhaustive `switch`
expressions (no discard arm) so a new case forces every consumer to make an
explicit decision, matching the style already used throughout
`InvoiceManager.Core` (e.g. `GenerateExpectedRecordsHttp.cs`).

## Make invalid states unrepresentable with strong typing

Avoid primitive types (`string`, `decimal`) for domain values where a more
specific type exists or is worth introducing - e.g. `Money` (NodaMoney) instead
of `decimal amount, string currencyCode`, or a small typed ID wrapper instead
of a raw `string` (see
[domain-model.md#identifier-types](domain-model.md#identifier-types) for the
`IStringId<TSelf>` pattern used for every ID type in this codebase).

**Why:** Compile-time failures are cheaper than runtime ones. Two adjacent
`string` parameters can be silently swapped at a call site with no compiler
error; two different wrapper types cannot. Centralizing validation and parsing
in the type's constructor/factory means callers don't each have to re-validate.

**How to apply:** When defining a domain model or public interface, prefer a
small wrapper type over a primitive for values with their own validation rules
or meaning (an ID, a currency amount, a slug). Not every string needs a
wrapper - use judgment for genuinely unstructured text.

## Prefer strongly typed models over loose dictionaries

Prefer strongly typed options classes and domain models over passing
`Dictionary<string, object>` or similar loose structures through the
application.

**Why:** A dictionary's shape isn't visible in any signature; the only way to
know what keys it needs is to read every place that reads or writes it.

## Enumerate external failure modes explicitly

When translating an external HTTP API's failures into domain outcomes
(Microsoft Graph, FreeAgent, etc.), enumerate the failure modes explicitly
instead of special-casing only the one you happened to hit first: "not found"
(404) and "malformed/invalid input" (400) are both realistic for a
caller-supplied ID and usually need the same treatment, distinct from auth
failures (401/403) and transient server errors (429/5xx), which should still
propagate as failures rather than being silently treated the same as "not
found."
