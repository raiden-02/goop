using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Goop.Gallery {
    /// <summary>
    /// Showcase app: builds interesting shapes and writes them to
    /// <c>gallery/output/</c> as STL. Today it only reports the host it is
    /// running on and makes sure the output directory exists.
    /// </summary>
    public static class Program {
        public static int Main(string[] args) {
            // These two lines are the fastest way to diagnose a
            // BadImageFormatException. A 64-bit goop.dll cannot load into a
            // 32-bit process, and net48 defaults to AnyCPU/Prefer32Bit - so if
            // Is64BitProcess prints False, the PlatformTarget settings in
            // Directory.Build.props did not take effect and no amount of
            // debugging the interop will help.
            Console.WriteLine("Goop Gallery");
            Console.WriteLine("  Framework:      " + RuntimeInformation.FrameworkDescription);
            Console.WriteLine("  Is64BitProcess: " + Environment.Is64BitProcess);

            var outputDirectory = ResolveOutputDirectory();
            Directory.CreateDirectory(outputDirectory);
            Console.WriteLine("  Output:         " + outputDirectory);

            // TODO (M4+): generate the showcase shapes into outputDirectory.
            //   * the README's headline blob: a sphere smooth-unioned with a box
            //   * a sweep of the same blend across several k values, to show what
            //     the blend radius actually does - that sequence is the single
            //     most illustrative thing this project can produce
            //   * a torus subtracted from a box, for plain CSG
            //   * a twisted column, to exercise the non-isometric transform and
            //     see how the mesher copes with a field that only bounds the
            //     true distance
            //   * something at a deliberately low resolution next to the same
            //     shape at a high one, to make the grid visible
            //
            // TODO (M6): pass a progress callback that draws a console progress
            //       bar. The high-resolution meshes take long enough for it to
            //       matter, and it is a real exercise of the delegate lifetime
            //       rules in Internal/ProgressCallback.cs.
            //
            // TODO: accept the resolution and an output directory on the command
            //       line rather than ignoring args, so the gallery can be
            //       regenerated cheaply at draft quality.

            return 0;
        }

        /// <summary>
        /// Finds <c>gallery/output/</c> at the repository root by walking up from
        /// the binary's directory, looking for the top-level CMakeLists.txt.
        /// </summary>
        /// <remarks>
        /// The executable lives several levels deep under
        /// <c>dotnet/samples/Goop.Gallery/bin/&lt;Config&gt;/&lt;tfm&gt;/</c>, and
        /// that depth differs between frameworks, so searching for a marker file
        /// is steadier than counting "..".
        /// </remarks>
        private static string ResolveOutputDirectory() {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null) {
                if (File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "gallery"))) {
                    return Path.Combine(directory.FullName, "gallery", "output");
                }

                directory = directory.Parent;
            }

            // Not running from inside the repo (packaged, copied elsewhere).
            // Fall back to a folder beside the binary rather than failing.
            return Path.Combine(AppContext.BaseDirectory, "gallery-output");
        }
    }
}
