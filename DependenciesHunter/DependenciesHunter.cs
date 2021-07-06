using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DependenciesHunter
{
	/// <summary>
	/// Lists all unreferenced assets in a project.
	/// </summary>
	public class AllProjectAssetsReferencesWindow : EditorWindow
	{
		private ProjectAssetsAnalysisHelper _service;
		
		private const TextAnchor ResultButtonAlignment = TextAnchor.MiddleLeft;

		private bool _ignoreProjectChange;

		private readonly List<string> _unusedAssets = new List<string>();
		
		private readonly Dictionary<string, bool> _toggles = new Dictionary<string, bool>();

		private readonly Vector2 _resultButtonSize = new Vector2(300f, 18f);
		private Vector2 _scroll = Vector2.zero;

		[MenuItem("Tools/References/Find unreferenced assets")]
		public static void LaunchUnreferencedAssetsWindow()
		{
			var window = GetWindow<AllProjectAssetsReferencesWindow>();
			window.ListAllUnusedAssetsInProject();
		}

		private void ListAllUnusedAssetsInProject()
		{
			if (_service == null)
			{
				_service = new ProjectAssetsAnalysisHelper();
			}
			
			Clear();
			
			Show();
			
			var assetPaths = AssetDatabase.GetAllAssetPaths();

			DependenciesMapUtilities.FillReverseDependenciesMap(assetPaths, out var map, false, 
				"Creating a map of dependencies");

			EditorUtility.ClearProgressBar();

			var count = 0;
			foreach (var mapElement in map)
			{
				EditorUtility.DisplayProgressBar("Unreferenced Assets", "Searching for unreferenced assets",
					(float) count / map.Count);
				count++;

				if (_service.IsTargetOfAnalysis(mapElement.Key) && mapElement.Value.Count == 0)
				{
					_unusedAssets.Add(mapElement.Key);
					_toggles.Add(mapElement.Key, false);
				}
			}

			EditorUtility.ClearProgressBar();
		}
		
		private void SetToggles(bool on)
		{
			var keys = new string[_toggles.Keys.Count];
			_toggles.Keys.CopyTo(keys, 0);

			foreach (var key in keys)
			{
				_toggles[key] = on;
			}
		}

		private void Remove()
		{
			_ignoreProjectChange = true;

			foreach (var asset in _unusedAssets.Where(asset => _toggles[asset]))
			{
				AssetDatabase.DeleteAsset(asset);
			}

			_ignoreProjectChange = false;

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			ListAllUnusedAssetsInProject();
		}

		private void Clear()
		{
			_unusedAssets.Clear();
			_toggles.Clear();

			EditorUtility.UnloadUnusedAssetsImmediate();
		}

		private void OnGUI()
		{
			if (_unusedAssets.Count == 0)
			{
				EditorGUILayout.LabelField("No unreferenced assets to show.");

				if (GUILayout.Button("Run Analysis"))
				{
					ListAllUnusedAssetsInProject();
				}
				
				return;
			}

			EditorGUILayout.LabelField($"Unreferenced Assets: {_unusedAssets.Count}");
			EditorGUILayout.LabelField("The Resources & Editor folders are skipped.");
			
			_scroll = GUILayout.BeginScrollView(_scroll);

			foreach (var unusedAssetPath in _unusedAssets)
			{
				EditorGUILayout.BeginHorizontal();

				_toggles[unusedAssetPath] = EditorGUILayout.Toggle(_toggles[unusedAssetPath]);

				var type = AssetDatabase.GetMainAssetTypeAtPath(unusedAssetPath);
				var guiContent = EditorGUIUtility.ObjectContent(null, type);
				guiContent.text = Path.GetFileName(unusedAssetPath);

				var alignment = GUI.skin.button.alignment;
				GUI.skin.button.alignment = ResultButtonAlignment;

				if (GUILayout.Button(guiContent, GUILayout.MinWidth(_resultButtonSize.x),
					GUILayout.Height(_resultButtonSize.y)))
				{
					Selection.objects = new[] {AssetDatabase.LoadMainAssetAtPath(unusedAssetPath)};
				}

				GUI.skin.button.alignment = alignment;

				EditorGUILayout.EndHorizontal();
			}

			GUILayout.EndScrollView();

			var color = GUI.color;
			
			var selectedCount = _toggles.Count(x => x.Value);
			
			GUI.color = selectedCount > 0 ? Color.red : Color.gray;
			
			GUILayout.Space(20);
			
			if (GUILayout.Button($"Remove {selectedCount} Selected"))
			{
				Remove();
			}

			GUI.color = color;
		}

		private void OnProjectChange()
		{
			if (!_ignoreProjectChange)
			{
				Clear();
			}
		}

		private void OnDestroy()
		{
			Clear();
		}
	}

	public class ProjectAssetsAnalysisHelper
	{
		private List<string> _iconPaths;
		
		public bool IsTargetOfAnalysis(string path)
		{
			var type = AssetDatabase.GetMainAssetTypeAtPath(path);

			if (type == typeof(MonoScript) || type == typeof(DefaultAsset))
			{
				return false;
			}

			if (type == typeof(SceneAsset))
			{
				var scenes = EditorBuildSettings.scenes;

				if (scenes.Any(scene => scene.path == path))
				{
					return false;
				}
			}

			if (type == typeof(Texture2D) && UsedAsIcon(path))
			{
				return false;
			}

			if (path.Contains("/Resources/") || path.Contains("/Editor/"))
			{
				return false;
			}

			return true;
		}
		
		private bool UsedAsIcon(string texturePath)
		{
			if (_iconPaths == null)
			{
				FindAllIcons();
			}

			return _iconPaths.Contains(texturePath);
		}

		private void FindAllIcons()
		{
			_iconPaths = new List<string>();

			var icons = new List<Texture2D>();
			var targetGroups = Enum.GetValues(typeof(BuildTargetGroup));

			foreach (var targetGroup in targetGroups)
			{
				icons.AddRange(PlayerSettings.GetIconsForTargetGroup((BuildTargetGroup) targetGroup));
			}

			foreach (var icon in icons)
			{
				_iconPaths.Add(AssetDatabase.GetAssetPath(icon));
			}
		}
	}
	
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
	
	/// <summary>
	/// Lists all references of the selected assets.
	/// </summary>
	public class SelectedAssetsReferencesWindow : EditorWindow
	{
		private SelectedAssetsAnalysisHelper _service;
		
		private const float TabLength = 60f;
		private const TextAnchor ResultButtonAlignment = TextAnchor.MiddleLeft;
		
		private Dictionary<Object, List<string>> _lastResults;

		private readonly Vector2 _resultButtonSize = new Vector2(300f, 18f);

		private Object[] _selectedObjects;

		private bool[] _foldouts;

		private float _workTime;

		private Vector2 _scrollPos = Vector2.zero;
		private Vector2[] _foldoutsScrolls;

		[MenuItem("Assets/Find .meta references", false, 20)]
		public static void FindReferences()
		{
			var window = GetWindow<SelectedAssetsReferencesWindow>();
			window.Start();
		}

		private void Start()
		{
			if (_service == null)
			{
				_service = new SelectedAssetsAnalysisHelper();
			}
			
			Show();

			var startTime = Time.realtimeSinceStartup;

			_selectedObjects = Selection.objects;
			
			_lastResults = _service.GetReferences(_selectedObjects);

			EditorUtility.DisplayProgressBar("DependenciesHunter", "Unloading Unused Assets", 1f);
			EditorUtility.UnloadUnusedAssetsImmediate();
			EditorUtility.ClearProgressBar();

			_workTime = Time.realtimeSinceStartup - startTime;
			_foldouts = new bool[_selectedObjects.Length];
			if (_foldouts.Length == 1)
			{
				_foldouts[0] = true;
			}

			_foldoutsScrolls = new Vector2[_foldouts.Length];
		}

		private void Clear()
		{
			_selectedObjects = null;
			_service = null;

			EditorUtility.UnloadUnusedAssetsImmediate();
		}

		private void OnGUI()
		{
			if (_lastResults == null)
			{
				return;
			}

			if (_selectedObjects == null || _selectedObjects.Any(selectedObject => selectedObject == null))
			{
				Clear();
				return;
			}

			GUILayout.BeginVertical();

			GUILayout.Label($"Found in: {_workTime} s");

			var results = _lastResults;

			_scrollPos = GUILayout.BeginScrollView(_scrollPos);

			for (var i = 0; i < _foldouts.Length; i++)
			{
				GUILayout.BeginHorizontal();

				_foldouts[i] = EditorGUILayout.Foldout(_foldouts[i], results[_selectedObjects[i]].Count.ToString());
				EditorGUILayout.ObjectField(_selectedObjects[i], typeof(Object), true);

				GUILayout.EndHorizontal();

				if (_foldouts[i])
				{
					_foldoutsScrolls[i] = GUILayout.BeginScrollView(_foldoutsScrolls[i]);

					foreach (var resultPath in results[_selectedObjects[i]])
					{
						EditorGUILayout.BeginHorizontal();

						GUILayout.Space(TabLength);

						var type = AssetDatabase.GetMainAssetTypeAtPath(resultPath);
						var guiContent = EditorGUIUtility.ObjectContent(null, type);
						guiContent.text = Path.GetFileName(resultPath);

						var alignment = GUI.skin.button.alignment;
						GUI.skin.button.alignment = ResultButtonAlignment;

						if (GUILayout.Button(guiContent, GUILayout.MinWidth(_resultButtonSize.x),
							GUILayout.Height(_resultButtonSize.y)))
						{
							Selection.objects = new[] {AssetDatabase.LoadMainAssetAtPath(resultPath)};
						}

						GUI.skin.button.alignment = alignment;

						EditorGUILayout.EndHorizontal();
					}

					GUILayout.EndScrollView();
				}
			}

			GUILayout.EndScrollView();
		}

		private void OnProjectChange()
		{
			Clear();
		}

		private void OnDestroy()
		{
			Clear();
		}
	}

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
			
			var assetPaths = AssetDatabase.GetAllAssetPaths();

			if (_cachedAssetsMap == null)
			{
				DependenciesMapUtilities.FillReverseDependenciesMap(assetPaths, out _cachedAssetsMap, false, 
					"Creating a map of dependencies");
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