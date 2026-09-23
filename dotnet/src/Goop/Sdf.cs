using System;
using Goop.Internal;

namespace Goop {
    // ======================================================================
    // Sdf - the static entry point. Every Goop expression starts here.
    //
    // This class exists purely so that the reading order of the fluent API
    // matches the reading order of the sentence:
    //
    //     using var shape = Sdf.Sphere(1.0)
    //                          .SmoothUnion(Sdf.Sphere(2.0), 0.3);
    //
    // Primitives are static factory methods here; everything that COMBINES or
    // TRANSFORMS shapes is an instance method on Shape. That split is the whole
    // design: factories start a chain, instance methods continue it.
    // ======================================================================

    // TODO: the remaining primitives, once the native side has them:
    //
    //         Box(double sizeX, double sizeY, double sizeZ)
    //         Torus(double majorRadius, double minorRadius)
    //         Cylinder(double radius, double height)
    //
    //       Same shape as Sphere below: validate in C#, call the native
    //       constructor, wrap the handle with Shape.FromNative.
    //
    // TODO: decide the Box convention and document it in the XML docs: are the
    //       three arguments FULL extents or HALF extents? The native distance
    //       function wants half extents. Pick one, say so, and convert in
    //       exactly one place - this is the kind of ambiguity that silently
    //       produces shapes twice the intended size.
    //
    // TODO (stretch): Gyroid(). Not a real SDF at all - an implicit surface
    //       whose field is only a rough distance bound - which makes it a good
    //       test of whether the mesher copes with fields that lie.

    /// <summary>
    /// Factory methods for primitive shapes. Every Goop expression starts here.
    /// </summary>
    /// <example>
    /// <code>
    /// using var blob = Sdf.Sphere(1.0).SmoothUnion(Sdf.Sphere(2.0), 0.5);
    /// double d = blob.Evaluate(new Vec3(0, 0, 0));
    /// </code>
    /// </example>
    public static class Sdf {
        /// <summary>Creates a sphere centred at the origin.</summary>
        /// <param name="radius">The radius. Must be greater than zero.</param>
        /// <returns>A new shape. The caller owns it and should dispose it.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="radius"/> is zero, negative, or NaN.
        /// </exception>
        public static Shape Sphere(double radius) {
            // Checked here first so the caller gets ArgumentOutOfRangeException
            // with ParamName = "radius" and the bad value. The native side
            // validates again (defence in depth: C# is not the only caller).
            // !(x > 0) rather than (x <= 0), so NaN is rejected too.
            if (!(radius > 0.0)) {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be greater than zero.");
            }

            int status = NativeMethods.goop_shape_sphere(radius, out ShapeSafeHandle handle);
            return Shape.FromNative(status, handle);
        }
    }
}
