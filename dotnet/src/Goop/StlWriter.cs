using System;
using System.IO;
using System.Text;

namespace Goop {
    // ======================================================================
    // StlWriter - writes a Mesh out as binary STL.
    //
    // Note where this lives: entirely in MANAGED code. The native side never
    // touches a file. Writing bytes is something C# does perfectly well, and
    // keeping file I/O out of the DLL keeps the C ABI free of paths, encodings,
    // file handles and the platform-specific mess that comes with them.
    //
    // Binary STL, byte for byte:
    //
    //   80 bytes   header - free text, but must NOT start with "solid"
    //    4 bytes   uint32 triangle count
    //   then per triangle, 50 bytes:
    //     12       normal      (3 x float32)
    //     36       3 vertices  (9 x float32)
    //      2       uint16 "attribute byte count", always 0
    //
    // Everything is little-endian. BinaryWriter is little-endian on every
    // platform .NET runs on, which is exactly what STL wants.
    // ======================================================================

    /// <summary>Writes meshes as binary STL, the format 3D printers and viewers read.</summary>
    public static class StlWriter {
        private const int HeaderLength = 80;

        // Some readers decide "ASCII or binary?" by checking whether the file
        // starts with the text "solid" - which is how ASCII STL begins. A binary
        // file whose header happened to start that way would be misread.
        private const string Header = "Goop binary STL";

        /// <summary>Writes <paramref name="mesh"/> to a file, replacing it if it exists.</summary>
        public static void Write(Mesh mesh, string path) {
            if (path == null) {
                throw new ArgumentNullException(nameof(path));
            }
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1 << 16);
            Write(mesh, file);
        }

        /// <summary>Writes <paramref name="mesh"/> to a stream, leaving the stream open.</summary>
        public static void Write(Mesh mesh, Stream stream) {
            if (mesh == null) {
                throw new ArgumentNullException(nameof(mesh));
            }
            // Copy out of native memory once, then do all the formatting in C#.
            Write(mesh.GetVertices(), mesh.GetIndices(), stream);
        }

        /// <summary>
        /// The actual writer, working on plain arrays. Internal so tests can feed it
        /// a hand-built mesh and check the exact bytes.
        /// </summary>
        internal static void Write(ReadOnlySpan<Vec3> vertices, ReadOnlySpan<int> indices, Stream stream) {
            if (stream == null) {
                throw new ArgumentNullException(nameof(stream));
            }
            if (indices.Length % 3 != 0) {
                throw new ArgumentException("indices must hold 3 entries per triangle.", nameof(indices));
            }

            int triangleCount = indices.Length / 3;

            // leaveOpen: true - the caller owns the stream, so disposing our
            // writer must not close it.
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

            var header = new byte[HeaderLength]; // zero-filled
            Encoding.ASCII.GetBytes(Header, 0, Header.Length, header, 0);
            writer.Write(header);
            writer.Write((uint)triangleCount);

            for (int t = 0; t < triangleCount; t++) {
                Vec3 a = vertices[indices[3 * t]];
                Vec3 b = vertices[indices[3 * t + 1]];
                Vec3 c = vertices[indices[3 * t + 2]];

                // Face normal from the winding: (b - a) x (c - a). The mesher
                // winds every triangle counter-clockwise from outside, so this
                // points OUT. Most viewers recompute normals and ignore these,
                // but some (and many slicers) trust them - so they must be right.
                double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
                double vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                double nx = uy * vz - uz * vy;
                double ny = uz * vx - ux * vz;
                double nz = ux * vy - uy * vx;
                double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (length > 0) {
                    nx /= length;
                    ny /= length;
                    nz /= length;
                } // a zero-area triangle has no direction: write (0, 0, 0)

                WriteVector(writer, nx, ny, nz);
                WriteVector(writer, a.X, a.Y, a.Z);
                WriteVector(writer, b.X, b.Y, b.Z);
                WriteVector(writer, c.X, c.Y, c.Z);
                writer.Write((ushort)0);
            }
        }

        // STL stores SINGLE precision. Goop works in doubles everywhere; this is
        // the one place in the whole library where they are narrowed to float.
        private static void WriteVector(BinaryWriter writer, double x, double y, double z) {
            writer.Write((float)x);
            writer.Write((float)y);
            writer.Write((float)z);
        }
    }
}
