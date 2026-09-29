using System.Collections.Generic;
using UnityEngine;

namespace FPS.Networking.Session
{
    public static class CoopEnemyPresentationCatalog
    {
        private static Dictionary<string, CoopEnemyPresentationDefinition> definitions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => definitions = null;

        public static CoopEnemyPresentationDefinition Find(string sourceAddress)
        {
            if (definitions == null)
            {
                definitions = new Dictionary<string, CoopEnemyPresentationDefinition>();
                foreach (GameObject prefab in Resources.LoadAll<GameObject>("CoopPresentation/EnemyModels"))
                {
                    var definition = prefab.GetComponent<CoopEnemyPresentationDefinition>();
                    if (definition != null && !string.IsNullOrWhiteSpace(definition.SourceAddress))
                        definitions[definition.SourceAddress] = definition;
                }
            }
            return sourceAddress != null && definitions.TryGetValue(sourceAddress, out var found) ? found : null;
        }
    }
}
