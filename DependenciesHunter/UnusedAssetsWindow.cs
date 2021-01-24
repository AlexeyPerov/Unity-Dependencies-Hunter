using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DependenciesHunter
{
	public class UnusedAssetsWindow : EditorWindow
	{
		private const TextAnchor ResultButtonAlignment = TextAnchor.MiddleLeft;
		private readonly Vector2 _resultButtonSize = new Vector2(300f, 18f);

		private DependenciesHunter _dependenciesHunter;
		private List<string> _iconPaths;
		private bool _ignoreProjectChange;

		private readonly List<string> _unusedAssets = new List<string>();

		private Vector2 _scroll = Vector2.zero;
		private readonly Dictionary<string, bool> _toggles = new Dictionary<string, bool>();

		[MenuItem("Platform/Tools/References/Dependencies Hunter (Find unused assets)")]
		public static void HuntForDirectDependencies()
		{
			var window = GetWindow<UnusedAssetsWindow>();
			window.SearchForUnusedAssets();
		}

		private void SearchForUnusedAssets()
		{
			Clear();
			
			_dependenciesHunter = new DependenciesHunter();
			Show();

			_dependenciesHunter.CreateMapIfNeeded(true, false);
			var map = _dependenciesHunter.DirectMap;

			var count = 0;
			foreach (var mapElement in map)
			{
				EditorUtility.DisplayProgressBar("Unused Assets", "Searching for unused assets",
					(float) count / map.Count);
				count++;

				if (ShouldBeChecked(mapElement.Key) && mapElement.Value.Count == 0)
				{
					_unusedAssets.Add(mapElement.Key);
					_toggles.Add(mapElement.Key, true);
				}
			}

			EditorUtility.ClearProgressBar();
		}

		private bool ShouldBeChecked(string path)
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

			if (path.Contains("/Resources/"))
			{
				return false;
			}

			if (path.Contains("/Editor/"))
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

			for (var i = 0; i < _unusedAssets.Count; i++)
			{
				if (_toggles[_unusedAssets[i]])
				{
					AssetDatabase.DeleteAsset(_unusedAssets[i]);
				}
			}

			_ignoreProjectChange = false;

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			SearchForUnusedAssets();
		}

		private void Clear()
		{
			_unusedAssets.Clear();
			_toggles.Clear();
			_iconPaths = null;
			_dependenciesHunter = null;

			EditorUtility.UnloadUnusedAssetsImmediate();
		}

		private void OnGUI()
		{
			if (_unusedAssets.Count == 0)
			{
				EditorGUILayout.LabelField("No unused assets found.");
				return;
			}

			EditorGUILayout.LabelField($"Unused Assets: {_unusedAssets.Count}");

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
}