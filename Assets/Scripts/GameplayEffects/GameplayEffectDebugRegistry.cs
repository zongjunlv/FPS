using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.GameplayEffects
{
    public static class GameplayEffectDebugRegistry
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly List<WeakReference<GameplayEffectRuntime>>
            runtimes = new();
#endif

        internal static void Register(GameplayEffectRuntime runtime)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (runtime != null)
            {
                runtimes.Add(new WeakReference<GameplayEffectRuntime>(runtime));
            }
#endif
        }

        public static IReadOnlyList<GameplayEffectDebugTargetSnapshot>
            CaptureActiveTargets()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var grouped = new Dictionary<EntityId, TargetBuilder>();

            for (int index = runtimes.Count - 1; index >= 0; index--)
            {
                if (!runtimes[index].TryGetTarget(
                        out GameplayEffectRuntime runtime) ||
                    runtime?.Target == null)
                {
                    runtimes.RemoveAt(index);
                    continue;
                }

                GameObject target = ResolveGameObject(runtime.Target);

                if (target == null || !target.activeInHierarchy)
                {
                    continue;
                }

                EntityId id = target.GetEntityId();

                if (!grouped.TryGetValue(id, out TargetBuilder builder))
                {
                    builder = new TargetBuilder(target);
                    grouped.Add(id, builder);
                }

                builder.Runtimes.Add(runtime);
            }

            var snapshots = new List<GameplayEffectDebugTargetSnapshot>(
                grouped.Count);

            foreach (TargetBuilder builder in grouped.Values)
            {
                builder.Runtimes.Sort((left, right) => string.CompareOrdinal(
                    left.DebugChannel,
                    right.DebugChannel));
                snapshots.Add(new GameplayEffectDebugTargetSnapshot(
                    builder.Target,
                    builder.Runtimes.ToArray()));
            }

            snapshots.Sort((left, right) => string.CompareOrdinal(
                left.DisplayName,
                right.DisplayName));
            return snapshots;
#else
            return Array.Empty<GameplayEffectDebugTargetSnapshot>();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static void ClearForTesting()
        {
            runtimes.Clear();
        }

        private static GameObject ResolveGameObject(UnityEngine.Object target)
        {
            return target switch
            {
                GameObject gameObject => gameObject,
                Component component => component.gameObject,
                _ => null
            };
        }

        private sealed class TargetBuilder
        {
            public TargetBuilder(GameObject target)
            {
                Target = target;
            }

            public GameObject Target { get; }
            public List<GameplayEffectRuntime> Runtimes { get; } = new();
        }
#endif
    }
}
