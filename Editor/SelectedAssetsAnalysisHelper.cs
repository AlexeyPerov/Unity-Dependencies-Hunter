using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DependenciesHunter
{
    public class SelectedAssetsAnalysisHelper
    {
        private Dictionary<string, List<string>> _cachedAssetsMap;

        public Dictionary<Object, List<string>> GetReferences(Object[] selectedObjects)
        {
            if (selectedObjects == null)
            {
                Debug.Log("No selected objects passed");
                return new Dictionary<Object, List<string>>();
            }

            if (_cachedAssetsMap == null)
            {
                DependenciesMapUtilities.FillReverseDependenciesMap(out _cachedAssetsMap);
            }

            EditorUtility.ClearProgressBar();

            GetDependencies(selectedObjects, _cachedAssetsMap, out var result);

            return result;
        }

        private static void GetDependencies(IEnumerable<Object> selectedObjects, IReadOnlyDictionary<string,
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
    }
}