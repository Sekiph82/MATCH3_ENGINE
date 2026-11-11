// ENG-M18-010 TelemetryDebugGate tests
using NUnit.Framework;
using System;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class TelemetryDebugGateTests
    {
        // DebugSession ArgumentException on null/empty GameId
        [Test]
        public void DebugSession_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => DebugSession.Create(null, DebugMode.Disabled, false));
        }

        [Test]
        public void DebugSession_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => DebugSession.Create("", DebugMode.EventInspector, false));
        }

        // DebugPolicy ArgumentException on null/empty desc
        [Test]
        public void DebugPolicy_Safe_NullDesc_Throws()
        {
            Assert.Throws<ArgumentException>(() => DebugPolicy.Safe(null));
        }

        [Test]
        public void DebugPolicy_Safe_EmptyDesc_Throws()
        {
            Assert.Throws<ArgumentException>(() => DebugPolicy.Safe(""));
        }

        // Create factory and IsActive for all modes
        [Test]
        public void DebugSession_IsActive_Disabled_False()
        {
            var s = DebugSession.Create("Test", DebugMode.Disabled, false);
            Assert.IsFalse(s.IsActive);
        }

        [Test]
        public void DebugSession_IsActive_EventInspector_True()
        {
            var s = DebugSession.Create("Test", DebugMode.EventInspector, false);
            Assert.IsTrue(s.IsActive);
        }

        [Test]
        public void DebugSession_IsActive_SchemaValidator_True()
        {
            var s = DebugSession.Create("Test", DebugMode.SchemaValidator, false);
            Assert.IsTrue(s.IsActive);
        }

        // CanInspect for all modes
        [Test]
        public void DebugSession_CanInspect_Disabled_False()
        {
            var s = DebugSession.Create("Test", DebugMode.Disabled, false);
            Assert.IsFalse(s.CanInspect);
        }

        [Test]
        public void DebugSession_CanInspect_EventInspector_True()
        {
            var s = DebugSession.Create("Test", DebugMode.EventInspector, false);
            Assert.IsTrue(s.CanInspect);
        }

        [Test]
        public void DebugSession_CanInspect_SchemaValidator_True()
        {
            var s = DebugSession.Create("Test", DebugMode.SchemaValidator, false);
            Assert.IsTrue(s.CanInspect);
        }

        // Guard 1: Wonder_Pop blocked (Ordinal)
        [Test]
        public void CanActivateDebug_WonderPop_Blocked()
        {
            var session = DebugSession.Create("Wonder_Pop", DebugMode.EventInspector, true);
            var policy = DebugPolicy.Safe("desc");
            var (eligible, reason) = TelemetryDebugGate.CanActivateDebug("Wonder_Pop", session, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopDebugProhibited", reason);
        }

        // "wonder_pop" lowercase NOT blocked (Ordinal — case sensitive)
        [Test]
        public void CanActivateDebug_WonderPopLowercase_NotBlocked()
        {
            var session = DebugSession.Create("wonder_pop", DebugMode.EventInspector, true);
            var policy = new DebugPolicy(); // AllowDebugInProduction=false, RequireTestDeviceFlag=false by default struct
            // Use a policy that allows eligible
            var policyAllow = new DebugPolicy();
            // Build a policy with AllowDebugInProduction=false, RequireTestDeviceFlag=false via reflection is tricky
            // Use Safe but set isTestDevice=true so both guards pass
            var policyDesc = DebugPolicy.Safe("test");
            var (eligible, reason) = TelemetryDebugGate.CanActivateDebug("wonder_pop", session, policyDesc);
            // isTestDevice=true satisfies both guard 3 and 4
            Assert.IsTrue(eligible);
        }

        // Guard 2: DebugModeDisabled
        [Test]
        public void CanActivateDebug_ModeDisabled_Blocked()
        {
            var session = DebugSession.Create("Other", DebugMode.Disabled, true);
            var policy = DebugPolicy.Safe("desc");
            var (eligible, reason) = TelemetryDebugGate.CanActivateDebug("Other", session, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("DebugModeDisabled", reason);
        }

        // Guard 3: ProductionDebugNotAllowed
        [Test]
        public void CanActivateDebug_ProductionDebugNotAllowed()
        {
            var session = DebugSession.Create("Other", DebugMode.EventInspector, false);
            // Safe: AllowDebugInProduction=false, RequireTestDeviceFlag=true
            var policy = DebugPolicy.Safe("desc");
            var (eligible, reason) = TelemetryDebugGate.CanActivateDebug("Other", session, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("ProductionDebugNotAllowed", reason);
        }

        // Guard 4: TestDeviceFlagRequired (AllowDebugInProduction=true but RequireTestDeviceFlag=true, isTestDevice=false)
        // We need a policy with AllowDebugInProduction=true, RequireTestDeviceFlag=true
        // Safe() sets AllowDebugInProduction=false. Use Default (same). We need a custom one.
        // The struct only has private ctor, exposed via Safe(). Let's construct this scenario differently:
        // We can't reach Guard 4 with Safe() because Guard 3 fires first (AllowDebugInProduction=false).
        // Guard 4 is only reachable if AllowDebugInProduction=true && RequireTestDeviceFlag=true && !IsTestDevice.
        // Since we have no public ctor for that combination, we test Guard 4 is unreachable with current API.
        // Instead test the eligible path: isTestDevice=true satisfies both guard 3 and 4.
        [Test]
        public void CanActivateDebug_TestDevice_Eligible()
        {
            var session = DebugSession.Create("Other", DebugMode.EventInspector, true);
            var policy = DebugPolicy.Safe("desc");
            var (eligible, reason) = TelemetryDebugGate.CanActivateDebug("Other", session, policy);
            Assert.IsTrue(eligible);
            Assert.AreEqual("eligible", reason);
        }

        // IsDebugActive helper
        [Test]
        public void IsDebugActive_Active_ReturnsTrue()
        {
            var s = DebugSession.Create("X", DebugMode.SchemaValidator, false);
            Assert.IsTrue(TelemetryDebugGate.IsDebugActive(s));
        }

        [Test]
        public void IsDebugActive_Disabled_ReturnsFalse()
        {
            var s = DebugSession.Create("X", DebugMode.Disabled, false);
            Assert.IsFalse(TelemetryDebugGate.IsDebugActive(s));
        }

        // CanInspectEvents helper
        [Test]
        public void CanInspectEvents_EventInspector_True()
        {
            var s = DebugSession.Create("X", DebugMode.EventInspector, false);
            Assert.IsTrue(TelemetryDebugGate.CanInspectEvents(s));
        }

        [Test]
        public void CanInspectEvents_Disabled_False()
        {
            var s = DebugSession.Create("X", DebugMode.Disabled, false);
            Assert.IsFalse(TelemetryDebugGate.CanInspectEvents(s));
        }

        // DebugEventLog tests
        [Test]
        public void DebugEventLog_Record_NullGameId_Throws()
        {
            var log = new DebugEventLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "event1"));
        }

        [Test]
        public void DebugEventLog_Record_EmptyGameId_Throws()
        {
            var log = new DebugEventLog();
            Assert.Throws<ArgumentException>(() => log.Record("", "event1"));
        }

        [Test]
        public void DebugEventLog_Record_NullEventName_Throws()
        {
            var log = new DebugEventLog();
            Assert.Throws<ArgumentException>(() => log.Record("GameA", null));
        }

        [Test]
        public void DebugEventLog_Record_EmptyEventName_Throws()
        {
            var log = new DebugEventLog();
            Assert.Throws<ArgumentException>(() => log.Record("GameA", ""));
        }

        [Test]
        public void DebugEventLog_Count_AndHasEntry()
        {
            var log = new DebugEventLog();
            Assert.AreEqual(0, log.Count);
            log.Record("GameA", "ev1");
            log.Record("GameA", "ev2");
            Assert.AreEqual(2, log.Count);
            Assert.IsTrue(log.HasEntry("GameA"));
            Assert.IsFalse(log.HasEntry("GameB"));
        }

        [Test]
        public void DebugEventLog_Clear_ResetsCount()
        {
            var log = new DebugEventLog();
            log.Record("GameA", "ev1");
            log.Clear();
            Assert.AreEqual(0, log.Count);
            Assert.IsFalse(log.HasEntry("GameA"));
        }

        // Safe() and Default values correct
        [Test]
        public void DebugPolicy_Safe_CorrectValues()
        {
            var p = DebugPolicy.Safe("my desc");
            Assert.IsFalse(p.AllowDebugInProduction);
            Assert.IsTrue(p.RequireTestDeviceFlag);
            Assert.AreEqual("my desc", p.PolicyDescription);
        }

        [Test]
        public void DebugPolicy_Default_CorrectValues()
        {
            var p = DebugPolicy.Default;
            Assert.IsFalse(p.AllowDebugInProduction);
            Assert.IsTrue(p.RequireTestDeviceFlag);
            Assert.AreEqual("Debug tooling disabled in production; test device flag required", p.PolicyDescription);
        }
    }
}
