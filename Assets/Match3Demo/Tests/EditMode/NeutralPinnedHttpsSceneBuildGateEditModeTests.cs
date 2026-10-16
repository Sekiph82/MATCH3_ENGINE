using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Match3.Engine.Demo.Tests
{
    // Exercises the EXISTING production IProcessSceneWithReport guard with
    // a disposable in-memory scene. It never writes a scene asset, builds a
    // player, requests HTTPS, or replaces the owner's open Editor scenes.
    public sealed class NeutralPinnedHttpsSceneBuildGateEditModeTests
    {
        private const BindingFlags StaticPrivate =
            BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags InstancePrivate =
            BindingFlags.NonPublic | BindingFlags.Instance;

        private static MethodInfo ResolveShippingGate()
        {
            // The Editor build callback belongs to Assembly-CSharp-Editor;
            // this test belongs to the existing Demo EditMode asmdef. Reuse
            // the actual callback without adding another production policy.
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Match3PinnedHttpsBuildPreflight", false))
                .FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null,
                "Shipping gate must exist in the compiled Editor assembly.");
            var gate = type.GetMethod("ValidateLoadedScene", StaticPrivate);
            Assert.That(gate, Is.Not.Null,
                "The real build callback must reuse ValidateLoadedScene.");
            return gate;
        }

        private static int Audit(MethodInfo gate, Scene scene)
        {
            return (int)gate.Invoke(null, new object[]
            {
                scene, BuildTarget.StandaloneWindows64,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        private static GameObject NewNeutralRoot(Scene scene)
        {
            var root = new GameObject("isolated-signed-https-build-gate");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static NeutralPinnedHttpsPlayerBootstrap Configure(
            GameObject root, byte[] modulus, bool enable)
        {
            var player = root.AddComponent<NeutralPinnedHttpsPlayerBootstrap>();
            var type = typeof(NeutralPinnedHttpsPlayerBootstrap);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Set(type, player, "enableSignedUpdates", enable);
            Set(type, player, "gameId", "Coral_Rescue");
            Set(type, player, "channel", RemoteChannel.FamilyTest);
            Set(type, player, "approvedHttpsPrefix",
                "https://signed.qa.invalid/first-party/");
            Set(type, player, "trustedPublicKeyId", "ephemeral-editor-r24");
            Set(type, player, "trustedRsaModulusBase64",
                Convert.ToBase64String(modulus));
            Set(type, player, "keyValidFromUnixSeconds", now - 30);
            Set(type, player, "keyValidUntilUnixSeconds", now + 3600);
            return player;
        }

        private static void Set(Type type, object target,
            string name, object value)
        {
            var field = type.GetField(name, InstancePrivate);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void AssertRejected(MethodInfo gate, Scene scene)
        {
            var thrown = Assert.Throws<TargetInvocationException>(
                () => Audit(gate, scene));
            Assert.That(thrown.InnerException,
                Is.InstanceOf<BuildFailedException>(),
                "Real scene-processing callback must abort unsafe player build.");
        }

        [Test]
        public void M11_013_RealBuildGateAcceptsConfiguredActiveSignedScene()
        {
            var gate = ResolveShippingGate();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            try
            {
                using (var key = new RSACryptoServiceProvider(2048))
                {
                    var root = NewNeutralRoot(scene);
                    root.AddComponent<NeutralMatch3Demo>();
                    root.AddComponent<NeutralPatchBootstrapView>();
                    root.AddComponent<NeutralPatchSessionHost>();
                    Configure(root, key.ExportParameters(false).Modulus, true);
                    root.SetActive(true);
                    Assert.That(Audit(gate, scene), Is.EqualTo(1),
                        "Actual in-memory shipping scene is structurally valid.");
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void M11_013_RealBuildGateRejectsMissingSignedHostSibling()
        {
            var gate = ResolveShippingGate();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            try
            {
                using (var key = new RSACryptoServiceProvider(2048))
                {
                    var root = NewNeutralRoot(scene);
                    root.AddComponent<NeutralMatch3Demo>();
                    root.AddComponent<NeutralPatchBootstrapView>();
                    Configure(root, key.ExportParameters(false).Modulus, true);
                    root.SetActive(true);
                    AssertRejected(gate, scene);
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void M11_013_RealBuildGateRejectsInactiveSignedBootstrap()
        {
            var gate = ResolveShippingGate();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            try
            {
                using (var key = new RSACryptoServiceProvider(2048))
                {
                    var root = NewNeutralRoot(scene);
                    root.AddComponent<NeutralMatch3Demo>();
                    root.AddComponent<NeutralPatchBootstrapView>();
                    root.AddComponent<NeutralPatchSessionHost>();
                    Configure(root, key.ExportParameters(false).Modulus, true);
                    // Remains inactive, but requests signed HTTPS updates.
                    AssertRejected(gate, scene);
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void M11_013_RealBuildGateReportsZeroWhenNoOptedInPlayer()
        {
            var gate = ResolveShippingGate();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            try
            {
                using (var key = new RSACryptoServiceProvider(2048))
                {
                    var root = NewNeutralRoot(scene);
                    root.AddComponent<NeutralMatch3Demo>();
                    root.AddComponent<NeutralPatchBootstrapView>();
                    root.AddComponent<NeutralPatchSessionHost>();
                    Configure(root, key.ExportParameters(false).Modulus, false);
                    root.SetActive(true);
                    Assert.That(Audit(gate, scene), Is.Zero,
                        "No signed player is NOT a production acceptance PASS.");
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
