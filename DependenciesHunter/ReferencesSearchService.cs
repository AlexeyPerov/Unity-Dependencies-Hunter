using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DH
{
	public class ReferencesSearchService
	{
		private Dictionary<string, List<string>> _cachedAssetsMap;

		public Dictionary<Object, List<string>> GetReferences(Object[] selectedObjects)
		{
			var assetPaths = AssetDatabase.GetAllAssetPaths();

			if (_cachedAssetsMap == null)
			{
				ReferencesSearchUtilities.FillReverseDependenciesMap(assetPaths, out _cachedAssetsMap, false, 
					"Creating a map of dependencies");
			}

			EditorUtility.ClearProgressBar();

			if (selectedObjects == null)
			{
				return new Dictionary<Object, List<string>>();
			}

			ReferencesSearchUtilities.GetDependencies(selectedObjects, _cachedAssetsMap, out var result);

			return result;
		}
	} 
}
