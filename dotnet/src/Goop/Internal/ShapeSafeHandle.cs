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

    // TODO: write the retain rule in docs/design.md. The code already follows
    //       it: every ShapeSafeHandle owns one count, CSG retains its operands,
    //       and a fresh node from a constructor is a count the caller already
    //       owns.

    internal sealed class ShapeSafeHandle : SafeHandleZeroOrMinusOneIsInvalid {
        private ShapeSafeHandle() : base(ownsHandle: true) { }

        // Must not throw, allocate, or block. This can run on the finalizer thread.
        protected override bool ReleaseHandle() {
            NativeMethods.goop_shape_release(handle);
            return true;
        }
    }
}
