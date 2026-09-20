namespace Goop.Internal {
    // ======================================================================
    // ShapeSafeHandle - the managed half of the native refcount on goop_shape.
    //
    // This is the piece that makes Shape safe to use. A raw IntPtr would leak
    // whenever an exception unwound past a release, and could be used after
    // free; a SafeHandle is tracked by the runtime, has a critical finalizer
    // that runs even during process shutdown or an aborted thread, and blocks
    // release while a P/Invoke using it is still in flight.
    // ======================================================================

    // TODO: an internal sealed class ShapeSafeHandle deriving from
    //       SafeHandleZeroOrMinusOneIsInvalid (the native constructors return
    //       null on failure, so "zero is invalid" is the right base) with:
    //
    //         * a private parameterless constructor, so the marshaller can
    //           construct one when a P/Invoke returns this type directly. Making
    //           it private keeps everyone else on the explicit path.
    //
    //         * an override of ReleaseHandle() that calls goop_shape_release and
    //           returns true. Rules for that method, all of them load-bearing:
    //           it must not throw, must not allocate, must not take locks, and
    //           must not call anything that could block - it can run on the
    //           finalizer thread during shutdown.
    //
    // TODO: think carefully about where the RETAIN happens.
    //
    //       The native graph is reference counted: goop_shape_smooth_union
    //       retains its operands, so the result owns a count on each. The
    //       managed rule that keeps this straight is "every ShapeSafeHandle owns
    //       exactly one count, and releases exactly one count". A constructor
    //       that hands back a fresh node gives you a count you already own. If
    //       a handle is ever created from a pointer obtained some other way, it
    //       must retain first.
    //
    //       Write this rule down in docs/design.md, because off-by-one
    //       refcounting is the single most likely bug in this whole project and
    //       it presents as a crash somewhere completely unrelated.
    //
    // TODO: use DangerousAddRef / DangerousRelease (or better, let the
    //       marshaller do it by declaring the P/Invoke parameter as the
    //       SafeHandle type) when passing a handle to a native call. That is
    //       what prevents another thread from disposing the Shape mid-call.
    //
    // TODO (test): a reference-counting test that builds a shared subexpression,
    //       disposes the operand, and then still evaluates the combined shape.
    //       That is the scenario a missing retain destroys.
}
