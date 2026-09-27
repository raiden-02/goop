using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Goop.Gallery {
    /// <summary>
    /// Showcase app: builds shapes with Goop and writes them to
    /// <c>gallery/output/</c> as STL files that any 3D viewer can open.
    /// </summary>
    public static class Program {
        public static int Main(string[] args) {
            // These two lines are the fastest way to diagnose a
            // BadImageFormatException. A 64-bit goop_native.dll cannot load into
            // a 32-bit process, and net48 defaults to AnyCPU/Prefer32Bit - so if
            // Is64BitProcess prints False, the PlatformTarget settings in
            // Directory.Build.props did not take effect and no amount of
            // debugging the interop will help.
            Console.WriteLine("Goop Gallery");
            Console.WriteLine("  Framework:      " + RuntimeInformation.FrameworkDescription);
            Console.WriteLine("  Is64BitProcess: " + Environment.Is64BitProcess);

            var outputDirectory = ResolveOutputDirectory();
            Directory.CreateDirectory(outputDirectory);
            Console.WriteLine("  Output:         " + outputDirectory);
            Console.WriteLine();

            // Two spheres, side by side with a gap between them. Every piece is
            // in its own `using`, so native memory is released deterministically.
            using var ball = Sdf.Sphere(1.0);
            using var smallBall = Sdf.Sphere(0.8);
            using var left = ball.Translate(-1.05, 0, 0);
            using var right = smallBall.Translate(1.15, 0, 0);

            // A box around both, with room for the blend to bulge outward.
            var min = new Vec3(-2.5, -1.5, -1.5);
            var max = new Vec3(2.5, 1.5, 1.5);

            // The headline: the same two spheres, apart and then melted together.
            using (var apart = left.Union(right)) {
                Save(apart, min, max, 128, Path.Combine(outputDirectory, "blob_apart.stl"));
            }
            // The surfaces are 0.4 apart, so at the middle of the gap each sphere
            // reports 0.2, and the smooth union there is 0.2 - k/4. It only goes
            // negative - i.e. the gap actually fills - once k > 0.8. At exactly
            // 0.8 the two just touch at a point. 1.2 gives a clear, solid neck.
            using (var blob = left.SmoothUnion(right, 1.2)) {
                Save(blob, min, max, 128, Path.Combine(outputDirectory, "blob.stl"));
            }

            // What the blend radius actually does: the same pair at increasing k.
            // Open these side by side. Below 0.8 the spheres stay apart (each is
            // only softened on its inner side); above it they join, and the neck
            // thickens as k grows.
            foreach (double k in new[] { 0.5, 1.0, 1.5 }) {
                using var blended = left.SmoothUnion(right, k);
                Save(blended, min, max, 96, Path.Combine(outputDirectory, "blend_k" + k.ToString("0.0").Replace('.', '_') + ".stl"));
            }

            Console.WriteLine();
            Console.WriteLine("Open the .stl files in any 3D viewer (Windows: 3D Viewer, or drag onto");
            Console.WriteLine("https://www.viewstl.com). Start with blob_apart.stl next to blob.stl.");

            // TODO: the rest of the showcase, as primitives and transforms land:
            //   * a torus subtracted from a box, for plain CSG
            //   * a twisted column, to exercise the non-isometric transform and
            //     see how the mesher copes with a field that only bounds the
            //     true distance
            //   * the same shape at a deliberately low resolution next to a high
            //     one, to make the grid visible
            //
            // TODO: accept the resolution and an output directory on the command
            //       line rather than ignoring args, so the gallery can be
            //       regenerated cheaply at draft quality.

            return 0;
        }

        private static void Save(Shape shape, Vec3 min, Vec3 max, int resolution, string path) {
            string name = Path.GetFileName(path);
            var timer = Stopwatch.StartNew();

            // The progress lambda is called from INSIDE the native mesher, once
            // per grid slice. It redraws a one-line bar in place with '\r'.
            int lastPercent = -1;
            using var mesh = shape.ToMesh(min, max, resolution, progress: fraction => {
                int percent = (int)(fraction * 100);
                if (percent != lastPercent) {
                    lastPercent = percent;
                    int filled = percent / 5;
                    Console.Write("\r  {0,-16} [{1}{2}] {3,3}%", name,
                        new string('#', filled), new string('.', 20 - filled), percent);
                }
            });
            mesh.SaveStl(path);
            timer.Stop();

            // Overwrite the bar with the final result line.
            Console.WriteLine("\r  {0,-16} {1,8:N0} triangles  res {2,3}  {3,6:N0} ms        ",
                name, mesh.TriangleCount, resolution, timer.ElapsedMilliseconds);
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
