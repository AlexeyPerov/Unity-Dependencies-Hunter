using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

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
				EditorGUILayout.LabelField("No unreferenced assets found.");
				return;
			}

			EditorGUILayout.LabelField($"Unreferenced Assets: {_unusedAssets.Count}");
			EditorGUILayout.LabelField("The Resources & Editor folders are skipped.");

			EditorGUILayout.BeginHorizontal();

			if (GUILayout.Button("Check All"))
			{
				SetToggles(true);
			}

			if (GUILayout.Button("Uncheck All"))
			{
				SetToggles(false);
			}

			EditorGUILayout.EndHorizontal();

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
			GUI.color = Color.red;
			
			GUILayout.Space(20);

			if (GUILayout.Button("Remove Selected"))
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
}