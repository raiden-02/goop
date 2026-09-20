namespace Goop {
    // ======================================================================
    // Sdf - the static entry point. Every Goop expression starts here.
    //
    // This class exists purely so that the reading order of the fluent API
    // matches the reading order of the sentence:
    //
    //     using var shape = Sdf.Sphere(1.0)
    //                          .SmoothUnion(Sdf.Box(0.8, 1.4, 0.8), 0.3);
    //
    // Primitives are static factory methods here; everything that COMBINES or
    // TRANSFORMS shapes is an instance method on Shape. That split is the whole
    // design: factories start a chain, instance methods continue it.
    // ======================================================================

    // TODO: a public static class Sdf with one factory per native primitive:
    //
    //         Sphere(double radius)
    //         Box(double sizeX, double sizeY, double sizeZ)
    //         Torus(double majorRadius, double minorRadius)
    //         Cylinder(double radius, double height)
    //
    //       Each one calls the matching goop_shape_* constructor, checks the
    //       returned status, and wraps the resulting handle in a Shape.
    //
    // TODO: decide the Box convention and then document it in the XML docs:
    //       are the three arguments FULL extents or HALF extents? The native
    //       distance function wants half extents. The example in the README
    //       reads like full sizes. Pick one, say so, and convert in exactly one
    //       place - this is the kind of ambiguity that silently produces shapes
    //       twice the intended size.
    //
    // TODO: argument validation in managed code, before the P/Invoke. A negative
    //       radius should raise ArgumentOutOfRangeException with a useful
    //       parameter name, not travel across the ABI and come back as a generic
    //       status code. The native side still validates too - defence in depth,
    //       because C# is not the only possible caller - but the good error
    //       message belongs here.
    //
    // TODO: XML doc comments on each factory. This is the surface a user meets
    //       first, so it is where IntelliSense matters most.
    //
    // TODO (stretch): Gyroid(). Not a real SDF at all - an implicit surface
    //       whose field is only a rough distance bound - which makes it a good
    //       test of whether the mesher copes with fields that lie.
}
