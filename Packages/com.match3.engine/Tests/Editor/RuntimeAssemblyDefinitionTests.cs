using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Match3.Engine.Tests.Editor
{
    public sealed class RuntimeAssemblyDefinitionTests
    {
        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string[] references;
            public string[] includePlatforms;
            public string[] optionalUnityReferences;
            public bool noEngineReferences;
        }

        [Test]
        public void RuntimeAssemblyIsEngineIndependentAndNotATestAssembly()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var asmdefPath = Path.Combine(projectRoot, "Packages", "com.match3.engine", "Runtime", "Match3.Engine.Runtime.asmdef");
            Assert.That(File.Exists(asmdefPath), Is.True, "Runtime assembly definition must be present in the embedded package.");

            var json = File.ReadAllText(asmdefPath);
            var definition = JsonUtility.FromJson<AssemblyDefinition>(json);
            Assert.That(definition.noEngineReferences, Is.True);
            Assert.That(definition.references, Is.Empty);
            Assert.That(definition.includePlatforms, Is.Empty);
            Assert.That(definition.optionalUnityReferences ?? Array.Empty<string>(), Does.Not.Contain("TestAssemblies"));
            var runtimeAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "Match3.Engine.Runtime");
            Assert.That(runtimeAssembly.GetReferencedAssemblies().Any(reference => reference.Name == "UnityEditor"), Is.False);
        }
    }
}
