using Match3.Engine.Demo;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralSignedQaBootstrapPlayModeTests
    {
        [Test]
        public void M11_013_QaBootstrapAwakeGatesBeforeAnyStartOrUserInteraction()
        {
            var root = new GameObject("qa-signed-gate-instantiation");
            try
            {
                var demo = root.AddComponent<NeutralMatch3Demo>();
                var view = root.AddComponent<NeutralPatchBootstrapView>();
                var qa = root.AddComponent<NeutralSignedQaBootstrap>();

                // Awake() is synchronous and precedes Start(). This test
                // never authors or accepts a self-signed shipping fixture.
                Assert.That(demo.IsSignedContentRequired, Is.True);
                Assert.That(demo.IsBoardInputAllowed, Is.False);
                Assert.That(demo.IsPlayable, Is.False);
                Assert.That(view.HasVerifiedSession, Is.False);
                Assert.That(view.CanEnterGame, Is.False);
                Assert.That(qa.HasAuthenticatedStarter, Is.False);
                Assert.That(qa.IsFinished, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
