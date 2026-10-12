using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class RuntimeAssemblyIsolationTests
    {
        [Test]
        public void RuntimeAssemblyDoesNotReferenceUnityEditor()
        {
            var references = typeof(AssemblyMarker).Assembly.GetReferencedAssemblies();
            Assert.That(references.Any(reference => reference.Name.StartsWith("UnityEditor")), Is.False);
        }
    }
}
