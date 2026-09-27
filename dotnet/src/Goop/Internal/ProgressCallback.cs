using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

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
    // HOW THIS FILE SOLVES IT
    //
    // There are two textbook fixes:
    //
    //   (a) a per-call delegate held in a local, with GC.KeepAlive(local) after
    //       the native call so the JIT cannot consider it dead early;
    //   (b) ONE static delegate that lives for the whole life of the process,
    //       with the per-call state carried through the native user_data
    //       pointer instead of captured in a closure.
    //
    // This uses (b). A static readonly field is a GC root forever, so the thunk
    // can never be collected - there is nothing to forget, no KeepAlive to get
    // wrong, and no allocation per call. The state (the user's lambda, the
    // CancellationToken, any exception) lives in a ProgressBridge object, and a
    // GCHandle turns that object into an IntPtr that C++ carries around without
    // understanding and hands straight back. That is exactly what user_data is
    // for.
    // ======================================================================

    /// <summary>
    /// The managed shape of <c>goop_progress_fn</c>: <c>int32_t (*)(double, void*)</c>.
    /// </summary>
    /// <remarks>
    /// Cdecl to match the C typedef. Returns <c>int</c>, not <c>bool</c>: a
    /// managed bool marshals as a 4-byte Win32 BOOL by default, which is not
    /// what the C side declares. 0 = carry on, nonzero = cancel.
    /// </remarks>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ProgressCallback(double fraction, IntPtr userData);

    /// <summary>
    /// Everything one <c>goop_shape_to_mesh</c> call needs to report progress,
    /// honour a <see cref="CancellationToken"/>, and survive a throwing lambda.
    /// Create it right before the native call and dispose it right after.
    /// </summary>
    internal sealed class ProgressBridge : IDisposable {
        // THE fix for the lifetime problem: one delegate, rooted by a static
        // field for the life of the process. Its native thunk can never be freed.
        private static readonly ProgressCallback SharedCallback = OnProgress;

        private readonly Action<double>? _progress;
        private readonly CancellationToken _cancellationToken;

        // A NORMAL (not pinned) handle to this object. Normal is enough: C++ never
        // dereferences user_data, it only passes it back, so the object is free to
        // move - the handle keeps it alive and GCHandle.FromIntPtr finds it
        // wherever it went. Pinning would only get in the GC's way.
        private GCHandle _self;

        // An exception thrown by the user's lambda, captured inside the callback
        // and rethrown once we are safely back in managed code.
        private ExceptionDispatchInfo? _error;

        private ProgressBridge(Action<double>? progress, CancellationToken cancellationToken) {
            _progress = progress;
            _cancellationToken = cancellationToken;
            _self = GCHandle.Alloc(this, GCHandleType.Normal);
        }

        /// <summary>
        /// A bridge for this call, or <c>null</c> when there is nothing to report
        /// and nothing to cancel - the native side then runs with no callback at
        /// all, at zero cost.
        /// </summary>
        public static ProgressBridge? Create(Action<double>? progress, CancellationToken cancellationToken) {
            if (progress == null && !cancellationToken.CanBeCanceled) {
                return null;
            }
            return new ProgressBridge(progress, cancellationToken);
        }

        /// <summary>The function pointer to pass as <c>progress</c>.</summary>
        public ProgressCallback Callback => SharedCallback;

        /// <summary>The value to pass as <c>user_data</c>.</summary>
        public IntPtr UserData => GCHandle.ToIntPtr(_self);

        /// <summary>True if the user's lambda threw during the native call.</summary>
        public bool Failed => _error != null;

        /// <summary>
        /// Call after the native function returns. Rethrows the user's own
        /// exception if the lambda threw, or throws
        /// <see cref="OperationCanceledException"/> carrying the caller's token if
        /// the token is what stopped the mesher. Otherwise does nothing.
        /// </summary>
        public void ThrowIfStopped() {
            // Rethrow with the ORIGINAL stack trace, pointing into the user's
            // lambda - not a new exception pointing here.
            _error?.Throw();
            _cancellationToken.ThrowIfCancellationRequested();
        }

        public void Dispose() {
            if (_self.IsAllocated) {
                _self.Free(); // always: a leaked GCHandle leaks this object forever
            }
        }

        // Called FROM C++, on the thread that called goop_shape_to_mesh, once per
        // z-slice. Must never let an exception escape (ABI rule 6): unwinding
        // through native frames is undefined behaviour.
        private static int OnProgress(double fraction, IntPtr userData) {
            var bridge = (ProgressBridge)GCHandle.FromIntPtr(userData).Target!;
            try {
                if (bridge._cancellationToken.IsCancellationRequested) {
                    return 1; // stop
                }
                bridge._progress?.Invoke(fraction);
                // Checked again: the lambda itself may have requested cancellation.
                return bridge._cancellationToken.IsCancellationRequested ? 1 : 0;
            } catch (Exception ex) {
                // Park the exception and ask the mesher to stop. It unwinds its
                // own C++ frames normally, returns GOOP_ERROR_CANCELLED, and the
                // caller rethrows this from managed code via ThrowIfStopped.
                bridge._error = ExceptionDispatchInfo.Capture(ex);
                return 1;
            }
        }
    }
}
