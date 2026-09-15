namespace Goop
{
    // ======================================================================
    // Shape - a node in the SDF expression graph, seen from managed code.
    //
    // Owns exactly one ShapeSafeHandle and nothing else. All the geometry lives
    // in C++; this class is a name, a lifetime, and a fluent grammar.
    // ======================================================================

    // TODO: a public sealed class Shape : IDisposable wrapping a
    //       ShapeSafeHandle. Sealed because subclassing it would imply managed
    //       polymorphism over what is really a native graph.
    //
    // TODO: the fluent combinators, each returning a NEW Shape and leaving both
    //       operands valid and usable:
    //
    //         Union(Shape other)
    //         Subtract(Shape other)
    //         Intersect(Shape other)
    //         SmoothUnion(Shape other, double blendRadius)
    //
    //       "Leaving both operands valid" is the part that needs care. The
    //       native graph is a DAG and is reference counted, so a Shape passed
    //       into SmoothUnion is retained by the result, not consumed by it. The
    //       caller can keep using it, and can dispose it independently:
    //
    //           using var ball = Sdf.Sphere(1.0);
    //           using var a = ball.SmoothUnion(Sdf.Box(1, 1, 1), 0.3);
    //           using var b = ball.Union(Sdf.Torus(1.0, 0.25));   // still fine
    //
    //       Getting this wrong in either direction is a classic interop bug:
    //       forget a retain and you get use-after-free, forget a release and you
    //       leak the whole subgraph.
    //
    // TODO: the fluent transforms, same rules:
    //
    //         Translate(Vec3 offset) / Translate(double x, double y, double z)
    //         Rotate(Vec3 axis, double angleRadians)
    //         Scale(double factor)
    //         Twist(double amountPerUnit)
    //
    //       Decide whether angles are radians or degrees and be consistent.
    //       Radians matches the native side; degrees is friendlier at a call
    //       site. If both, name them differently (RotateDegrees) rather than
    //       overloading on meaning.
    //
    // TODO: Evaluate(Vec3 point) for a single distance, and
    //       Evaluate(ReadOnlySpan<Vec3> points, Span<double> distances) - or an
    //       array-based overload on net48 - for the batch path over
    //       goop_shape_eval_batch. The batch form is the one that matters: a
    //       P/Invoke per point costs more than the arithmetic it performs.
    //
    // TODO: ToMesh(int resolution, Action<double>? progress = null,
    //                CancellationToken cancellationToken = default)
    //       returning a Mesh. The progress callback and the cancellation token
    //       both funnel into the single native progress function - see
    //       Internal/ProgressCallback.cs for the delegate lifetime problem that
    //       makes this the trickiest method in the library.
    //
    // TODO: Dispose. Just dispose the SafeHandle and let it do the real work; do
    //       not add a finalizer here, because SafeHandle already has one and
    //       duplicating it gets the ordering wrong. Guard the public methods
    //       against use-after-dispose with a clear ObjectDisposedException.
    //
    // TODO: XML doc comments, especially on SmoothUnion - blendRadius is the
    //       single most interesting number in the API and deserves an
    //       explanation of what happens as it approaches zero.
}
