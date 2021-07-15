using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace DependenciesHunter
{
    public static class DependenciesMapUtilities
    {
        public static void FillReverseDependenciesMap(out Dictionary<string, List<string>> reverseDependencies)
        {
            var assetPaths = AssetDatabase.GetAllAssetPaths().ToList();

            reverseDependencies = assetPaths.ToDictionary(assetPath => assetPath, assetPath => new List<string>());

            Debug.Log($"Total Assets Count: {assetPaths.Count}");

            for (var i = 0; i < assetPaths.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Dependencies Hunter", "Creating a map of dependencies",
                    (float) i / assetPaths.Count);

                var assetDependencies = AssetDatabase.GetDependencies(assetPaths[i], false);

                foreach (var assetDependency in assetDependencies)
                {
                    if (reverseDependencies.ContainsKey(assetDependency) && assetDependency != assetPaths[i])
                    {
                        reverseDependencies[assetDependency].Add(assetPaths[i]);
                    }
                }
            }
        }
    }
}