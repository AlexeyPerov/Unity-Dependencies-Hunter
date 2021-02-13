using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DH
{
    public static class ReferencesSearchUtilities
    {
        public static void GetDependencies(IEnumerable<Object> selectedObjects, IReadOnlyDictionary<string, 
            List<string>> source, out Dictionary<Object, List<string>> results)
        {
            results = new Dictionary<Object, List<string>>();

            foreach (var selectedObject in selectedObjects)
            {
                var selectedObjectPath = AssetDatabase.GetAssetPath(selectedObject);

                if (source.ContainsKey(selectedObjectPath))
                {
                    results.Add(selectedObject, source[selectedObjectPath]);
                }
                else
                {
                    Debug.LogWarning("Dependencies Hunter doesn't contain the specified object in the assets map", 
                        selectedObject);
                    results.Add(selectedObject, new List<string>());
                }
            }
        }

        public static void FillReverseDependenciesMap(IReadOnlyList<string> assetPaths, 
            out Dictionary<string, List<string>> reverseDependencies, bool recursive, string progressBarInfo)
        {
            reverseDependencies = assetPaths.ToDictionary(assetPath => assetPath, assetPath => new List<string>());

            for (var i = 0; i < assetPaths.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Dependencies Hunter", progressBarInfo, (float)i / assetPaths.Count);

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