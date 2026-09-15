namespace Goop.Internal
{
    // ======================================================================
    // MeshSafeHandle - owns a goop_mesh handle.
    //
    // Simpler than ShapeSafeHandle: a mesh is NOT reference counted. It has one
    // owner and is released exactly once. There is no retain, because nothing
    // else on the native side ever holds a mesh.
    // ======================================================================

    // TODO: an internal sealed class MeshSafeHandle deriving from
    //       SafeHandleZeroOrMinusOneIsInvalid, with a private parameterless
    //       constructor and an override of ReleaseHandle() that calls
    //       goop_mesh_release and returns true.
    //
    //       Same rules for ReleaseHandle as in ShapeSafeHandle: no throwing, no
    //       allocating, no locks, no blocking. It can run on the finalizer
    //       thread.
    //
    // TODO: meshes can be large - a 128^3 grid produces a lot of triangles - so
    //       the difference between "released when Dispose is called" and
    //       "released when the finalizer eventually gets round to it" is real
    //       memory pressure. Consider calling GC.AddMemoryPressure with the
    //       approximate native size when the handle is created, and
    //       RemoveMemoryPressure on release, so the GC understands that a small
    //       managed object is holding a large native allocation. Without that
    //       hint the collector has no reason to hurry.
    //
    // TODO: the copy-out functions take the handle plus a pinned caller buffer.
    //       Make sure the P/Invoke declares the parameter as MeshSafeHandle so
    //       the marshaller ref-counts it for the duration of the call, rather
    //       than passing DangerousGetHandle() and hoping nobody disposes it on
    //       another thread mid-copy.
}
