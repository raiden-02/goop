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
        /// <exception cref="GoopException">
        /// The loaded goop_native.dll comes from a different build and speaks a
        /// different ABI version. Checked once, on first use.
        /// </exception>
        public static Shape Sphere(double radius) {
            // Checked here first so the caller gets ArgumentOutOfRangeException
            // with ParamName = "radius" and the bad value. The native side
            // validates again (defence in depth: C# is not the only caller).
            // !(x > 0) rather than (x <= 0), so NaN is rejected too.
            if (!(radius > 0.0)) {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be greater than zero.");
            }

            // Every Shape starts at an Sdf factory, so this is the single place
            // that guarantees the ABI check runs before any real native call.
            // New factories must call it too.
            AbiCheck.EnsureCompatible();

            int status = NativeMethods.goop_shape_sphere(radius, out ShapeSafeHandle handle);
            return Shape.FromNative(status, handle);
        }

        /// <summary>Creates a box centred at the origin.</summary>
        /// <param name="halfExtents">The half-extents of the box. All components must be greater than zero.</param>
        /// <returns>A new shape. The caller owns it and should dispose it.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="halfExtents"/> has any component that is not greater than zero.
        /// </exception>
        /// <exception cref="GoopException">
        /// The loaded goop_native.dll comes from a different build and speaks a
        /// different ABI version. Checked once, on first use.
        /// </exception>
        public static Shape Box(Vec3 halfExtents) {
            if (!(halfExtents.X > 0.0) || !(halfExtents.Y > 0.0) || !(halfExtents.Z > 0.0)) {
                throw new ArgumentOutOfRangeException(nameof(halfExtents), halfExtents, "All components of halfExtents must be greater than zero.");
            }
            AbiCheck.EnsureCompatible();
            int status = NativeMethods.goop_shape_box(halfExtents, out ShapeSafeHandle handle);
            return Shape.FromNative(status, handle);
        }
    }
}
