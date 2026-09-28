namespace Goop.Internal {
    // ======================================================================
    // AbiCheck - refuses to talk to a goop_native.dll from a different build.
    //
    // Goop.dll and goop_native.dll ship as two files. If they ever come from
    // different builds, nothing else notices, and the failure depends on what
    // changed: a renamed export fails loudly, but a reordered struct or a
    // renumbered status code produces silently WRONG results. So before the
    // first real native call, Goop asks the DLL which ABI version it was built
    // with, and stops with one clear message if it is not the expected one.
    // ======================================================================

    internal static class AbiCheck {
        /// <summary>
        /// The ABI version this build of Goop.dll speaks. Must equal
        /// <c>GOOP_ABI_VERSION</c> in <c>native/include/goop/goop.h</c>; a test
        /// checks the two agree.
        /// </summary>
        internal const int ExpectedVersion = 1;

        // Set once the check has passed. volatile so every thread sees it. The
        // check itself is idempotent, so two threads racing through it the first
        // time is harmless - they both just verify.
        private static volatile bool _verified;

        /// <summary>
        /// Throws if the loaded goop_native.dll speaks a different ABI version.
        /// Cheap after the first call. Call before any other native function.
        /// </summary>
        internal static void EnsureCompatible() {
            if (_verified) {
                return;
            }
            // goop_get_version is the one export whose signature can never
            // change, which is what makes it safe to call on a DLL of unknown
            // version. A missing DLL surfaces here as DllNotFoundException.
            Verify(NativeMethods.goop_get_version());
            _verified = true;
        }

        /// <summary>The comparison, separated out so it can be tested without a mismatched DLL.</summary>
        internal static void Verify(int nativeVersion) {
            if (nativeVersion != ExpectedVersion) {
                throw new GoopException((int)GoopStatus.Internal,
                    "goop_native.dll reports ABI version " + nativeVersion + ", but this Goop.dll requires version " +
                    ExpectedVersion + ". The two files come from different builds; reinstall the Goop package so they match.");
            }
        }
    }
}
