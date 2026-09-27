using System;
using System.IO;
using Goop.Internal;

namespace Goop {
    // ======================================================================
    // Mesh - a finished triangle mesh produced by Shape.ToMesh.
    //
    // Owns one MeshSafeHandle. Unlike Shape, a Mesh is NOT reference counted on
    // the native side: it has a single owner and is released exactly once.
    // ======================================================================

    /// <summary>
    /// A triangle mesh of a shape's surface: a list of vertices, and three vertex
    /// indices per triangle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Winding.</b> Every triangle is counter-clockwise when seen from outside
    /// the shape, so <c>(v1 - v0) x (v2 - v0)</c> points outward.
    /// </para>
    /// <para>
    /// <b>A snapshot.</b> A mesh holds no reference to the shape it came from. The
    /// shape can be disposed as soon as the mesh exists.
    /// </para>
    /// <para>
    /// The data stays in native memory until you read it. <see cref="GetVertices"/>
    /// and <see cref="GetIndices"/> copy it into new arrays; the span overloads
    /// copy into memory you already own.
    /// </para>
    /// </remarks>
    public sealed class Mesh : IDisposable {
        private readonly MeshSafeHandle _handle;

        internal Mesh(MeshSafeHandle handle) {
            _handle = handle;

            // Counts are cached: a mesh never changes after it is built. Queried
            // once here, so the properties below cost nothing and cannot fail.
            Errors.ThrowIfError(NativeMethods.goop_mesh_vertex_count(handle, out long vertexCount));
            Errors.ThrowIfError(NativeMethods.goop_mesh_triangle_count(handle, out long triangleCount));

            // .NET arrays are indexed by int. Checking here, once, means every
            // index in the mesh fits in an int too - which is what lets
            // GetIndices hand out int[] instead of the awkward uint[].
            if (vertexCount > int.MaxValue || triangleCount * 3 > int.MaxValue) {
                throw new GoopException((int)GoopStatus.Internal, "The mesh is too large to be read into .NET arrays.");
            }
            VertexCount = (int)vertexCount;
            TriangleCount = (int)triangleCount;
        }

        /// <summary>The native handle, for Goop itself and its tests.</summary>
        internal MeshSafeHandle Handle => _handle;

        /// <summary>The number of vertices.</summary>
        public int VertexCount { get; }

        /// <summary>The number of triangles. The index list holds three per triangle.</summary>
        public int TriangleCount { get; }

        /// <summary>The number of entries in the index list: 3 x <see cref="TriangleCount"/>.</summary>
        public int IndexCount => TriangleCount * 3;

        // -------------------------------------------------------------------
        // Reading the data
        // -------------------------------------------------------------------

        /// <summary>Copies every vertex into a new array.</summary>
        /// <exception cref="ObjectDisposedException">The mesh has been disposed.</exception>
        public Vec3[] GetVertices() {
            var vertices = new Vec3[VertexCount];
            CopyVertices(vertices);
            return vertices;
        }

        /// <summary>
        /// Copies every triangle's three vertex indices into a new array.
        /// Triangle <c>t</c> is <c>indices[3t]</c>, <c>indices[3t + 1]</c>,
        /// <c>indices[3t + 2]</c>.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The mesh has been disposed.</exception>
        public int[] GetIndices() {
            var indices = new int[IndexCount];
            CopyIndices(indices);
            return indices;
        }

        /// <summary>Copies every vertex into memory you already own.</summary>
        /// <param name="destination">Must be exactly <see cref="VertexCount"/> long.</param>
        /// <exception cref="ArgumentException"><paramref name="destination"/> is the wrong length.</exception>
        /// <exception cref="ObjectDisposedException">The mesh has been disposed.</exception>
        public unsafe void CopyVertices(Span<Vec3> destination) {
            if (destination.Length != VertexCount) {
                throw new ArgumentException(
                    "destination must be exactly VertexCount (" + VertexCount + ") long, but was " +
                    destination.Length + ".", nameof(destination));
            }
            ThrowIfDisposed();

            int status;
            fixed (Vec3* p = destination) {
                status = NativeMethods.goop_mesh_copy_vertices(_handle, p, destination.Length);
            }
            Errors.ThrowIfError(status);
        }

        /// <summary>Copies every triangle's vertex indices into memory you already own.</summary>
        /// <param name="destination">Must be exactly <see cref="IndexCount"/> long.</param>
        /// <exception cref="ArgumentException"><paramref name="destination"/> is the wrong length.</exception>
        /// <exception cref="ObjectDisposedException">The mesh has been disposed.</exception>
        public unsafe void CopyIndices(Span<int> destination) {
            if (destination.Length != IndexCount) {
                throw new ArgumentException(
                    "destination must be exactly IndexCount (" + IndexCount + ") long, but was " +
                    destination.Length + ".", nameof(destination));
            }
            ThrowIfDisposed();

            int status;
            // The native side writes uint32. An int and a uint are the same 4
            // bytes, and the constructor proved every index is below
            // int.MaxValue, so the top bit is never set and the bits mean the same
            // number either way. So the int buffer is handed over as uint*.
            fixed (int* p = destination) {
                status = NativeMethods.goop_mesh_copy_indices(_handle, (uint*)p, destination.Length);
            }
            Errors.ThrowIfError(status);
        }

        // -------------------------------------------------------------------
        // Export
        // -------------------------------------------------------------------

        /// <summary>Writes the mesh to a file as binary STL.</summary>
        /// <param name="path">The file to create or overwrite.</param>
        /// <exception cref="ObjectDisposedException">The mesh has been disposed.</exception>
        public void SaveStl(string path) => StlWriter.Write(this, path);

        /// <summary>Writes the mesh to a stream as binary STL.</summary>
        /// <param name="stream">A writable stream. Left open afterwards.</param>
        /// <exception cref="ObjectDisposedException">The mesh has been disposed.</exception>
        public void SaveStl(Stream stream) => StlWriter.Write(this, stream);

        // -------------------------------------------------------------------
        // Lifetime
        // -------------------------------------------------------------------

        /// <summary>
        /// Frees the native mesh. Arrays already copied out are unaffected.
        /// Calling Dispose more than once is harmless.
        /// </summary>
        public void Dispose() => _handle.Dispose();

        private void ThrowIfDisposed() {
            if (_handle.IsClosed) {
                throw new ObjectDisposedException(nameof(Mesh));
            }
        }
    }
}
