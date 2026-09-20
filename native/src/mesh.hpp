// ===========================================================================
// mesh.hpp - plain mesh storage. Deliberately dumb.
//
// This is the output of the mesher and the input to the copy-out functions in
// api.cpp: a flat vertex array plus a flat index array, three indices per
// triangle. No normals, no attributes, no adjacency, no cleverness.
//
// Anything smarter - adjacency, vertex welding, normal generation - belongs
// either in the mesher (before the mesh is handed over) or on the managed side.
// This type exists so that goop_mesh has something to point at.
// ===========================================================================

#ifndef GOOP_MESH_HPP
#define GOOP_MESH_HPP

namespace goop {

// TODO: struct Mesh with just:
//           std::vector<Vec3>     vertices;
//           std::vector<uint32_t> indices;   // 3 per triangle
//       and that is genuinely all. Decide and document the WINDING ORDER right
//       here, because the STL writer has to agree with it or every normal in
//       the exported file points the wrong way.

// TODO: a couple of cheap invariant helpers the tests can lean on:
//       indices.size() % 3 == 0, and every index < vertices.size().

} // namespace goop

#endif // GOOP_MESH_HPP
