# Goop — design notes

Design decisions and the reasoning behind them, recorded as they are made. Each
heading below carries the open question it exists to answer; they are filled in
as the corresponding milestone lands.

## Goals & non-goals

*What is Goop deliberately trying to be, and what is it explicitly refusing to do?*

## Layering

*Why four layers (consumer → C# fluent API → C ABI → C++ core), and what is each one allowed to know about the others?*

## Shape graph ownership (refcounting vs copying)

*Nodes form a DAG, so a subexpression can be shared — why reference counting rather than deep-copying the graph on every combine, and what exactly does "every handle owns one count" mean at each boundary?*

## Memory ownership of buffers

*Who allocates, who fills, and who frees for vertices, indices and batch evaluation — and why the caller-allocates, query-count-then-copy pattern instead of returning native pointers?*

## Error model

*How a C++ exception becomes a `goop_status` and then a .NET exception, and why the message lives in thread-local storage on the native side.*

## Callbacks & cancellation

*How a managed delegate survives a long native call, what `user_data` carries, and why "return nonzero to cancel" beats every other cancellation mechanism across a C ABI.*

## Threading

*What may be called concurrently and what may not — is the refcount atomic, is a single `Shape` safe to evaluate from two threads, and where does the last-error string live?*

## ABI rules

*The constraints listed at the top of `goop.h` — C types only, no exceptions crossing, fixed-width integers, opaque handles, caller-allocated buffers — and what each one costs if broken.*

## Versioning & compatibility

*What `GOOP_ABI_VERSION` promises, what counts as a breaking change, and how a mismatch between `goop.dll` and `Goop.dll` is detected rather than discovered as a crash.*

## Packaging

*How `goop.dll` gets from the CMake output into a NuGet package and then into a consumer's output directory — and why that is different for `net48` and `net8.0`.*

## Testing strategy

*Which properties are tested from C++ against the static core, which are tested from C# through the ABI, which need the packed package — and what a mesh oracle can assert without a reference mesh to compare against.*

## Open questions

*Things not yet decided, with the tradeoff sketched so the decision can be made rather than drifted into.*
