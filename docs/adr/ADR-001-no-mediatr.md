# ADR-001 — No MediatR; a hand-written dispatcher instead

**Status** Accepted · 2026-09-04

## Context

The reference architecture (`vertical_slice/design_3_clean_architecture`) assumes MediatR
for request dispatch, while noting that MediatR moved to a commercial licence in early
2025 and that "nothing in this design depends on MediatR specifically".

Checked against nuget.org on 2026-09-04: the current line is **14.2.0**, and every
version from 13 onward is RPL-1.5 with a paid tier above a revenue threshold. RPL-1.5 is
a reciprocal public licence — usable for open-source work, but it places conditions on a
project that will be published and defended, and those conditions have to be understood
rather than assumed.

## Decision

Implement the mediator pattern directly. `Calametra.Application` owns:

- `IRequest<TResponse>`, `ICommand`, `ICommand<T>`, `IQuery<T>`
- `IRequestHandler<,>`, `ICommandHandler<>`, `IQueryHandler<,>`
- `IPipelineBehavior<,>` and `RequestHandlerDelegate<T>`
- `Dispatcher`, using a cached closed-generic wrapper per request type

Roughly 90 lines in `Abstractions/Messaging/IDispatcher.cs` and `Messaging/Dispatcher.cs`.

## Alternatives considered

**MediatR 14 under RPL-1.5.** Workable, but adds a licence obligation to a public
academic project for functionality we can write in an afternoon.

**`Mediator` (martinothamar), MIT, source-generated.** Genuinely good — faster than
MediatR and AOT-friendly. Rejected only because it introduces a source-generator
dependency whose .NET 10 compatibility we would have to track, in exchange for solving a
problem we do not have. Worth revisiting if the pipeline ever becomes hot.

## Consequences

Good:
- No licence ambiguity for a published thesis project.
- No source-generator or reflection-library compatibility risk on .NET 10.
- The dispatch mechanism is fully explainable in a defence — useful when the panel asks
  how the architecture works.
- The behaviour pipeline is ours, so adding a caching or auditing behaviour needs no
  knowledge of a third-party extension model.

Bad:
- Roughly 90 lines to maintain that a library would have maintained for us.
- No notification/publish support. Not needed: nothing in the design broadcasts.
- Handler registration is a reflection scan at startup rather than a source-generated
  table. Measurable only at cold start, and paid once.

## Verification

`Calametra.ArchitectureTests.SliceConventionTests.Handlers_ShouldBeInternalAndSealed`
confirms handlers stay unreachable except through the dispatcher.
