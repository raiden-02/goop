using System;
using Goop.Internal;

namespace Goop {
    // ======================================================================
    // Shape - a node in the SDF expression graph, seen from managed code.
    //
    // Owns exactly one ShapeSafeHandle and nothing else. All the geometry lives
    // in C++; this class is a name, a lifetime, and a fluent grammar.
    // ======================================================================

    // TODO: the remaining fluent transforms, same ownership rules as Translate:
    //
    //         Rotate(Vec3 axis, double angleRadians)
    //         Scale(double factor)
    //         Twist(double amountPerUnit)
    //
    //       Decide whether angles are radians or degrees and be consistent.
    //       Radians matches the native side; degrees is friendlier at a call
    //       site. If both, name them differently (RotateDegrees) rather than
    //       overloading on meaning.
    //
    // TODO (M6): progress and cancellation for ToMesh:
    //         ToMesh(min, max, resolution, Action<double>? progress = null,
    //                CancellationToken cancellationToken = default)
    //       Both funnel into the single native progress function - see
    //       Internal/ProgressCallback.cs for the delegate lifetime problem that
    //       makes this the trickiest method in the library.
    //
    // TODO: automatic bounds. ToMesh needs the caller to say where the shape
    //       is. Once nodes can report a bounding box, add ToMesh(resolution)
    //       that picks the box itself.

    /// <summary>
    /// A signed distance field: a shape that can report, for any point, how far
    /// that point is from its surface. Negative inside, zero on the surface,
    /// positive outside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Create one with a factory on <see cref="Sdf"/>, move it with
    /// <see cref="Translate(Vec3)"/>, and combine shapes with
    /// <see cref="Union"/>, <see cref="Intersect"/>, <see cref="Subtract"/> and
    /// <see cref="SmoothUnion"/>. Every one of these returns a NEW shape and
    /// leaves its inputs untouched and still usable.
    /// </para>
    /// <para>
    /// <b>Ownership.</b> A combined shape keeps its operands alive for as long as
    /// it lives, so you may dispose an operand as soon as you have combined it.
    /// Shapes can be shared: the same shape may appear in any number of
    /// expressions.
    /// </para>
    /// <para>
    /// <b>Temporaries in a chain.</b> In
    /// <c>Sdf.Sphere(1).Union(Sdf.Sphere(2))</c> the two inner spheres are never
    /// assigned to a variable, so nothing disposes them. That is safe - the
    /// union holds its own references - but their native memory is only
    /// released when the garbage collector finalizes them, not immediately. For
    /// deterministic cleanup, hold every piece in its own <c>using</c>.
    /// </para>
    /// <para>
    /// Evaluating the same shape from several threads at once is safe:
    /// evaluation only reads. Disposing a shape while another thread is still
    /// using it is not.
    /// </para>
    /// </remarks>
    public sealed class Shape : IDisposable {
        // The ONLY field. Everything about the shape lives on the native side;
        // this handle is how we reach it, and disposing it releases our one
        // reference to the native node.
        private readonly ShapeSafeHandle _handle;

        // Matches the signature shared by goop_shape_union/intersect/subtract,
        // so Combine() can take any of them as a parameter. Same idea as
        // make_binary<Node> in api.cpp: one body, the operation passed in.
        private delegate int BinaryOp(ShapeSafeHandle a, ShapeSafeHandle b, out ShapeSafeHandle result);

        // Internal: users cannot write `new Shape(...)`. The only ways to get a
        // Shape are Sdf factories and combinators, so every Shape is guaranteed
        // to wrap a real native node.
        internal Shape(ShapeSafeHandle handle) {
            _handle = handle;
        }

        /// <summary>The native handle, for other Goop types (e.g. meshing).</summary>
        internal ShapeSafeHandle Handle {
            get {
                ThrowIfDisposed();
                return _handle;
            }
        }

        /// <summary>
        /// Turns the result of a native constructor into a <see cref="Shape"/>, or
        /// throws. The one place every creating call funnels through.
        /// </summary>
        internal static Shape FromNative(int status, ShapeSafeHandle handle) {
            if (status != 0) {
                // On failure the handle is invalid (native set it to NULL), so
                // this Dispose never reaches native code. Done anyway so the
                // SafeHandle object is closed now rather than at finalization.
                handle.Dispose();
                Errors.ThrowIfError(status);
            }
            return new Shape(handle);
        }

        // -------------------------------------------------------------------
        // CSG
        // -------------------------------------------------------------------

        /// <summary>
        /// Everything inside this shape OR <paramref name="other"/>.
        /// </summary>
        /// <param name="other">The shape to add. Not modified, and still usable afterwards.</param>
        /// <returns>A new shape. The caller owns it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">Either shape has been disposed.</exception>
        public Shape Union(Shape other) => Combine(other, NativeMethods.goop_shape_union);

