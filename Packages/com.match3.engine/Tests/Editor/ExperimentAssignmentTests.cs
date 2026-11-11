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
            // Ids may contain the '|' separator. Without length prefixes both sides of each pair would hash the
            // identical material "a|b|c|p" (resp. "e|a|b|c"); with them it is "3:a|b|1:c|1:p" vs "1:a|3:b|c|1:p".
            Assert.AreNotEqual(StableBucketing.Bucket("a|b", "c", "p", int.MaxValue), StableBucketing.Bucket("a", "b|c", "p", int.MaxValue));
            Assert.AreNotEqual(StableBucketing.Bucket("e", "a|b", "c", int.MaxValue), StableBucketing.Bucket("e", "a", "b|c", int.MaxValue));
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
            // Ordinal: only the exact Wonder_Pop id triggers the title rule; near-misses and other non-catalog
            // IDs are not catalog games and fail closed as UnknownGame (checked before state/consent).
            foreach (var g in new[] { "wonder_pop", "WONDER_POP", "Unknown_Game", "coral_rescue", " Coral_Rescue" })
            {
                AssertNotEligible(ExperimentAssigner.Assign(d, Adult("p1", g)), "UnknownGame");
                AssertNotEligible(ExperimentAssigner.Assign(Def(ExperimentState.RolledBack), Adult("p1", g)), "UnknownGame");
            }
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
            var v = new[] { new ExperimentVariant("control", 1), new ExperimentVariant("treatment", 1) };
            var v1 = ExperimentDefinition.Create("exp", 1, "s", v, 0, ExperimentState.Running);
            var v2 = ExperimentDefinition.Create("exp", 2, "s", v, 0, ExperimentState.Running);
            var upper = ExperimentDefinition.Create("EXP", 1, "s", v, 0, ExperimentState.Running);
            var log = new ExposureLog();
            Assert.IsTrue(log.RecordExposure(v1, Adult("p1")));
            Assert.IsFalse(log.RecordExposure(v1, Adult("p1")));
            Assert.IsTrue(log.RecordExposure(v2, Adult("p1")));
            Assert.IsTrue(log.RecordExposure(v1, Adult("p2")));
            Assert.IsTrue(log.RecordExposure(upper, Adult("p1")), "ordinal comparison");
            Assert.AreEqual(4, log.Count);
        }

        [Test]
        public void Exposure_InvalidArgs_Throw()
        {
            var log = new ExposureLog();
            Assert.Throws<ArgumentNullException>(() => log.RecordExposure(null, Adult("p")));
            Assert.Throws<ArgumentNullException>(() => log.RecordExposure(Def(), null));
            Assert.Throws<ArgumentNullException>(() => log.VariantCounts(null));
            Assert.Throws<ArgumentException>(() => log.TryGetExposure(Def(), "", out _));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void Exposure_NeverLogsIneligiblePlayers()
        {
            var d = Def(ExperimentState.Running, 10);
            var log = new ExposureLog();
            for (int i = 0; i < 200; i++)
            {
                Assert.IsFalse(log.RecordExposure(d, new PlayerExperimentContext("Wonder_Pop", "wp" + i, false, true)));
                Assert.IsFalse(log.RecordExposure(d, new PlayerExperimentContext("Coral_Rescue", "kid" + i, true, true)));
                Assert.IsFalse(log.RecordExposure(d, new PlayerExperimentContext("Coral_Rescue", "nc" + i, false, false)));
                Assert.IsFalse(log.RecordExposure(Def(ExperimentState.RolledBack), Adult("rb" + i)));
            }
            Assert.AreEqual(0, log.Count);
            Assert.IsFalse(log.TryGetExposure(d, "kid0", out _));
            CollectionAssert.AreEqual(new long[] { 0, 0 }, log.VariantCounts(d));
            Assert.AreEqual(0, log.HoldoutCount(d));

            var adult = Adult("adult");
            Assert.IsTrue(log.RecordExposure(d, adult));
            Assert.IsFalse(log.RecordExposure(d, adult));
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void Exposure_PublicApi_HasNoUncheckedEntryPoint()
        {
            var overloads = new List<string>();
            foreach (var m in typeof(ExposureLog).GetMethods())
                if (m.Name == "RecordExposure")
                {
                    var ps = m.GetParameters();
                    overloads.Add(ps.Length == 2 ? ps[0].ParameterType.Name + "," + ps[1].ParameterType.Name : ps.Length.ToString());
                }
            CollectionAssert.AreEqual(new[] { "ExperimentDefinition,PlayerExperimentContext" }, overloads);
            Assert.IsEmpty(typeof(ExperimentAssignment).GetConstructors(), "assignments can only come from ExperimentAssigner");
            Assert.IsEmpty(typeof(ExposureRecord).GetConstructors());
        }

        [Test]
        public void Exposure_StoresServedVariant_HoldoutDistinctFromControl_CountsFeedSrm()
        {
            var d = Def(ExperimentState.Running, 10);
            var log = new ExposureLog();
            const int n = 20000;
            string holdoutPlayer = null, controlPlayer = null;
            for (int i = 0; i < n; i++)
            {
                var ctx = Adult("synthetic-" + i);
                Assert.IsTrue(log.RecordExposure(d, ctx));
                Assert.IsTrue(log.TryGetExposure(d, ctx.PlayerId, out var rec));
                var a = ExperimentAssigner.Assign(d, ctx);
                Assert.AreEqual(a.Kind, rec.Kind);
                Assert.AreEqual(a.VariantName, rec.VariantName);
                if (rec.Kind == ExperimentAssignmentKind.Holdout) holdoutPlayer = ctx.PlayerId;
                else if (rec.VariantName == "control") controlPlayer = ctx.PlayerId;
            }
            Assert.IsNotNull(holdoutPlayer);
            Assert.IsNotNull(controlPlayer);
            log.TryGetExposure(d, holdoutPlayer, out var h);
            Assert.AreEqual(ExperimentAssignmentKind.Holdout, h.Kind);
            Assert.AreEqual("control", h.VariantName, "holdout is served control but recorded as Holdout");

            var counts = log.VariantCounts(d);
            long holdout = log.HoldoutCount(d);
            Assert.AreEqual(2, counts.Count);
            Assert.AreEqual(n, counts[0] + counts[1] + holdout, "holdout is not folded into control");
            Assert.AreEqual(0.10, holdout / (double)n, 0.02);

            var srm = SampleRatioGuardrail.Check(counts, new[] { d.Variants[0].Weight, d.Variants[1].Weight });
            Assert.IsFalse(srm.IsMismatch, "balanced exposures pass SRM, chi2=" + srm.ChiSquare);
            // Folding holdout into control would skew 50/50 by ~10 points and must trip the guardrail.
            var folded = SampleRatioGuardrail.Check(new[] { counts[0] + holdout, counts[1] }, new[] { 50, 50 });
            Assert.IsTrue(folded.IsMismatch, "chi2=" + folded.ChiSquare);

            Assert.Throws<NotSupportedException>(() => ((IList<long>)counts)[0] = 0);
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
