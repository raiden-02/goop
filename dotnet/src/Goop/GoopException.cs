using Goop.Internal;
using System;
using System.Runtime.InteropServices;

namespace Goop {
    // ======================================================================
    // GoopException - where goop_status codes turn back into exceptions.
    //
    // The ABI cannot carry exceptions (see rule 2 in goop.h), so api.cpp catches
    // everything and returns a status code plus a thread-local message. This
    // file is the other half of that trade: it reads the status, fetches the
    // message via goop_last_error_message, and throws something a C# caller can
    // actually catch.
    //
    // The point is that the exception is reconstructed at the boundary, so the
    // managed API never asks its users to check return codes.
    // ======================================================================

    // TODO: GoopException(string message, Exception inner) so a caller can chain
    //       a managed failure onto the native one. The status-and-message
    //       constructor already exists.
    //
    // TODO: [Serializable] and the serialization constructor if targeting net48
    //       properly - legacy, but net48 consumers may still expect it.

    /// <summary>Mirrors <c>goop_status</c> in goop.h. Values must never be renumbered.</summary>
    internal enum GoopStatus : int {
        Ok = 0,
        InvalidArgument = 1,
        NullHandle = 2,
        OutOfMemory = 3,
        Cancelled = 4,
        Internal = 5,
    }

    /// <summary>
    /// Raised for native failures that have no closer standard .NET exception.
    /// </summary>
    public class GoopException : Exception {
        /// <summary>The raw <c>goop_status</c> value returned by the native call.</summary>
        public int Status { get; }

        /// <summary>Creates an exception carrying the native status and message.</summary>
        public GoopException(int status, string message)
            : base(message) {
            Status = status;
        }
    }

    /// <summary>
    /// The single place that turns a native status code into a thrown exception.
    /// </summary>
    internal static class Errors {
        /// <summary>
        /// Returns if <paramref name="status"/> is OK; otherwise reads the native
        /// last-error message and throws the matching .NET exception.
        /// </summary>
        public static void ThrowIfError(int status) {
            if (status == (int)GoopStatus.Ok) {
                return;
            }

            // Copy immediately: the native buffer is only valid until the next
            // failing call on this thread. IntPtr, not string, in the DllImport so
            // the marshaller never tries to free memory the DLL owns.
            string? message = Marshal.PtrToStringAnsi(NativeMethods.goop_last_error_message());
            if (string.IsNullOrEmpty(message)) {
                message = "native call failed with status " + status;
            }

            switch ((GoopStatus)status) {
                case GoopStatus.InvalidArgument:
                    throw new ArgumentException(message);
                case GoopStatus.NullHandle:
                    throw new ObjectDisposedException(null, message);
                case GoopStatus.OutOfMemory:
                    throw new OutOfMemoryException(message);
                case GoopStatus.Cancelled:
                    throw new OperationCanceledException(message);
                default:
                    throw new GoopException(status, message);
            }
        }
    }
}
