namespace Goop
{
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

    // TODO: a public class GoopException : Exception carrying the native status
    //       code as a property, plus the standard constructor set (message;
    //       message + inner). Keep the raw status accessible - "it failed" is
    //       much less useful than "it failed with status 3".
    //
    // TODO: an internal static ThrowIfError(status) helper that every P/Invoke
    //       call site funnels through. One place that knows the mapping means
    //       the mapping can be changed in one place.
    //
    // TODO: map statuses onto the RIGHT exception types, not all onto
    //       GoopException. A caller should be able to catch what they expect:
    //
    //         invalid argument  -> ArgumentException / ArgumentOutOfRangeException
    //         null handle       -> ObjectDisposedException (usually what it means
    //                              in practice) or ArgumentNullException
    //         out of memory     -> OutOfMemoryException
    //         cancelled         -> OperationCanceledException, NOT an error -
    //                              cancellation is a normal outcome and callers
    //                              expect the standard type so that
    //                              CancellationToken plumbing works
    //         internal/unknown  -> GoopException
    //
    // TODO: fetching the message. goop_last_error_message returns a const char*
    //       owned by the DLL and valid only until the next failing call on that
    //       thread, so copy it immediately with Marshal.PtrToStringAnsi (or
    //       PtrToStringUTF8 on net8.0 - note that one does not exist on net48,
    //       which is exactly the kind of thing LangVersion=latest does NOT fix).
    //       Decide the encoding on the native side first and match it.
    //
    // TODO: handle the null / empty message case gracefully. A status code with
    //       no message should still produce a readable exception rather than
    //       "Exception of type 'GoopException' was thrown."
    //
    // TODO: [Serializable] and the serialization constructor if targeting net48
    //       properly - legacy, but net48 consumers may still expect it.
}
