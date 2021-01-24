using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DependenciesHunter
{
	public class DependenciesHunterWindow : EditorWindow
	{
		private const float TabLength = 60f;
		private const TextAnchor ResultButtonAlignment = TextAnchor.MiddleLeft;
		
		private readonly Vector2 _resultButtonSize = new Vector2(300f, 18f);

		private bool _directDependencies;
		private bool _allDependencies;
		private Object[] _selectedObjects;
		private float _workTime;

		private DependenciesHunter _dependenciesHunter;

		private Vector2 _scrollPos = Vector2.zero;
		private bool[] _foldouts;
		private Vector2[] _foldoutsScrolls;
		private bool _showDirectDependencies = true;

		[MenuItem("Assets/Dependencies Hunter/Which objects use it?", false, 20)]
		public static void HuntForDirectDependencies()
		{
			var window = GetWindow<DependenciesHunterWindow>();
			window.Go(true, false);
		}

		private void Go(bool directDependencies, bool allDependencies)
		{
			_directDependencies = directDependencies;
			_allDependencies = allDependencies;
			_showDirectDependencies = !allDependencies;
			Show();

			var startTime = Time.realtimeSinceStartup;

			_selectedObjects = Selection.objects;

			if (_dependenciesHunter == null)
			{
				_dependenciesHunter = new DependenciesHunter();
			}
			
			_dependenciesHunter.GetDependenciesRevert(_selectedObjects, _directDependencies, _allDependencies);

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
			if (_dependenciesHunter == null || _selectedObjects == null
				|| _directDependencies && _dependenciesHunter.DirectResults == null
				|| _allDependencies && _dependenciesHunter.AllResults == null)
			{
				return;
			}

			if (_selectedObjects.Any(selectedObject => selectedObject == null))
			{
				Clear();
				return;
			}

			GUILayout.BeginVertical();

			GUILayout.Label($"Found in: {_workTime} s");

			if (_directDependencies && _allDependencies)
			{
				_showDirectDependencies = EditorGUILayout.Toggle("Only direct dependencies", _showDirectDependencies);
			}

			var results = _showDirectDependencies ? _dependenciesHunter.DirectResults : _dependenciesHunter.AllResults;

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

						if (GUILayout.Button(guiContent, GUILayout.MinWidth(_resultButtonSize.x), GUILayout.Height(_resultButtonSize.y)))
						{
							Selection.objects = new[] { AssetDatabase.LoadMainAssetAtPath(resultPath) };
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
