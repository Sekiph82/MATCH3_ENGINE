using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralM11HttpsQaPlayerProbeEditModeTests
    {
        [TestCase(false, false, false, false, NeutralM11HttpsQaRunState.Waiting)]
        [TestCase(false, false, false, true, NeutralM11HttpsQaRunState.Failed)]
        [TestCase(true, false, false, false, NeutralM11HttpsQaRunState.Failed)]
        [TestCase(true, true, false, false, NeutralM11HttpsQaRunState.Waiting)]
        [TestCase(true, true, true, false, NeutralM11HttpsQaRunState.Passed)]
        [TestCase(true, true, true, true, NeutralM11HttpsQaRunState.Failed)]
        public void RunStateEvaluatorBoundsPendingPositiveAndNegativeChecks(
            bool negativeFinished, bool negativePassed, bool positivePassed,
            bool deadlineExpired, NeutralM11HttpsQaRunState expected)
        {
            Assert.That(NeutralM11HttpsQaRunStateEvaluator.Evaluate(
                negativeFinished, negativePassed, positivePassed,
                deadlineExpired), Is.EqualTo(expected));
        }

        [Test]
        public async Task CompletionWaitReturnsAtTimeoutForUnresponsiveTransport()
        {
            var pending = new TaskCompletionSource<object>();
            var timer = Stopwatch.StartNew();

            var completed = await NeutralM11HttpsQaPlayerProbe
                .CompletesWithinAsync(pending.Task,
                    TimeSpan.FromMilliseconds(100));

            pending.TrySetResult(null);
            Assert.That(completed, Is.False);
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        }

        [Test]
        public async Task CompletionWaitAcceptsTaskThatFinishesBeforeTimeout()
        {
            var completed = await NeutralM11HttpsQaPlayerProbe
                .CompletesWithinAsync(Task.FromResult(true),
                    TimeSpan.FromSeconds(1));

            Assert.That(completed, Is.True);
        }
    }
}