        /// <summary>
        /// Only what is inside BOTH this shape and <paramref name="other"/>.
        /// </summary>
        /// <param name="other">The shape to intersect with. Not modified, and still usable afterwards.</param>
        /// <returns>A new shape. The caller owns it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">Either shape has been disposed.</exception>
        public Shape Intersect(Shape other) => Combine(other, NativeMethods.goop_shape_intersect);

        /// <summary>
        /// This shape with <paramref name="other"/> carved out of it. Order
        /// matters: <c>a.Subtract(b)</c> removes b from a.
        /// </summary>
        /// <param name="other">The shape to cut away. Not modified, and still usable afterwards.</param>
        /// <returns>A new shape. The caller owns it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">Either shape has been disposed.</exception>
        public Shape Subtract(Shape other) => Combine(other, NativeMethods.goop_shape_subtract);

        /// <summary>
        /// Like <see cref="Union"/>, but the two surfaces melt into each other
        /// where they meet instead of forming a sharp crease.
        /// </summary>
        /// <param name="other">The shape to blend with. Not modified, and still usable afterwards.</param>
        /// <param name="blendRadius">
        /// How far the blend reaches, in the same units as the shapes. Larger
        /// values give a wider, softer join. Away from the join the result is
        /// exactly a plain union; as this approaches zero, the whole result
        /// approaches a plain union. Must be greater than zero.
        /// </param>
        /// <returns>A new shape. The caller owns it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="blendRadius"/> is zero, negative, or NaN.
        /// </exception>
        /// <exception cref="ObjectDisposedException">Either shape has been disposed.</exception>
        public Shape SmoothUnion(Shape other, double blendRadius) {
            if (other == null) {
                throw new ArgumentNullException(nameof(other));
            }
            if (!(blendRadius > 0.0)) {
                throw new ArgumentOutOfRangeException(nameof(blendRadius), blendRadius,
                    "Blend radius must be greater than zero.");
            }
            ThrowIfDisposed();
            other.ThrowIfDisposed();

            int status = NativeMethods.goop_shape_smooth_union(_handle, other._handle, blendRadius,
                out ShapeSafeHandle result);
            return FromNative(status, result);
        }

        private Shape Combine(Shape other, BinaryOp op) {
            if (other == null) {
                throw new ArgumentNullException(nameof(other));
            }
            ThrowIfDisposed();
            other.ThrowIfDisposed();

            int status = op(_handle, other._handle, out ShapeSafeHandle result);
            return FromNative(status, result);
        }

        // -------------------------------------------------------------------
        // Transforms
        // -------------------------------------------------------------------

        /// <summary>
        /// This shape moved by <paramref name="offset"/>.
        /// </summary>
        /// <param name="offset">How far to move it along each axis. Every component must be finite.</param>
        /// <returns>
        /// A new shape. The caller owns it. This shape is not modified and is
        /// still usable afterwards.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="offset"/> is NaN or infinite.</exception>
        /// <exception cref="ObjectDisposedException">The shape has been disposed.</exception>
        public Shape Translate(Vec3 offset) {
            if (!IsFinite(offset.X) || !IsFinite(offset.Y) || !IsFinite(offset.Z)) {
                throw new ArgumentOutOfRangeException(nameof(offset), offset,
                    "Every component of the offset must be a finite number.");
            }
            ThrowIfDisposed();

            int status = NativeMethods.goop_shape_translate(_handle, offset, out ShapeSafeHandle result);
            return FromNative(status, result);
        }

        /// <summary>
        /// This shape moved by (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).
        /// </summary>
        /// <inheritdoc cref="Translate(Vec3)"/>
        public Shape Translate(double x, double y, double z) => Translate(new Vec3(x, y, z));

        // double.IsFinite only exists on .NET Core 2.1+, not on net48, so it is
        // spelled out here once instead of behind #if.
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        // -------------------------------------------------------------------
        // Evaluation
        // -------------------------------------------------------------------

        /// <summary>Signed distance from one point to the surface.</summary>
        /// <param name="point">The point to measure from.</param>
        /// <returns>Negative inside, zero on the surface, positive outside.</returns>
        /// <remarks>
        /// Each call crosses into native code once. For more than a handful of
        /// points use <see cref="Evaluate(ReadOnlySpan{Vec3}, Span{double})"/>,
        /// which crosses once for all of them.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The shape has been disposed.</exception>
        public double Evaluate(Vec3 point) {
            ThrowIfDisposed();
            int status = NativeMethods.goop_shape_eval(_handle, point, out double distance);
            Errors.ThrowIfError(status);
            return distance;
        }

