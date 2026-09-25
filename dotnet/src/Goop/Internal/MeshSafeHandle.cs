using Microsoft.Win32.SafeHandles;

namespace Goop.Internal {
    // ======================================================================
    // MeshSafeHandle - owns a goop_mesh handle.
    //
    // Simpler than ShapeSafeHandle: a mesh is NOT reference counted. It has one
    // owner and is released exactly once. There is no retain, because nothing
    // else on the native side ever holds a mesh.
    // ======================================================================

    // TODO (M4c): meshes can be large - a 256^3 grid produces a lot of
    //       triangles - so the difference between "released when Dispose is
    //       called" and "released when the finalizer eventually gets round to
    //       it" is real memory pressure. Consider GC.AddMemoryPressure with the
    //       approximate native size when the handle is created, and
    //       RemoveMemoryPressure on release, so the GC understands that a small
    //       managed object is holding a large native allocation.

    /// <summary>
    /// Owns one native <c>goop_mesh</c> and destroys it exactly once.
    /// </summary>
    internal sealed class MeshSafeHandle : SafeHandleZeroOrMinusOneIsInvalid {
        // Private: only the marshaller creates these, when a P/Invoke returns
        // one through an `out MeshSafeHandle` parameter.
        private MeshSafeHandle() : base(ownsHandle: true) { }

        // Same rules as ShapeSafeHandle.ReleaseHandle: never throw, never
        // allocate, never lock - it may run on the finalizer thread. And the
        // same reason goop_mesh_release takes the raw IntPtr: this handle is
        // already closed by the time we get here.
        protected override bool ReleaseHandle() {
            NativeMethods.goop_mesh_release(handle);
            return true;
        }
    }
}
