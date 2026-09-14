using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class AssemblyBoundaryTests
    {
        private static readonly IReadOnlyDictionary<string, string[]>
            AllowedProjectReferences =
                new Dictionary<string, string[]>
                {
                    ["FPS.Determinism"] = Array.Empty<string>(),
                    ["FPS.Simulation"] = Array.Empty<string>(),
                    ["FPS.Core"] = Array.Empty<string>(),
                    ["FPS.Combat"] = new[]
                    {
                        "FPS.Core",
                        "FPS.GameplayEffects",
                        "FPS.Simulation"
                    },
                    ["FPS.GameplayEffects"] = new[]
                    {
                        "FPS.Core",
                        "FPS.Simulation"
                    },
                    ["FPS.AI"] = new[]
                    {
                        "FPS.Core",
                        "FPS.Combat",
                        "FPS.GameplayEffects"
                    },
                    ["FPS.AI.Hybrid.Shared"] = new[]
                    {
                        "FPS.AI"
                    },
                    ["FPS.AI.HybridEcs"] = new[]
                    {
                        "FPS.AI",
                        "FPS.AI.Hybrid.Shared",
                        "FPS.Performance.HybridAi"
                    },
                    ["FPS.Performance.HybridAi"] = Array.Empty<string>(),
                    ["FPS.Inventory"] = new[]
                    {
                        "FPS.Core",
                        "FPS.Combat",
                        "FPS.GameplayEffects",
                        "FPS.Determinism"
                    },
                    ["FPS.UI"] = new[]
                    {
                        "FPS.Core",
                        "FPS.Combat",
                        "FPS.GameplayEffects",
                        "FPS.AI",
                        "FPS.Inventory"
                    },
                    ["FPS.SaveGame"] = Array.Empty<string>(),
                    ["FPS.Composition"] = new[]
                    {
                        "FPS.Core",
                        "FPS.Combat",
                        "FPS.GameplayEffects",
                        "FPS.AI",
                        "FPS.AI.Hybrid.Shared",
                        "FPS.AI.HybridEcs",
                        "FPS.Performance.HybridAi",
                        "FPS.Inventory",
                        "FPS.SaveGame",
                        "FPS.UI",
                        "FPS.Determinism",
                        "FPS.Simulation"
                    }
                };

        [Test]
        public void RuntimeAssembliesUseOnlyAllowedDependencyDirections()
        {
            Dictionary<string, AssemblyDefinitionData> definitions =
                LoadRuntimeDefinitions();

            CollectionAssert.IsSubsetOf(
                AllowedProjectReferences.Keys,
                definitions.Keys,
                "Issue 27 requires every planned runtime assembly.");

            foreach (KeyValuePair<string, AssemblyDefinitionData> pair in
                     definitions)
            {
                Assert.That(
                    AllowedProjectReferences.ContainsKey(pair.Key),
                    Is.True,
                    $"Unclassified runtime assembly: {pair.Key}");
                HashSet<string> allowed = new(
                    AllowedProjectReferences[pair.Key],
                    StringComparer.Ordinal);

                foreach (string reference in pair.Value.references ??
                         Array.Empty<string>())
                {
                    string dependency = ResolveReferenceName(reference);

                    if (!dependency.StartsWith(
                            "FPS.",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Assert.That(
                        allowed.Contains(dependency),
                        Is.True,
                        $"{pair.Key} must not reference {dependency}.");
                }
            }

            AssertAcyclic(definitions);
        }

        [Test]
        public void DomainSlicesCompileIntoTheirNamedAssemblies()
        {
            AssertAssembly<GameplayLockState>("FPS.Core");
            AssertAssembly<Health>("FPS.Combat");
            AssertAssembly<EnemyAwarenessStateMachine>("FPS.AI");
            AssertAssembly<InventoryState>("FPS.Inventory");
            AssertAssembly<FPS.SaveGame.RunSnapshot>("FPS.SaveGame");
            AssertAssembly<FPS.Determinism.DeterministicRun>(
                "FPS.Determinism");
            AssertAssembly<FPS.Simulation.RunSimulationKernel>(
                "FPS.Simulation");
            AssertAssembly<SafeAreaFitter>("FPS.UI");
            AssertAssembly(
                typeof(GameplayEffectsModule),
                "FPS.GameplayEffects");
        }

        [Test]
        public void EveryProjectRuntimeSourceHasExplicitAssemblyOwnership()
        {
            string scriptsRoot = Path.Combine(Application.dataPath, "Scripts");

            foreach (string sourcePath in Directory.GetFiles(
                         scriptsRoot,
                         "*.cs",
                         SearchOption.AllDirectories))
            {
                string definitionPath = FindNearestAssemblyDefinition(
                    Path.GetDirectoryName(sourcePath),
                    scriptsRoot);
                Assert.That(
                    definitionPath,
                    Is.Not.Null,
                    $"Runtime source has no asmdef owner: {sourcePath}");
                AssemblyDefinitionData definition = ReadDefinition(
                    definitionPath);
                StringAssert.StartsWith(
                    "FPS.",
                    definition.name,
                    $"Runtime source escaped FPS assemblies: {sourcePath}");
            }
        }

        [Test]
        public void RuntimeAndTestsDoNotHardcodeLegacyAssemblyName()
        {
            string forbidden = "Assembly-" + "CSharp";
            string[] roots =
            {
                Path.Combine(Application.dataPath, "Scripts"),
                Path.Combine(Application.dataPath, "Tests")
            };

            foreach (string root in roots)
            {
                foreach (string sourcePath in Directory.GetFiles(
                             root,
                             "*.cs",
                             SearchOption.AllDirectories))
                {
                    StringAssert.DoesNotContain(
                        forbidden,
                        File.ReadAllText(sourcePath),
                        $"Use an explicit module reference or " +
                        $"RuntimeTypeResolver: {sourcePath}");
                }
            }
        }

        [Test]
        public void MigratedPlayerCombatConsumersDoNotUseGlobalResolution()
        {
            string scriptsRoot = Path.Combine(Application.dataPath, "Scripts");
            string[] migratedConsumers =
            {
                "Player/PlayerCombatController.cs",
                "Player/PlayerCombatFeedbackController.cs",
                "Player/PlayerVitalsHudPresenter.cs",
                "Player/UnifiedGameHudBootstrap.cs",
                "Combat/Weapons/WeaponController.cs"
            };
            string[] forbiddenGlobalResolution =
            {
                "Resources.Load",
                "GameObject.Find",
                "FindAnyObjectByType",
                "FindFirstObjectByType",
                "FindObjectOfType"
            };

            foreach (string relativePath in migratedConsumers)
            {
                string sourcePath = Path.Combine(scriptsRoot, relativePath);
                string source = File.ReadAllText(sourcePath);

                foreach (string forbidden in forbiddenGlobalResolution)
                {
                    StringAssert.DoesNotContain(
                        forbidden,
                        source,
                        $"Composition root must inject combat services: " +
                        sourcePath);
                }
            }

            string[] dynamicallyWiredConsumers =
            {
                "Player/PlayerCombatController.cs",
                "Player/PlayerCombatFeedbackController.cs"
            };

            foreach (string relativePath in dynamicallyWiredConsumers)
            {
                StringAssert.DoesNotContain(
                    "AddComponent<",
                    File.ReadAllText(Path.Combine(scriptsRoot, relativePath)),
                    $"Runtime component creation belongs to " +
                    "PlayerCombatCompositionRoot");
            }
        }

        private static Dictionary<string, AssemblyDefinitionData>
            LoadRuntimeDefinitions()
        {
            string scriptsRoot = Path.Combine(Application.dataPath, "Scripts");
            return Directory.GetFiles(
                    scriptsRoot,
                    "*.asmdef",
                    SearchOption.AllDirectories)
                .Select(ReadDefinition)
                .ToDictionary(
                    definition => definition.name,
                    definition => definition,
                    StringComparer.Ordinal);
        }

        private static AssemblyDefinitionData ReadDefinition(string path)
        {
            return JsonUtility.FromJson<AssemblyDefinitionData>(
                File.ReadAllText(path));
        }

        private static string ResolveReferenceName(string reference)
        {
            const string GuidPrefix = "GUID:";

            if (!reference.StartsWith(GuidPrefix, StringComparison.Ordinal))
            {
                return reference;
            }

            string assetPath = AssetDatabase.GUIDToAssetPath(
                reference.Substring(GuidPrefix.Length));
            return string.IsNullOrEmpty(assetPath)
                ? reference
                : ReadDefinition(assetPath).name;
        }

        private static void AssertAcyclic(
            IReadOnlyDictionary<string, AssemblyDefinitionData> definitions)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);

            foreach (string assemblyName in definitions.Keys)
            {
                Visit(assemblyName, definitions, visiting, visited);
            }
        }

        private static void Visit(
            string assemblyName,
            IReadOnlyDictionary<string, AssemblyDefinitionData> definitions,
            ISet<string> visiting,
            ISet<string> visited)
        {
            if (visited.Contains(assemblyName))
            {
                return;
            }

            Assert.That(
                visiting.Add(assemblyName),
                Is.True,
                $"Assembly dependency cycle includes {assemblyName}.");

            AssemblyDefinitionData definition = definitions[assemblyName];

            foreach (string reference in definition.references ??
                     Array.Empty<string>())
            {
                string dependency = ResolveReferenceName(reference);

                if (definitions.ContainsKey(dependency))
                {
                    Visit(dependency, definitions, visiting, visited);
                }
            }

            visiting.Remove(assemblyName);
            visited.Add(assemblyName);
        }

        private static void AssertAssembly<T>(string expected)
        {
            AssertAssembly(typeof(T), expected);
        }

        private static void AssertAssembly(Type type, string expected)
        {
            Assert.That(
                type.Assembly.GetName().Name,
                Is.EqualTo(expected));
        }

        private static string FindNearestAssemblyDefinition(
            string directory,
            string scriptsRoot)
        {
            string current = directory;

            while (!string.IsNullOrEmpty(current) &&
                   current.StartsWith(scriptsRoot, StringComparison.Ordinal))
            {
                string[] definitions = Directory.GetFiles(
                    current,
                    "*.asmdef",
                    SearchOption.TopDirectoryOnly);
                Assert.That(
                    definitions.Length,
                    Is.LessThanOrEqualTo(1),
                    $"Multiple asmdefs compete for {directory}.");

                if (definitions.Length == 1)
                {
                    return definitions[0];
                }

                current = Path.GetDirectoryName(current);
            }

            return null;
        }

        [Serializable]
        private sealed class AssemblyDefinitionData
        {
            public string name;
            public string[] references;
        }
    }
}
