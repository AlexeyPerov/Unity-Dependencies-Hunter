using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DH
{
	public class ReferencesWindow : EditorWindow
	{
		private const float TabLength = 60f;
		private const TextAnchor ResultButtonAlignment = TextAnchor.MiddleLeft;

		private ReferencesSearchService _dependenciesHunter;
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
			var window = GetWindow<ReferencesWindow>();
			window.Start();
		}

		private void Start()
		{
			Show();

			var startTime = Time.realtimeSinceStartup;

			_selectedObjects = Selection.objects;

			if (_dependenciesHunter == null)
			{
				_dependenciesHunter = new ReferencesSearchService();
			}

			_lastResults = _dependenciesHunter.GetReferences(_selectedObjects);

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
			_dependenciesHunter = null;

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
}
