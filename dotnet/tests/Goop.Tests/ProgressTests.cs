using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// A managed lambda called from inside native code: the three ways that goes
    /// wrong (GC, routing, exceptions) plus cancellation.
    /// </summary>
    [TestClass]
    public class ProgressTests {
        private static readonly Vec3 Min = new Vec3(-1.5, -1.5, -1.5);
        private static readonly Vec3 Max = new Vec3(1.5, 1.5, 1.5);

        [TestMethod]
        public void SurvivesFullGarbageCollectionsDuringTheNativeCall() {
            // C++ holds a raw pointer to the callback's thunk for the whole call. If
            // the delegate were collectable, a collection mid-mesh would free it and
            // the next report would crash the test host. So: collect as hard as
            // possible inside every report.
            using var sphere = Sdf.Sphere(1.0);
            int reports = 0;

            using var mesh = sphere.ToMesh(Min, Max, 48, progress: _ => {
                reports++;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });

            Assert.IsTrue(reports > 40, "expected a report per slice, got " + reports);
            MeshOracle.AssertGoodClosedMesh(mesh, sphere, 3.0 / 48);
        }

        [TestMethod]
        public void ConcurrentMeshesEachGetOnlyTheirOwnReports() {
            // One static callback serves every call; user_data routes each report
            // back to the right caller. Four meshes at once must not cross wires.
            using var sphere = Sdf.Sphere(1.0);
            var results = new List<double>[4];

            Parallel.For(0, 4, i => {
                var mine = new List<double>();
                using var mesh = sphere.ToMesh(Min, Max, 24 + i * 4, progress: f => mine.Add(f));
                results[i] = mine;
            });

            foreach (List<double> r in results) {
                for (int j = 1; j < r.Count; j++) {
                    Assert.IsTrue(r[j] > r[j - 1], "a task received another task's report");
                }
                Assert.AreEqual(1.0, r[r.Count - 1]);
            }
        }

        [TestMethod]
        public void CancellingStopsPromptlyWithTheCallersToken() {
            using var sphere = Sdf.Sphere(1.0);
            using var cts = new CancellationTokenSource();
            int reports = 0;

            var ex = Assert.ThrowsExactly<OperationCanceledException>(() =>
                sphere.ToMesh(Min, Max, 64, progress: _ => {
                    reports++;
                    cts.Cancel();
                }, cancellationToken: cts.Token));

            Assert.AreEqual(1, reports, "should stop at the first report, not finish the work");
            Assert.AreEqual(cts.Token, ex.CancellationToken);
        }

        [TestMethod]
        public void ExceptionInTheCallbackReachesTheCallerIntact() {
            // Unwinding through the native mesher would be undefined behaviour. The
            // bridge catches it, the mesher stops, and ToMesh rethrows it - with the
            // original stack trace still pointing into this test's lambda.
            using var sphere = Sdf.Sphere(1.0);

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                sphere.ToMesh(Min, Max, 32, progress: _ => throw new InvalidOperationException("boom")));

            Assert.AreEqual("boom", ex.Message);
            StringAssert.Contains(ex.StackTrace, nameof(ExceptionInTheCallbackReachesTheCallerIntact));
        }
    }
}
