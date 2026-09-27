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
