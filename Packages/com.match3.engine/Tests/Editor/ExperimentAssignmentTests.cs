using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Tests.Editor
{
    public class ExperimentAssignmentTests
    {
        private static ExperimentDefinition Def(ExperimentState state = ExperimentState.Running, int holdout = 0,
            params ExperimentVariant[] variants)
        {
            if (variants.Length == 0)
                variants = new[] { new ExperimentVariant("control", 50), new ExperimentVariant("treatment", 50) };
            return ExperimentDefinition.Create("exp-001", 1, "salt-a", variants, holdout, state);
        }

        private static PlayerExperimentContext Adult(string playerId, string gameId = "Coral_Rescue") =>
            new PlayerExperimentContext(gameId, playerId, false, true);

        // ---- StableBucketing ----

        [Test]
        public void Fnv1a64_MatchesPublishedReferenceVectors()
        {
            Assert.AreEqual(0xcbf29ce484222325UL, StableBucketing.Fnv1a64(new byte[0]));
            Assert.AreEqual(0xaf63dc4c8601ec8cUL, StableBucketing.Fnv1a64(Encoding.ASCII.GetBytes("a")));
            Assert.AreEqual(0x85944171f73967e8UL, StableBucketing.Fnv1a64(Encoding.ASCII.GetBytes("foobar")));
        }

        [Test]
        public void Bucket_GoldenValue_IsPinned()
        {
            // FNV-1a 64 of "7:exp-001|6:salt-a|9:player-42" = 0xfec5ea6c3c3f9fd2;
            // fmix64 -> 0x0a00905f88ee6c1e; mod 10000 = 8846.
            ulong raw = StableBucketing.Fnv1a64(Encoding.UTF8.GetBytes("7:exp-001|6:salt-a|9:player-42"));
            Assert.AreEqual(0xfec5ea6c3c3f9fd2UL, raw);
            Assert.AreEqual(0x0a00905f88ee6c1eUL, StableBucketing.Mix64(raw));
            Assert.AreEqual(8846, StableBucketing.Bucket("exp-001", "salt-a", "player-42"));
            Assert.AreEqual(932, StableBucketing.Bucket("exp-001", "salt-a", "player-43"));
        }

        [Test]
        public void Bucket_SmallEvenModulus_IsNotSaltParity()
        {
            // Raw FNV-1a mod 2 is byte parity: salts "s1"/"s2" would flip every player. The finalizer must prevent that.
            int flips = 0;
            const int n = 20000;
            for (int i = 0; i < n; i++)
                if (StableBucketing.Bucket("exp", "s1", "p" + i, 2) != StableBucketing.Bucket("exp", "s2", "p" + i, 2)) flips++;
            Assert.AreEqual(0.5, flips / (double)n, 0.02);
        }

        [Test]
        public void Bucket_IsDeterministicAndInRange()
        {
            for (int i = 0; i < 1000; i++)
            {
                string id = "p" + i;
                int a = StableBucketing.Bucket("e", "s", id);
                Assert.AreEqual(a, StableBucketing.Bucket("e", "s", id));
                Assert.That(a, Is.InRange(0, 9999));
                Assert.That(StableBucketing.Bucket("e", "s", id, 7), Is.InRange(0, 6));
            }
        }

        [Test]
        public void Bucket_LengthPrefixPreventsConcatenationCollision()
        {
            // Without length prefixes "ab"+"c" and "a"+"bc" would hash identical material.
            Assert.AreNotEqual(StableBucketing.Bucket("ab", "c", "p", int.MaxValue), StableBucketing.Bucket("a", "bc", "p", int.MaxValue));
        }

        [Test]
        public void Bucket_InvalidInput_Throws()
        {
            Assert.Throws<ArgumentException>(() => StableBucketing.Bucket(null, "s", "p"));
            Assert.Throws<ArgumentException>(() => StableBucketing.Bucket("e", "", "p"));
            Assert.Throws<ArgumentException>(() => StableBucketing.Bucket("e", "s", ""));
            Assert.Throws<ArgumentException>(() => StableBucketing.Bucket("e", "s", "\uD800"));
            Assert.Throws<ArgumentOutOfRangeException>(() => StableBucketing.Bucket("e", "s", "p", 0));
        }

        // ---- ExperimentDefinition validation ----

        [Test]
        public void Definition_Valid_ExposesControlAndWeights()
        {
            var d = Def();
            Assert.AreEqual("control", d.ControlVariant);
            Assert.AreEqual(100, d.TotalWeight);
            Assert.AreEqual(2, d.Variants.Count);
        }

        [Test]
        public void Definition_InvalidInputs_Throw()
        {
            var two = new[] { new ExperimentVariant("c", 1), new ExperimentVariant("t", 1) };
            Assert.Throws<ArgumentException>(() => ExperimentDefinition.Create("", 1, "s", two, 0, ExperimentState.Running));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentDefinition.Create("e", 0, "s", two, 0, ExperimentState.Running));
            Assert.Throws<ArgumentException>(() => ExperimentDefinition.Create("e", 1, "", two, 0, ExperimentState.Running));
            Assert.Throws<ArgumentException>(() => ExperimentDefinition.Create("e", 1, "s", null, 0, ExperimentState.Running));
            Assert.Throws<ArgumentException>(() => ExperimentDefinition.Create("e", 1, "s", new[] { new ExperimentVariant("c", 1) }, 0, ExperimentState.Running));
            Assert.Throws<ArgumentException>(() => ExperimentDefinition.Create("e", 1, "s", new[] { new ExperimentVariant("c", 1), new ExperimentVariant("c", 1) }, 0, ExperimentState.Running));
            Assert.Throws<ArgumentException>(() => ExperimentDefinition.Create("e", 1, "s", new[] { new ExperimentVariant("c", 1), new ExperimentVariant("", 1) }, 0, ExperimentState.Running));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentDefinition.Create("e", 1, "s", new[] { new ExperimentVariant("c", 1), new ExperimentVariant("t", 0) }, 0, ExperimentState.Running));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentDefinition.Create("e", 1, "s", new[] { new ExperimentVariant("c", -5), new ExperimentVariant("t", 1) }, 0, ExperimentState.Running));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentDefinition.Create("e", 1, "s", new[] { new ExperimentVariant("c", int.MaxValue), new ExperimentVariant("t", 1) }, 0, ExperimentState.Running));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentDefinition.Create("e", 1, "s", two, -1, ExperimentState.Running));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentDefinition.Create("e", 1, "s", two, 51, ExperimentState.Running));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentDefinition.Create("e", 1, "s", two, 0, (ExperimentState)99));
        }

        [Test]
        public void Definition_VariantListIsImmutableCopy()
        {
            var src = new List<ExperimentVariant> { new ExperimentVariant("c", 1), new ExperimentVariant("t", 1) };
            var d = ExperimentDefinition.Create("e", 1, "s", src, 0, ExperimentState.Running);
            src.Add(new ExperimentVariant("x", 1));
            Assert.AreEqual(2, d.Variants.Count);
            Assert.Throws<NotSupportedException>(() => ((IList<ExperimentVariant>)d.Variants).Add(new ExperimentVariant("y", 1)));
        }

        // ---- Assignment ----

        [Test]
        public void Assign_SamePlayer_AlwaysSameResult()
        {
            var d = Def();
            for (int i = 0; i < 500; i++)
            {
                var ctx = Adult("player-" + i);
                var a = ExperimentAssigner.Assign(d, ctx);
                var b = ExperimentAssigner.Assign(Def(), ctx);
                Assert.AreEqual(a.Kind, b.Kind);
                Assert.AreEqual(a.VariantName, b.VariantName);
            }
        }

        [Test]
        public void Assign_VersionBumpDoesNotReshuffle_SaltChangeDoes()
        {
            var v = new[] { new ExperimentVariant("control", 1), new ExperimentVariant("treatment", 1) };
            var d1 = ExperimentDefinition.Create("exp", 1, "s1", v, 0, ExperimentState.Running);
            var d2 = ExperimentDefinition.Create("exp", 2, "s1", v, 0, ExperimentState.Running);
            var d3 = ExperimentDefinition.Create("exp", 1, "s2", v, 0, ExperimentState.Running);
            int changedBySalt = 0;
            for (int i = 0; i < 1000; i++)
            {
                var ctx = Adult("p" + i);
                Assert.AreEqual(ExperimentAssigner.Assign(d1, ctx).VariantName, ExperimentAssigner.Assign(d2, ctx).VariantName);
                if (ExperimentAssigner.Assign(d1, ctx).VariantName != ExperimentAssigner.Assign(d3, ctx).VariantName) changedBySalt++;
            }
            Assert.That(changedBySalt, Is.InRange(400, 600), "a new salt re-randomizes about half the players");
        }

        [Test]
        public void Assign_Distribution_WithinTwoPercentOfWeights()
        {
            var d = Def(ExperimentState.Running, 0,
                new ExperimentVariant("control", 50), new ExperimentVariant("b", 30), new ExperimentVariant("c", 20));
            var counts = new Dictionary<string, int> { { "control", 0 }, { "b", 0 }, { "c", 0 } };
            const int n = 100000;
            for (int i = 0; i < n; i++)
            {
                var a = ExperimentAssigner.Assign(d, Adult("synthetic-" + i));
                Assert.AreEqual(ExperimentAssignmentKind.Variant, a.Kind);
                counts[a.VariantName]++;
            }
            Assert.AreEqual(0.50, counts["control"] / (double)n, 0.02);
            Assert.AreEqual(0.30, counts["b"] / (double)n, 0.02);
            Assert.AreEqual(0.20, counts["c"] / (double)n, 0.02);

            var srm = SampleRatioGuardrail.Check(new long[] { counts["control"], counts["b"], counts["c"] }, new[] { 50, 30, 20 });
            Assert.IsFalse(srm.IsMismatch, "assigner output must pass its own SRM guardrail, chi2=" + srm.ChiSquare);
        }

        [Test]
        public void Assign_Holdout_ShareMatchesPercentAndServesControl()
        {
            var d = Def(ExperimentState.Running, 10);
            int holdout = 0;
            var treatmentWhenNotHeld = 0;
            const int n = 100000;
            for (int i = 0; i < n; i++)
            {
                var a = ExperimentAssigner.Assign(d, Adult("synthetic-" + i));
                if (a.Kind == ExperimentAssignmentKind.Holdout)
                {
                    holdout++;
                    Assert.AreEqual("control", a.VariantName);
                    Assert.AreEqual("Holdout", a.Reason);
                }
                else if (a.VariantName == "treatment") treatmentWhenNotHeld++;
            }
            Assert.AreEqual(0.10, holdout / (double)n, 0.02);
            Assert.AreEqual(0.5, treatmentWhenNotHeld / (double)(n - holdout), 0.02, "variant split is independent of holdout");
        }

        [Test]
        public void Assign_RolledBack_EveryoneControl()
        {
            var running = Def();
            var rolledBack = Def(ExperimentState.RolledBack);
            int sawTreatmentWhileRunning = 0;
            for (int i = 0; i < 200; i++)
            {
                var ctx = Adult("p" + i);
                if (ExperimentAssigner.Assign(running, ctx).VariantName == "treatment") sawTreatmentWhileRunning++;
                var a = ExperimentAssigner.Assign(rolledBack, ctx);
                Assert.AreEqual(ExperimentAssignmentKind.NotEligible, a.Kind);
                Assert.AreEqual("control", a.VariantName);
                Assert.AreEqual("RolledBack", a.Reason);
            }
            Assert.Greater(sawTreatmentWhileRunning, 0, "precondition: rollback actually changes some players");
        }

        [TestCase(ExperimentState.Draft)]
        [TestCase(ExperimentState.Completed)]
        public void Assign_NotRunning_ServesControl(ExperimentState state)
        {
            var a = ExperimentAssigner.Assign(Def(state), Adult("p1"));
            Assert.AreEqual(ExperimentAssignmentKind.NotEligible, a.Kind);
            Assert.AreEqual("control", a.VariantName);
            Assert.AreEqual("NotRunning", a.Reason);
        }

        [Test]
        public void Assign_WonderPop_ChildAndNoConsent_NotEligibleControl()
        {
            var d = Def();
            for (int i = 0; i < 200; i++)
            {
                string p = "p" + i;
                AssertNotEligible(ExperimentAssigner.Assign(d, new PlayerExperimentContext("Wonder_Pop", p, false, true)), "WonderPopNoTargeting");
                AssertNotEligible(ExperimentAssigner.Assign(d, new PlayerExperimentContext("Coral_Rescue", p, true, true)), "ChildProfileNoTargeting");
                AssertNotEligible(ExperimentAssigner.Assign(d, new PlayerExperimentContext("Coral_Rescue", p, false, false)), "NoConsent");
            }
            // Ordinal: only the exact Wonder_Pop id triggers the title rule; a near-miss still needs consent etc.
            Assert.AreNotEqual("WonderPopNoTargeting", ExperimentAssigner.Assign(d, Adult("p1", "wonder_pop")).Reason);
        }

        private static void AssertNotEligible(ExperimentAssignment a, string reason)
        {
            Assert.AreEqual(ExperimentAssignmentKind.NotEligible, a.Kind);
            Assert.AreEqual("control", a.VariantName);
            Assert.AreEqual(reason, a.Reason);
        }

        [Test]
        public void Assign_NullArgs_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => ExperimentAssigner.Assign(null, Adult("p")));
            Assert.Throws<ArgumentNullException>(() => ExperimentAssigner.Assign(Def(), null));
            Assert.Throws<ArgumentException>(() => new PlayerExperimentContext("", "p", false, true));
            Assert.Throws<ArgumentException>(() => new PlayerExperimentContext("g", null, false, true));
        }

        // ---- Exposure ----

        [Test]
        public void Exposure_IsIdempotentPerExperimentVersionPlayer()
        {
            var log = new ExposureLog();
            Assert.IsTrue(log.RecordExposure("exp", 1, "p1", "control"));
            Assert.IsFalse(log.RecordExposure("exp", 1, "p1", "control"));
            Assert.IsFalse(log.RecordExposure("exp", 1, "p1", "treatment"), "re-exposure with a different variant is still a repeat");
            Assert.IsTrue(log.RecordExposure("exp", 2, "p1", "control"));
            Assert.IsTrue(log.RecordExposure("exp", 1, "p2", "control"));
            Assert.IsTrue(log.RecordExposure("EXP", 1, "p1", "control"), "ordinal comparison");
            Assert.AreEqual(4, log.Count);
        }

        [Test]
        public void Exposure_InvalidArgs_Throw()
        {
            var log = new ExposureLog();
            Assert.Throws<ArgumentException>(() => log.RecordExposure("", 1, "p", "v"));
            Assert.Throws<ArgumentOutOfRangeException>(() => log.RecordExposure("e", 0, "p", "v"));
            Assert.Throws<ArgumentException>(() => log.RecordExposure("e", 1, "", "v"));
            Assert.Throws<ArgumentException>(() => log.RecordExposure("e", 1, "p", null));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void Exposure_FromAssignment_NeverLogsIneligiblePlayers()
        {
            var d = Def();
            var log = new ExposureLog();
            var child = new PlayerExperimentContext("Coral_Rescue", "kid", true, true);
            var noConsent = new PlayerExperimentContext("Coral_Rescue", "nc", false, false);
            var wp = new PlayerExperimentContext("Wonder_Pop", "wp", false, true);
            Assert.IsFalse(log.RecordExposure(d, child.PlayerId, ExperimentAssigner.Assign(d, child)));
            Assert.IsFalse(log.RecordExposure(d, noConsent.PlayerId, ExperimentAssigner.Assign(d, noConsent)));
            Assert.IsFalse(log.RecordExposure(d, wp.PlayerId, ExperimentAssigner.Assign(d, wp)));
            Assert.AreEqual(0, log.Count);

            var adult = Adult("adult");
            Assert.IsTrue(log.RecordExposure(d, adult.PlayerId, ExperimentAssigner.Assign(d, adult)));
            Assert.IsFalse(log.RecordExposure(d, adult.PlayerId, ExperimentAssigner.Assign(d, adult)));
            Assert.AreEqual(1, log.Count);
        }

        // ---- SRM guardrail ----

        [Test]
        public void Srm_Balanced_NoMismatch()
        {
            var r = SampleRatioGuardrail.Check(new long[] { 10050, 9950 }, new[] { 50, 50 });
            Assert.AreEqual(1, r.DegreesOfFreedom);
            Assert.AreEqual(0.5, r.ChiSquare, 1e-9); // 2 * 50^2 / 10000
            Assert.IsFalse(r.IsMismatch);
        }

        [Test]
        public void Srm_Skewed_Mismatch()
        {
            var r = SampleRatioGuardrail.Check(new long[] { 10500, 9500 }, new[] { 50, 50 });
            Assert.AreEqual(50.0, r.ChiSquare, 1e-9);
            Assert.IsTrue(r.IsMismatch);
        }

        [Test]
        public void Srm_UsesDfSpecificCriticalValue()
        {
            // df=2 critical 13.816. Expected 5000/3000/2000 at 50/30/20.
            // 5150/2925/1925 -> chi2 = 4.5 + 1.875 + 2.8125 = 9.1875 < 13.816 -> no mismatch
            var ok = SampleRatioGuardrail.Check(new long[] { 5150, 2925, 1925 }, new[] { 50, 30, 20 });
            Assert.AreEqual(2, ok.DegreesOfFreedom);
            Assert.AreEqual(9.1875, ok.ChiSquare, 1e-9);
            Assert.IsFalse(ok.IsMismatch);
            // 5200/2900/1900 -> 8 + 3.333.. + 5 = 16.333.. > 13.816
            var bad = SampleRatioGuardrail.Check(new long[] { 5200, 2900, 1900 }, new[] { 50, 30, 20 });
            Assert.IsTrue(bad.IsMismatch);
        }

        [Test]
        public void Srm_ZeroTotal_NoMismatch()
        {
            var r = SampleRatioGuardrail.Check(new long[] { 0, 0 }, new[] { 1, 1 });
            Assert.AreEqual(0.0, r.ChiSquare);
            Assert.IsFalse(r.IsMismatch);
        }

        [Test]
        public void Srm_InvalidInputs_Throw()
        {
            var eleven = new long[11];
            var elevenW = new int[11];
            for (int i = 0; i < 11; i++) elevenW[i] = 1;
            Assert.Throws<ArgumentOutOfRangeException>(() => SampleRatioGuardrail.Check(eleven, elevenW));
            Assert.DoesNotThrow(() => SampleRatioGuardrail.Check(new long[10], new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }));
            Assert.Throws<ArgumentException>(() => SampleRatioGuardrail.Check(new long[] { 1, 2 }, new[] { 1 }));
            Assert.Throws<ArgumentException>(() => SampleRatioGuardrail.Check(new long[] { 1 }, new[] { 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => SampleRatioGuardrail.Check(new long[] { -1, 2 }, new[] { 1, 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => SampleRatioGuardrail.Check(new long[] { 1, 2 }, new[] { 1, 0 }));
            Assert.Throws<ArgumentNullException>(() => SampleRatioGuardrail.Check(null, new[] { 1, 1 }));
        }
    }
}
