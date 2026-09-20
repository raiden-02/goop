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

    // TODO: the arithmetic the fluent API still needs: +, -, scalar *, Length.
    //       Construction and ToString are already here. Resist adding a full
    //       vector maths library; the real arithmetic happens in C++.
    //
    // TODO: value equality. Override Equals and GetHashCode, and think about
    //       whether == should be exact bitwise comparison (defensible for a
    //       struct) or tolerance-based (never do this in operator==; put it in
    //       a separate ApproximatelyEquals used by tests).

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
