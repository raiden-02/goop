using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>The STL format pinned byte for byte, then checked on a real mesh.</summary>
    [TestClass]
    public class StlWriterTests {
        private const int HeaderLength = 80;
        private const int TriangleRecordLength = 50;

        [TestMethod]
        public void OneTriangleProducesExactlyTheExpectedBytes() {
            // One triangle in z = 0, counter-clockwise from +z, so normal (0, 0, 1).
            var vertices = new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0) };
            using var stream = new MemoryStream();
            StlWriter.Write(vertices, new[] { 0, 1, 2 }, stream);
            byte[] bytes = stream.ToArray();

            Assert.AreEqual(HeaderLength + 4 + TriangleRecordLength, bytes.Length);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            string header = Encoding.ASCII.GetString(reader.ReadBytes(HeaderLength));
            Assert.IsFalse(header.StartsWith("solid"), "readers would take the file for ASCII STL");
            Assert.AreEqual(1u, reader.ReadUInt32());
            CollectionAssert.AreEqual(new[] { 0f, 0f, 1f }, ReadFloats(reader, 3), "normal");
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f }, ReadFloats(reader, 3), "vertex 0");
            CollectionAssert.AreEqual(new[] { 1f, 0f, 0f }, ReadFloats(reader, 3), "vertex 1");
            CollectionAssert.AreEqual(new[] { 0f, 1f, 0f }, ReadFloats(reader, 3), "vertex 2");
            Assert.AreEqual((ushort)0, reader.ReadUInt16());
        }

        [TestMethod]
        public void EveryNormalInARealMeshPointsOutward() {
            // Sphere at the origin: an outward normal points away from the centre.
            // Checks the mesher's winding all the way through to the file.
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(new Vec3(-1.5, -1.5, -1.5), new Vec3(1.5, 1.5, 1.5), 32);
            using var stream = new MemoryStream();
            mesh.SaveStl(stream);

            using var reader = new BinaryReader(new MemoryStream(stream.ToArray()));
            reader.ReadBytes(HeaderLength);
            uint count = reader.ReadUInt32();
            Assert.AreEqual((uint)mesh.TriangleCount, count);
            for (int t = 0; t < count; t++) {
                float[] n = ReadFloats(reader, 3), a = ReadFloats(reader, 3), b = ReadFloats(reader, 3), c = ReadFloats(reader, 3);
                reader.ReadUInt16();
                double dot = n[0] * (a[0] + b[0] + c[0]) + n[1] * (a[1] + b[1] + c[1]) + n[2] * (a[2] + b[2] + c[2]);
                Assert.IsTrue(dot > 0, "triangle " + t + " has an inward normal");
            }
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
