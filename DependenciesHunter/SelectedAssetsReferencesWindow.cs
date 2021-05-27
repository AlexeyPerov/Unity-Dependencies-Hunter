using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DependenciesHunter
{
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
