namespace Goop {
    // ======================================================================
    // StlWriter - writes a Mesh out as binary STL.
    //
    // Note where this lives: entirely in MANAGED code. The native side never
    // touches a file. Writing bytes is something C# does perfectly well, and
    // keeping file I/O out of the DLL keeps the C ABI free of paths, encodings,
    // file handles and the platform-specific mess that comes with them.
    // ======================================================================

    // TODO: an internal or public static class StlWriter with something like
    //       Write(Mesh mesh, Stream stream) plus a path-taking convenience
    //       overload. Stream-first means tests can write to a MemoryStream and
    //       assert on the bytes without touching the disk.
    //
    // TODO: the binary STL format, which is small enough to write from memory
    //       but has three traps in it:
    //
    //         * 80-byte header. Arbitrary content, but it must NOT begin with
    //           the ASCII text "solid" - some readers sniff those five bytes to
    //           decide whether the file is ASCII STL and will then fail to parse
    //           a binary file. Write something like "Goop binary STL" padded
    //           with zeros.
    //
    //         * uint32 triangle count, then that many 50-byte records:
    //           12 floats (normal xyz, then three vertices xyz) followed by a
    //           uint16 "attribute byte count" that is essentially always 0.
    //           50 bytes, not 52 - the record is not 4-byte aligned, so do not
    //           let any padding creep in.
    //
    //         * SINGLE precision. Goop works in doubles throughout; STL floats
    //           are 32-bit. The narrowing happens here and nowhere else, and it
    //           is worth a comment at the conversion site.
    //
    // TODO: normals. Most readers ignore the stored normal and recompute from
    //       the winding order, but some do not. Compute the face normal from the
    //       triangle rather than writing zeros, and make sure the winding
    //       convention agrees with what mesh.hpp documents - if they disagree,
    //       every normal in the file points inward and the model renders
    //       inside-out.
    //
    // TODO: little-endian. BinaryWriter is little-endian on every platform .NET
    //       runs on today, which matches the STL spec, but say so in a comment
    //       so the assumption is deliberate rather than accidental.
    //
    // TODO: for a large mesh, avoid building the whole byte array in memory -
    //       stream it. Also consider a buffered writer; a per-float write call
    //       on a FileStream for a million triangles is painfully slow.
    //
    // TODO (test): round-trip. Write a known cube, read it back with a tiny
    //       parser in the test project, and assert the triangle count and
    //       vertex positions survive. A golden-bytes test for a two-triangle
    //       mesh is also worth having - it pins the exact header and record
    //       layout.
}
