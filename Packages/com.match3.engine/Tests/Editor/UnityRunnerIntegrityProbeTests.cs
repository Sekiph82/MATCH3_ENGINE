using System;
using NUnit.Framework;

namespace Match3.Engine.Tests.Editor
{
    public sealed class UnityRunnerIntegrityProbeTests
    {
        [Test]
        public void IntentionalAssertionFailureProbe()
        {
            if (Environment.GetEnvironmentVariable("M00_FORCE_TEST_FAILURE") == "1")
            {
                Assert.Fail("M00 runner-integrity probe: this assertion is intentionally failing.");
            }

            Assert.Pass("M00 runner-integrity probe is inert during the normal suite.");
        }
    }
}
