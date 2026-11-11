// KPIDashboardGateTests.cs — NUnit only; no Unity runtime dependency
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class KPIDashboardGateTests
    {
        // ---- DashboardSpec argument guards ----

        [Test]
        public void DashboardSpec_NullGameId_Throws()
            => Assert.Throws<ArgumentException>(() =>
                DashboardSpec.Create(null, DashboardScope.PerGame, false));

        [Test]
        public void DashboardSpec_EmptyGameId_Throws()
            => Assert.Throws<ArgumentException>(() =>
                DashboardSpec.Create(string.Empty, DashboardScope.PerGame, false));

        // ---- DashboardPolicy argument guards ----

        [Test]
        public void DashboardPolicy_NullDescription_Throws()
            => Assert.Throws<ArgumentException>(() => DashboardPolicy.Safe(null));

        [Test]
        public void DashboardPolicy_EmptyDescription_Throws()
            => Assert.Throws<ArgumentException>(() => DashboardPolicy.Safe(string.Empty));

        // ---- DashboardSpec Create factory ----

        [Test]
        public void Create_SetsAllProperties()
        {
            var spec = DashboardSpec.Create("GameA", DashboardScope.PerGame, true);
            Assert.AreEqual("GameA", spec.GameId);
            Assert.AreEqual(DashboardScope.PerGame, spec.Scope);
            Assert.IsTrue(spec.IncludeRevenueMetrics);
        }

        // ---- IsAggregate / IsPerGame for all 3 scope values ----

        [Test]
        public void PerGame_IsPerGameTrue_IsAggregateFalse()
        {
            var spec = DashboardSpec.Create("G", DashboardScope.PerGame, false);
            Assert.IsTrue(spec.IsPerGame);
            Assert.IsFalse(spec.IsAggregate);
        }

        [Test]
        public void Aggregate_IsAggregateTrue_IsPerGameFalse()
        {
            var spec = DashboardSpec.Create("G", DashboardScope.Aggregate, false);
            Assert.IsTrue(spec.IsAggregate);
            Assert.IsFalse(spec.IsPerGame);
        }

        [Test]
        public void BothPerGameAndAggregate_BothTrue()
        {
            var spec = DashboardSpec.Create("G", DashboardScope.BothPerGameAndAggregate, false);
            Assert.IsTrue(spec.IsAggregate);
            Assert.IsTrue(spec.IsPerGame);
        }

        // ---- DashboardPolicy Safe / Default ----

        [Test]
        public void Safe_DisablesRevenueAndAggregate()
        {
            var p = DashboardPolicy.Safe("desc");
            Assert.IsFalse(p.AllowRevenueInDashboard);
            Assert.IsFalse(p.AllowAggregateDashboard);
            Assert.AreEqual("desc", p.PolicyDescription);
        }

        [Test]
        public void Default_HasCorrectDescription()
        {
            var p = DashboardPolicy.Default;
            Assert.IsFalse(p.AllowRevenueInDashboard);
            Assert.IsFalse(p.AllowAggregateDashboard);
            Assert.AreEqual("Revenue and aggregate dashboard metrics disabled until authorized", p.PolicyDescription);
        }

        // ---- Guard 1: Wonder_Pop blocked (Ordinal exact case) ----

        [Test]
        public void WonderPop_ExactCase_Blocked()
        {
            var spec = DashboardSpec.Create("Wonder_Pop", DashboardScope.PerGame, false);
            var (eligible, reason) = KPIDashboardGate.CanPublishDashboard("Wonder_Pop", spec, DashboardPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopDashboardProhibited", reason);
        }

        [Test]
        public void WonderPop_LowercaseNotBlocked_OrdinalCheck()
        {
            var spec = DashboardSpec.Create("wonder_pop", DashboardScope.PerGame, false);
            var (eligible, _) = KPIDashboardGate.CanPublishDashboard("wonder_pop", spec, DashboardPolicy.Default);
            // "wonder_pop" != "Wonder_Pop" (Ordinal) — Guard 1 must NOT fire
            // PerGame, no revenue, Safe policy → eligible
            Assert.IsTrue(eligible);
        }

        // ---- Guard 2: RevenueDashboardDisabled ----

        [Test]
        public void RevenueMetrics_WithoutPolicy_Blocked()
        {
            var spec = DashboardSpec.Create("GameB", DashboardScope.PerGame, true);
            var (eligible, reason) = KPIDashboardGate.CanPublishDashboard("GameB", spec, DashboardPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("RevenueDashboardDisabled", reason);
        }

        // ---- Guard 3: AggregateDashboardDisabled ----

        [Test]
        public void AggregateScope_WithoutPolicy_Blocked()
        {
            var spec = DashboardSpec.Create("GameC", DashboardScope.Aggregate, false);
            var (eligible, reason) = KPIDashboardGate.CanPublishDashboard("GameC", spec, DashboardPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("AggregateDashboardDisabled", reason);
        }

        [Test]
        public void BothScope_WithoutPolicy_Blocked()
        {
            var spec = DashboardSpec.Create("GameC", DashboardScope.BothPerGameAndAggregate, false);
            var (eligible, reason) = KPIDashboardGate.CanPublishDashboard("GameC", spec, DashboardPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("AggregateDashboardDisabled", reason);
        }

        // ---- PerGame no revenue with Safe policy → eligible ----

        [Test]
        public void PerGame_NoRevenue_SafePolicy_Eligible()
        {
            var spec = DashboardSpec.Create("GameD", DashboardScope.PerGame, false);
            var (eligible, reason) = KPIDashboardGate.CanPublishDashboard("GameD", spec, DashboardPolicy.Default);
            Assert.IsTrue(eligible);
            Assert.AreEqual(string.Empty, reason);
        }

        // ---- IsAggregateScope and IsPerGameScope helpers ----

        [Test]
        public void IsAggregateScope_ReturnsSpecIsAggregate()
        {
            var spec = DashboardSpec.Create("G", DashboardScope.Aggregate, false);
            Assert.IsTrue(KPIDashboardGate.IsAggregateScope(spec));
        }

        [Test]
        public void IsAggregateScope_FalseForPerGame()
        {
            var spec = DashboardSpec.Create("G", DashboardScope.PerGame, false);
            Assert.IsFalse(KPIDashboardGate.IsAggregateScope(spec));
        }

        [Test]
        public void IsPerGameScope_ReturnsSpecIsPerGame()
        {
            var spec = DashboardSpec.Create("G", DashboardScope.PerGame, false);
            Assert.IsTrue(KPIDashboardGate.IsPerGameScope(spec));
        }

        [Test]
        public void IsPerGameScope_FalseForAggregate()
        {
            var spec = DashboardSpec.Create("G", DashboardScope.Aggregate, false);
            Assert.IsFalse(KPIDashboardGate.IsPerGameScope(spec));
        }

        // ---- DashboardLog ----

        [Test]
        public void DashboardLog_Record_IncreasesCount()
        {
            var log = new DashboardLog();
            log.Record("GameX", DashboardScope.PerGame);
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void DashboardLog_HasEntry_TrueAfterRecord()
        {
            var log = new DashboardLog();
            log.Record("GameX", DashboardScope.Aggregate);
            Assert.IsTrue(log.HasEntry("GameX"));
        }

        [Test]
        public void DashboardLog_HasEntry_FalseForUnrecorded()
        {
            var log = new DashboardLog();
            Assert.IsFalse(log.HasEntry("GameX"));
        }

        [Test]
        public void DashboardLog_Clear_ResetsCount()
        {
            var log = new DashboardLog();
            log.Record("GameX", DashboardScope.PerGame);
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void DashboardLog_NullGameId_Throws()
        {
            var log = new DashboardLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, DashboardScope.PerGame));
        }

        [Test]
        public void DashboardLog_EmptyGameId_Throws()
        {
            var log = new DashboardLog();
            Assert.Throws<ArgumentException>(() => log.Record(string.Empty, DashboardScope.PerGame));
        }
    }
}
