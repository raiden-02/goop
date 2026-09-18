using System.Runtime.InteropServices;

namespace Goop
{
    // ======================================================================
    // Vec3 - the managed mirror of goop_vec3.
    //
    // This type's entire job is to have the same memory layout as the native
    // struct so that arrays of it can be handed to goop_shape_eval_batch and
    // goop_mesh_copy_vertices with no marshalling at all.
    // ======================================================================

    // TODO: a public readonly struct Vec3 holding three doubles: X, Y, Z.
    //
    //       Requirements that come from the ABI, not from taste:
    //
    //         * BLITTABLE. Only double fields. No bool, no char, no string, no
    //           reference types, no auto-properties backed by anything unusual.
    //           A blittable struct can be pinned and passed as a pointer; a
    //           non-blittable one gets copied field by field through the
    //           marshaller, which is both slow and a source of surprises.
    //
    //         * [StructLayout(LayoutKind.Sequential)] so the field order is the
    //           declaration order rather than whatever the runtime prefers.
    //           Sequential, not Explicit - there is nothing to overlay here.
    //
    //         * double, matching the native side. Not float. Distance fields
    //           accumulate error quickly and single precision shows up as
    //           visible banding near thin features.
    //
    //       Deliberately NOT System.Numerics.Vector3: that type is
    //       single-precision and carries SIMD assumptions about its layout.
    //       Hand-rolling three doubles is both more honest and easier to match
    //       against the C struct.
    //
    // TODO: the small amount of arithmetic the fluent API actually needs -
    //       construction, +, -, scalar *, Length, and a readable ToString for
    //       debugging. Resist adding a full vector maths library here; the real
    //       arithmetic happens in C++.
    //
    // TODO: value equality. Override Equals and GetHashCode, and think about
    //       whether == should be exact bitwise comparison (defensible for a
    //       struct) or tolerance-based (never do this in operator==; put it in
    //       a separate ApproximatelyEquals used by tests).
    //
    // TODO: once the struct exists, add a test asserting
    //       Marshal.SizeOf<Vec3>() == 24. It is a one-line test that catches a
    //       whole class of layout mistakes immediately.

    /// <summary>
    /// A point or vector in 3D space. Three <c>double</c> fields, in the same
    /// order and size as <c>goop_vec3</c> in the native header, so a value can
    /// be passed to the DLL with no conversion.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Vec3
    {
        /// <summary>The X coordinate, in the same units as the native struct.</summary>
        public readonly double X;

        /// <summary>The Y coordinate, in the same units as the native struct.</summary>
        public readonly double Y;

        /// <summary>The Z coordinate, in the same units as the native struct.</summary>
        public readonly double Z;

        /// <summary>Creates a vector from three coordinates.</summary>
        /// <param name="x">The X coordinate.</param>
        /// <param name="y">The Y coordinate.</param>
        /// <param name="z">The Z coordinate.</param>
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        /// <summary>Returns the vector as <c>(X, Y, Z)</c>.</summary>
        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
