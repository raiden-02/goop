namespace Goop.Internal {
    // ======================================================================
    // ProgressCallback - the managed side of the native progress function.
    //
    // THE PROBLEM THIS FILE EXISTS TO SOLVE
    //
    // When a managed delegate is passed to native code, the marshaller creates a
    // small native thunk - a real function pointer that, when called, re-enters
    // the CLR and invokes the delegate. That thunk is kept alive by the
    // DELEGATE OBJECT, and by nothing else.
    //
    // The native side stores only the raw function pointer. The GC cannot see
    // it. So if the only managed reference to the delegate is a temporary at the
    // call site - or worse, a lambda converted inline in the argument list - the
    // delegate becomes eligible for collection the instant the call site stops
    // referencing it. The native code then calls through a freed thunk. The
    // result is a crash, and the classic symptom is that it only happens during
    // LONG meshes, on some machines, under memory pressure - because that is
    // when a GC actually runs mid-call.
    //
    // The fix is to hold a strong managed reference to the delegate for the
    // entire duration of the native call. Concretely:
    //
    //   * assign the delegate to a LOCAL VARIABLE before the P/Invoke, so it
    //     stays rooted for the call;
    //   * and add GC.KeepAlive(thatLocal) AFTER the native call returns.
    //     Without KeepAlive, an aggressive JIT is entitled to consider the local
    //     dead from its last read - which is before the native call finishes.
    //
    // A GCHandle (normal, not pinned) around the user's state object is the
    // other half: it turns a managed object into an IntPtr that can ride along
    // as the native user_data parameter and be unwrapped inside the callback.
    // Free it in a finally block, always.
    //
    // Deliberately no delegate type is declared here yet - that comes with
    // milestone M6, once the native signature is settled.
    // ======================================================================

    // TODO: declare the delegate matching the native progress typedef, something
    //       like:
    //           internal delegate int ProgressCallback(double fraction, IntPtr userData);
    //       with [UnmanagedFunctionPointer(CallingConvention.Cdecl)] on it. The
    //       calling convention must match the C side exactly - a mismatch here
    //       corrupts the stack rather than failing cleanly.
    //
    //       Return int, not bool: the native width of a managed bool is not what
    //       most people expect. Nonzero means cancel, matching goop.h.
    //
    // TODO: a small helper that takes the user's Action<double> plus a
    //       CancellationToken and produces (delegate, GCHandle) to hand to the
    //       P/Invoke, with a Dispose/finally that frees the GCHandle.
    //
    // TODO: EXCEPTIONS MUST NOT ESCAPE THE CALLBACK. If the user's progress
    //       lambda throws, that exception would unwind through native frames,
    //       which is undefined behaviour. Wrap the user's call in try/catch
    //       inside the callback body: stash the exception, return the cancel
    //       code so the mesher stops cleanly, and rethrow it on the managed side
    //       after the native call returns.
    //
    // TODO: fold CancellationToken in. The callback checks
    //       token.IsCancellationRequested and returns nonzero, and the resulting
    //       native cancelled status is translated into
    //       OperationCanceledException by GoopException's mapping - so
    //       cancellation behaves the way every other .NET API behaves.
    //
    // TODO: reentrancy. The callback runs on whichever thread is doing the
    //       meshing, which is the caller's thread for now. If meshing ever moves
    //       to a background thread, a progress callback touching UI state
    //       becomes a cross-thread bug. Say which thread it runs on in the XML
    //       docs on Shape.ToMesh.
    //
    // TODO (test): the one that actually catches the lifetime bug. Mesh at a
    //       resolution high enough to take a while, and from inside the progress
    //       callback force collections with GC.Collect() plus
    //       GC.WaitForPendingFinalizers(). If the delegate is not rooted
    //       properly, this crashes the test host - reliably, which is exactly
    //       what makes it a good test.
}
