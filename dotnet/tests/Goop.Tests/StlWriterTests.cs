using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>Tests for <see cref="StlWriter"/>: exact bytes, then real meshes.</summary>
    [TestClass]
    public class StlWriterTests {
        private const int HeaderLength = 80;
        private const int TriangleRecordLength = 50;

        [TestMethod]
        public void OneTriangleProducesExactlyTheExpectedBytes() {
            // A golden test: the format pinned byte for byte, on a mesh small
            // enough to work out by hand. One triangle in the z = 0 plane, wound
            // counter-clockwise seen from +z, so its normal is (0, 0, 1).
            var vertices = new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0) };
            var indices = new[] { 0, 1, 2 };

            byte[] bytes = WriteToBytes(vertices, indices);

            Assert.AreEqual(HeaderLength + 4 + TriangleRecordLength, bytes.Length); // 134

            using var reader = new BinaryReader(new MemoryStream(bytes));
            byte[] header = reader.ReadBytes(HeaderLength);
            Assert.AreEqual("Goop binary STL", Encoding.ASCII.GetString(header, 0, 15));
            Assert.AreEqual(0, header[HeaderLength - 1], "rest of the header is zero-padded");

            Assert.AreEqual(1u, reader.ReadUInt32(), "triangle count");
            CollectionAssert.AreEqual(new[] { 0f, 0f, 1f }, ReadFloats(reader, 3), "normal");
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f }, ReadFloats(reader, 3), "vertex 0");
            CollectionAssert.AreEqual(new[] { 1f, 0f, 0f }, ReadFloats(reader, 3), "vertex 1");
            CollectionAssert.AreEqual(new[] { 0f, 1f, 0f }, ReadFloats(reader, 3), "vertex 2");
            Assert.AreEqual((ushort)0, reader.ReadUInt16(), "attribute byte count");
        }

        [TestMethod]
        public void HeaderNeverStartsWithSolid() {
            // "solid" at the start is how readers recognise ASCII STL. A binary
            // file that began with it would be misread as text.
            byte[] bytes = WriteToBytes(Array.Empty<Vec3>(), Array.Empty<int>());
            string start = Encoding.ASCII.GetString(bytes, 0, 5);
            Assert.AreNotEqual("solid", start);
        }

        [TestMethod]
        public void EmptyMeshIsJustAHeaderAndAZeroCount() {
            byte[] bytes = WriteToBytes(Array.Empty<Vec3>(), Array.Empty<int>());
            Assert.AreEqual(HeaderLength + 4, bytes.Length);
            Assert.AreEqual(0u, BitConverter.ToUInt32(bytes, HeaderLength));
        }

        [TestMethod]
        public void RealMeshHasTheRightSizeAndEveryNormalPointsOutward() {
            // A sphere centred on the origin: an OUTWARD normal must point away
            // from the origin, i.e. have a positive dot product with the
            // triangle's centre. One inward normal fails the test - that is the
            // mesher's winding checked all the way through to the file.
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(new Vec3(-1.5, -1.5, -1.5), new Vec3(1.5, 1.5, 1.5), 32);

            using var stream = new MemoryStream();
            mesh.SaveStl(stream);
            byte[] bytes = stream.ToArray();

            Assert.AreEqual(HeaderLength + 4 + TriangleRecordLength * mesh.TriangleCount, bytes.Length);

            using var reader = new BinaryReader(new MemoryStream(bytes));
            reader.ReadBytes(HeaderLength);
            uint count = reader.ReadUInt32();
            Assert.AreEqual((uint)mesh.TriangleCount, count);

            for (int t = 0; t < count; t++) {
                float[] n = ReadFloats(reader, 3);
                float[] a = ReadFloats(reader, 3), b = ReadFloats(reader, 3), c = ReadFloats(reader, 3);
                reader.ReadUInt16();

                double cx = (a[0] + b[0] + c[0]) / 3, cy = (a[1] + b[1] + c[1]) / 3, cz = (a[2] + b[2] + c[2]) / 3;
                double dot = n[0] * cx + n[1] * cy + n[2] * cz;
                Assert.IsTrue(dot > 0, "triangle " + t + " has an inward normal");
            }
        }

        [TestMethod]
        public void SaveStlToAPathWritesTheFile() {
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(new Vec3(-1.5, -1.5, -1.5), new Vec3(1.5, 1.5, 1.5), 16);

            string path = Path.Combine(Path.GetTempPath(), "goop-test-" + Guid.NewGuid().ToString("N") + ".stl");
            try {
                mesh.SaveStl(path);
                Assert.AreEqual(HeaderLength + 4 + TriangleRecordLength * mesh.TriangleCount, new FileInfo(path).Length);
            } finally {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void TheCallersStreamIsLeftOpen() {
            using var stream = new MemoryStream();
            StlWriter.Write(Array.Empty<Vec3>(), Array.Empty<int>(), stream);
            Assert.IsTrue(stream.CanWrite, "the writer must not close a stream it does not own");
        }

        private static byte[] WriteToBytes(Vec3[] vertices, int[] indices) {
            using var stream = new MemoryStream();
            StlWriter.Write(vertices, indices, stream);
            return stream.ToArray();
        }

        private static float[] ReadFloats(BinaryReader reader, int count) {
            var values = new float[count];
            for (int i = 0; i < count; i++) {
                values[i] = reader.ReadSingle();
            }
            return values;
        }
    }
}
