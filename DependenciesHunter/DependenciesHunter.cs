using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DependenciesHunter
{
	public class DependenciesHunter
	{
		private Dictionary<string, List<string>> _directReverseDependencies;
		private Dictionary<string, List<string>> _allReverseDependencies;

		private Dictionary<Object, List<string>> _directResults;
		private Dictionary<Object, List<string>> _allResults;

		public Dictionary<string, List<string>> DirectMap => _directReverseDependencies;
		public Dictionary<Object, List<string>> DirectResults => _directResults;
		public Dictionary<Object, List<string>> AllResults => _allResults;

		public void GetDependenciesRevert(Object[] selectedObjects, bool directDependencies, bool allDependencies)
		{
			CreateMapIfNeeded(directDependencies, allDependencies);

			if (selectedObjects != null)
			{
				if (directDependencies)
				{
					FindDependencies(selectedObjects, _directReverseDependencies, out _directResults);
				}

				if (allDependencies)
				{
					FindDependencies(selectedObjects, _allReverseDependencies, out _allResults);
				}
			}
		}

		public void CreateMapIfNeeded(bool directDependencies, bool allDependencies)
		{
			var assetPaths = AssetDatabase.GetAllAssetPaths();

			if (directDependencies && _directReverseDependencies == null)
			{
				CreateMap(assetPaths, out _directReverseDependencies, false, "Creating a map of direct dependencies");
			}

			if (allDependencies && _allReverseDependencies == null)
			{
				CreateMap(assetPaths, out _allReverseDependencies, true, "Creating a map of all dependencies");
			}

			EditorUtility.ClearProgressBar();
		}

		private void CreateMap(string[] assetPaths, out Dictionary<string, List<string>> reverseDependencies, bool recursive, string progressBarInfo)
		{
			reverseDependencies = new Dictionary<string, List<string>>();

			foreach (var assetPath in assetPaths)
			{
				reverseDependencies.Add(assetPath, new List<string>());
			}

			for (var i = 0; i < assetPaths.Length; i++)
			{
				EditorUtility.DisplayProgressBar("Dependencies Hunter", progressBarInfo, (float)i / assetPaths.Length);

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

		private void FindDependencies(Object[] selectedObjects, Dictionary<string, List<string>> source, out Dictionary<Object, List<string>> results)
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
					Debug.LogWarning("Dependencies Hunter doesn't contain the object in its map", selectedObject);
					results.Add(selectedObject, new List<string>());
				}
			}
		}
	} 
}
