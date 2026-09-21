using System;
using System.Runtime.InteropServices;

namespace Goop.Internal {
    // ======================================================================
    // NativeMethods - every P/Invoke into goop_native.dll, and nothing else.
    //
    // Internal by design: the raw ABI is not part of the public surface of this
    // assembly. Goop.Tests can see it through InternalsVisibleTo so the interop
    // can be exercised directly.
    // ======================================================================

    internal static class NativeMethods {
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

        // Conventions for every DllImport in this class:
        //
        //   * CallingConvention = CallingConvention.Cdecl. The C side is
        //     cdecl; the P/Invoke default on Windows is StdCall. A
        //     mismatch does not fail at load time - it corrupts the stack
        //     at call time.
        //
        //   * ExactSpelling = true, so the marshaller does not go hunting
        //     for an "A"/"W" suffixed variant that does not exist.
        //
        //   * SetLastError = false. The native side reports errors through
        //     goop_status and goop_last_error_message, not through the
        //     Win32 last-error channel.
        //
        //   * Blittable parameter types only: the Vec3 struct, pointers,
        //     IntPtr, and the fixed-width integers. No string parameters,
        //     no bool (its native width is not what you would guess - use
        //     int and compare), no arrays that need marshalling.
        //
        //   * Use [DllImport], not [LibraryImport]. LibraryImport is a
        //     net7.0+ feature and this assembly also targets net48.
        //
        //   * SafeHandle subclasses wherever a handle crosses, rather than raw
        //     IntPtr. The one exception is the release function itself: it is
        //     called from ReleaseHandle(), which only has the raw handle field.

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
        internal static extern int goop_shape_sphere(double radius, out ShapeSafeHandle shape);

        [DllImport(
            LibraryName,
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true,
            SetLastError = false
        )]
        internal static extern void goop_shape_release(IntPtr shapeHandle);

        [DllImport(
            LibraryName,
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true,
            SetLastError = false
        )]
        internal static extern IntPtr goop_last_error_message();
    }
}
