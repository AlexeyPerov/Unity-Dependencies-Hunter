using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace DependenciesHunter
{
    public class DependenciesMapUtilities
    {
        public static void FillReverseDependenciesMap(IReadOnlyList<string> assetPaths,
            out Dictionary<string, List<string>> reverseDependencies, bool recursive, string progressBarInfo)
        {
            reverseDependencies = assetPaths.ToDictionary(assetPath => assetPath, assetPath => new List<string>());

            for (var i = 0; i < assetPaths.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Dependencies Hunter", progressBarInfo, (float) i / assetPaths.Count);

                var assetDependencies = AssetDatabase.GetDependencies(assetPaths[i], recursive);

                foreach (var assetDependency in assetDependencies)
                {
                    if (assetDependency != assetPaths[i])
                    {
                        reverseDependencies[assetDependency].Add(assetPaths[i]);
                    }
                }
            }
        }
    }
}