        /// <summary>
        /// Signed distances for many points, in a single call into native code.
        /// </summary>
        /// <param name="points">The points to measure from. Only read.</param>
        /// <param name="distances">
        /// Receives one distance per point. Must be exactly as long as
        /// <paramref name="points"/>. Allocated by you, filled by this method.
        /// </param>
        /// <exception cref="ArgumentException">The two spans differ in length.</exception>
        /// <exception cref="ObjectDisposedException">The shape has been disposed.</exception>
        public unsafe void Evaluate(ReadOnlySpan<Vec3> points, Span<double> distances) {
            if (distances.Length != points.Length) {
                throw new ArgumentException(
                    "distances must be the same length as points (" + points.Length + "), but was " +
                    distances.Length + ".", nameof(distances));
            }
            ThrowIfDisposed();

            int status;
            // Pin both spans so the GC cannot move the memory while C++ reads and
            // writes it. An empty span pins to a null pointer, which the native
            // side accepts when count is 0.
            fixed (Vec3* p = points)
            fixed (double* d = distances) {
                status = NativeMethods.goop_shape_eval_batch(_handle, p, d, points.Length);
            }
            Errors.ThrowIfError(status);
        }

        // -------------------------------------------------------------------
        // Meshing
        // -------------------------------------------------------------------

        /// <summary>Smallest resolution <see cref="ToMesh"/> accepts.</summary>
        public const int MinResolution = 2;

        /// <summary>
        /// Largest resolution <see cref="ToMesh"/> accepts. Memory grows with the
        /// CUBE of the resolution; this is roughly 1 GB.
        /// </summary>
        public const int MaxResolution = 512;

        /// <summary>
        /// Builds a triangle mesh of this shape's surface inside the box
        /// [<paramref name="min"/>, <paramref name="max"/>].
        /// </summary>
        /// <param name="min">Minimum corner of the box. Must be finite.</param>
        /// <param name="max">
        /// Maximum corner. Must be finite and greater than <paramref name="min"/> on
        /// every axis.
        /// </param>
        /// <param name="resolution">
        /// Grid cells along the LONGEST side of the box: higher is smoother and
        /// slower. 64 is a quick draft, 128 is good, 256 is fine detail. Between
        /// <see cref="MinResolution"/> and <see cref="MaxResolution"/>.
        /// </param>
        /// <returns>A new mesh. The caller owns it. It does not depend on this shape.</returns>
        /// <remarks>
        /// The box must contain the whole shape. Any part of the surface outside
        /// it is cut off, leaving a hole in the mesh. A box that misses the shape
        /// entirely is not an error: the mesh is simply empty.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="resolution"/> is out of range.</exception>
        /// <exception cref="ArgumentException">The box is not finite, or is empty on some axis.</exception>
        /// <exception cref="ObjectDisposedException">The shape has been disposed.</exception>
        public Mesh ToMesh(Vec3 min, Vec3 max, int resolution) {
            if (resolution < MinResolution || resolution > MaxResolution) {
                throw new ArgumentOutOfRangeException(nameof(resolution), resolution,
                    "Resolution must be between " + MinResolution + " and " + MaxResolution + ".");
            }
            if (!IsFinite(min.X) || !IsFinite(min.Y) || !IsFinite(min.Z) ||
                !IsFinite(max.X) || !IsFinite(max.Y) || !IsFinite(max.Z)) {
                throw new ArgumentException("Every coordinate of the bounds must be a finite number.");
            }
            if (!(max.X > min.X) || !(max.Y > min.Y) || !(max.Z > min.Z)) {
                throw new ArgumentException("max must be greater than min on every axis.", nameof(max));
            }
            ThrowIfDisposed();

            // IntPtr.Zero, IntPtr.Zero: no progress callback yet (M6).
            int status = NativeMethods.goop_shape_to_mesh(_handle, min, max, resolution,
                IntPtr.Zero, IntPtr.Zero, out MeshSafeHandle handle);
            if (status != 0) {
                handle.Dispose();
                Errors.ThrowIfError(status);
            }

            // If the Mesh constructor throws (it queries the counts), nothing else
            // owns the handle yet - so release it here rather than leaving it to
            // the finalizer.
            try {
                return new Mesh(handle);
            } catch {
                handle.Dispose();
                throw;
            }
        }

        // -------------------------------------------------------------------
        // Lifetime
        // -------------------------------------------------------------------

        /// <summary>
        /// Releases this shape's reference to the native node. Shapes built from
        /// this one are unaffected: they hold their own references. Calling
        /// Dispose more than once is harmless.
        /// </summary>
        public void Dispose() {
            // No finalizer on Shape: ShapeSafeHandle already has a critical one.
            // Adding a second would only duplicate it and get the order wrong.
            _handle.Dispose();
        }

        // The marshaller would also refuse a closed handle, but its message talks
        // about "safe handles". This one names the thing the user actually holds.
        private void ThrowIfDisposed() {
            if (_handle.IsClosed) {
                throw new ObjectDisposedException(nameof(Shape));
            }
        }
    }
}
