using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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

    internal sealed class ShapeSafeHandle : SafeHandleZeroOrMinusOneIsInvalid {
        private ShapeSafeHandle() : base(ownsHandle: true) { }

        // Must not throw, allocate, or block. This can run on the finalizer thread.
        protected override bool ReleaseHandle() {
            NativeMethods.goop_shape_release(handle);
            return true;
        }
    }
}
