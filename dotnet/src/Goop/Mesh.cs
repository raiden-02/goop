namespace Goop
{
    // ======================================================================
    // Mesh - a finished triangle mesh produced by Shape.ToMesh.
    //
    // Owns one MeshSafeHandle. Unlike Shape, a Mesh is NOT reference counted on
    // the native side: it has a single owner and is released exactly once.
    // ======================================================================

    // TODO: a public sealed class Mesh : IDisposable wrapping a MeshSafeHandle.
    //
    // TODO: VertexCount and TriangleCount properties over
    //       goop_mesh_vertex_count / goop_mesh_triangle_count. Cheap enough to
    //       call repeatedly, but caching them after the first call is fine
    //       because a Mesh is immutable once produced.
    //
    // TODO: the copy-out accessors. The native contract is "query the count,
    //       allocate, then fill a buffer you own" (ABI rule 5 in goop.h), so
    //       the managed side allocates the array and passes a pinned pointer:
    //
    //         Vec3[] GetVertices()
    //         int[]  GetIndices()      // or uint[] - see below
    //
    //       plus span-taking overloads for callers that want to reuse buffers
    //       across many meshes instead of allocating per call.
    //
    // TODO: decide int[] versus uint[] for indices. The native side uses
    //       uint32_t. uint[] is the honest mapping but is awkward in C# (not
    //       CLS-compliant, painful to index with). int[] is friendlier and
    //       overflows only past 2^31 vertices, which this mesher will never
    //       reach. Pick one, write down why in docs/design.md, and do the
    //       reinterpretation in exactly one place.
    //
    // TODO: SaveStl(string path) - a thin call through to StlWriter, present
    //       here because mesh.SaveStl("blob.stl") is the line from the README
    //       and the API should read the way the README promises.
    //
    // TODO: think about whether Mesh should expose triangles as a structured
    //       view (a Triangle struct, or an enumerable of vertex triples) rather
    //       than two parallel arrays. Nicer to consume; allocates more. Probably
    //       worth offering both, with the raw arrays as the fast path.
    //
    // TODO: Dispose that disposes the SafeHandle. No finalizer here - the
    //       SafeHandle already has one.
}
