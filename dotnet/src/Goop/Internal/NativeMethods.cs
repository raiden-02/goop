using System;
using System.Runtime.InteropServices;

namespace Goop.Internal
{
    // ======================================================================
    // NativeMethods - every P/Invoke into goop_native.dll, and nothing else.
    //
    // Internal by design: the raw ABI is not part of the public surface of this
    // assembly. Goop.Tests can see it through InternalsVisibleTo so the interop
    // can be exercised directly.
    // ======================================================================

    internal static class NativeMethods
    {
        /// <summary>
        /// The native library name passed to every <c>DllImport</c>.
        /// </summary>
        /// <remarks>
        /// No extension and no path on purpose. The runtime appends the
        /// platform's own suffix and searches the standard probing paths, which
        /// on Windows starts with the directory of the executing assembly -
        /// hence the copy target in the test and sample projects that drops
        /// goop_native.dll next to the managed output.
        /// </remarks>
        internal const string LibraryName = "goop_native";

        // TODO: one [DllImport(LibraryName)] extern per function in goop.h, added
        //       milestone by milestone. Conventions to settle before the first
        //       one goes in, because changing them later means touching all of
        //       them:
        //
        //         * CallingConvention = CallingConvention.Cdecl. The C side is
        //           cdecl; the P/Invoke default on Windows is StdCall. A
        //           mismatch does not fail at load time - it corrupts the stack
        //           at call time, which is a spectacularly confusing bug.
        //
        //         * ExactSpelling = true, so the marshaller does not go hunting
        //           for an "A"/"W" suffixed variant that does not exist.
        //
        //         * SetLastError = false. The native side reports errors through
        //           goop_status and goop_last_error_message, not through the
        //           Win32 last-error channel.
        //
        //         * Blittable parameter types only: the Vec3 struct, pointers,
        //           IntPtr, and the fixed-width integers. No string parameters,
        //           no bool (its native width is not what you would guess - use
        //           int and compare), no arrays that need marshalling.
        //
        //         * SafeHandle subclasses as return and parameter types wherever
        //           a handle crosses, rather than raw IntPtr. That is what makes
        //           the release path robust against exceptions and async
        //           aborts.
        //
        //       NOTE: use [DllImport], NOT [LibraryImport]. The source-generated
        //       LibraryImport is a net7.0+ feature and this assembly also targets
        //       net48, so the generator is simply not available there. One set of
        //       DllImports shared by both target frameworks is better than two
        //       divergent interop layers behind #if.

        [DllImport(
            LibraryName,
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true,
            SetLastError = false
        )]
        internal static extern int goop_get_version();

        [DllImport(
            LibraryName,
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true,
            SetLastError = false
        )]
        internal static extern int goop_vec3_size();

        [DllImport(
            LibraryName,
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true,
            SetLastError = false
        )]
        internal static extern int goop_shape_sphere(double radius, out IntPtr shape);

        [DllImport(
            LibraryName,
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true,
            SetLastError = false
        )]
        internal static extern void goop_shape_release(IntPtr shape);
    }
}
