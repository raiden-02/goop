# How signed distance fields work

## A shape is a function

A **signed distance field** (SDF) is a function that takes a point in space and
returns the distance to the nearest surface of a shape: **negative** inside,
**zero** exactly on the surface, **positive** outside. A unit sphere at the
origin is just `length(p) - 1`. That is the whole representation: no vertices, no
faces, just a function you can ask about any point.

A box centred at the origin with half extents `b` is only slightly longer:

```
q = abs(p) - b
d = length(max(q, 0)) + min(max(q.x, q.y, q.z), 0)
```

`abs` folds every point into one octant, since the box is symmetric. Outside,
the first term is the distance to the nearest face, edge or corner. Inside, the
second term is the (negative) distance to the nearest face.

![A box with a sphere smooth-unioned onto its top face](../gallery/box_blob.png)

## Combining shapes is arithmetic

Because shapes are functions, constructive solid geometry reduces to one line
each:

| Operation | Distance | Reads as |
|---|---|---|
| Union | `min(a, b)` | inside if inside **either** |
| Intersection | `max(a, b)` | inside only if inside **both** |
| Subtraction | `max(a, -b)` | inside `a` and **not** inside `b` |

Moving a shape is just as simple: to evaluate a shape translated by `t` at point
`p`, evaluate the original at `p - t`. The geometry never moves. The question
does.

## Smooth union: shapes that melt

A plain `min` leaves a sharp crease where two surfaces meet. The **smooth union**
replaces it with a polynomial blend:

```
h     = clamp(0.5 + 0.5 * (b - a) / k, 0, 1)
blend = mix(b, a, h) - k * h * (1 - h)
```

Near the crease, where the two distances are within `k` of each other, the result
is blended and pulled outward, so the surfaces bulge into one another. Everywhere
else `h` clamps to 0 or 1 and the result is exactly `min(a, b)`. The blend radius
`k` controls how far the influence reaches.

Two shapes separated by a gap only join once the blend reaches across it. With
the surfaces a distance `g` apart, the field at the middle of the gap is `g/2`
from each, and the smooth union there is `g/2 - k/4`, which is negative (inside)
once `k > 2g`. Below that threshold the shapes stay apart and are only softened.
Above it they join, and the neck thickens as `k` grows:

![Smooth union at k = 0.5, 1.0 and 1.5](../gallery/blend_sweep.png)

## From a field to triangles

A field has no triangles, so it has to be meshed before it can be printed or
viewed. Goop uses **surface nets**:

1. Sample the field on a grid.
2. Find the cells whose corners disagree in sign, since the surface passes
   through them.
3. Place one vertex in each such cell.
4. Join the vertices of neighbouring cells into triangles.

It is a close relative of marching cubes that needs no case table and produces
more even triangles. How the output is verified is covered in
[design.md](design.md#testing-strategy).

## Reference

The distance functions and the smooth-minimum formula come from
**Inigo Quilez's** articles, the standard reference for this material:
[distance functions](https://iquilezles.org/articles/distfunctions/) and
[smooth minimum](https://iquilezles.org/articles/smin/).
