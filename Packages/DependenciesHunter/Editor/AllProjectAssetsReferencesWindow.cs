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

        private readonly List<string> _unusedAssets = new List<string>();

        // ReSharper disable once InconsistentNaming
        private const string PATTERNS_PREFS_KEY = "DependencyHunterIgnorePatterns";

        private int? _pageToShow;
        private const int PageSize = 50;
        
        private Vector2 _pagesScroll = Vector2.zero;
        private Vector2 _assetsScroll = Vector2.zero;

        private bool _launchedAtLeastOnce;
        
        // ReSharper disable once StringLiteralTypo
        private readonly List<string> _defaultIgnorePatterns = new List<string>
        {
            @"/Resources/",
            @"/Editor/",
            @"/Editor Default Resources/",
            @"/ThirdParty/",
            @"ProjectSettings/",
            @"Packages/",
            @"\.asmdef$",
            @"link\.xml$",
            @"\.csv$",
            @"\.md$",
            @"\.json$",
            @"\.xml$",
            @"\.txt$"
        };

        private List<string> _ignoreInOutputPatterns;
        private bool _ignorePatternsFoldout;

        [MenuItem("Tools/Dependencies Hunter")]
        public static void LaunchUnreferencedAssetsWindow()
        {
            GetWindow<AllProjectAssetsReferencesWindow>();
        }

        private void ListAllUnusedAssetsInProject()
        {
            _pageToShow = null;
            _launchedAtLeastOnce = true;
            
            if (_service == null)
            {
                _service = new ProjectAssetsAnalysisHelper();
            }

            Clear();

            Show();

            DependenciesMapUtilities.FillReverseDependenciesMap(out var map);

            EditorUtility.ClearProgressBar();

            var count = 0;
            foreach (var mapElement in map)
            {
                EditorUtility.DisplayProgressBar("Unreferenced Assets", "Searching for unreferenced assets",
                    (float) count / map.Count);
                count++;

                if (mapElement.Value.Count == 0)
                {
                    var validAssetType = _service.IsValidAssetType(mapElement.Key);
                    var validForOutput = false;

                    if (validAssetType)
                    {
                        validForOutput = _service.IsValidForOutput(mapElement.Key, _ignoreInOutputPatterns);

                        if (!validForOutput)
                        {
                            Debug.Log($"Unreferenced Asset: {mapElement.Key} is ignored in output " +
                                      $"due to specified ignore patterns");
                            
                        }
                    }

                    if (validForOutput)
                    {
                        _unusedAssets.Add(mapElement.Key);
                    }
                }
            }

            EditorUtility.ClearProgressBar();
        }

        private void Clear()
        {
            _unusedAssets.Clear();
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        private void OnGUI()
        {
            EditorGUILayout.Separator();

            OnPatternsGUI();
            
            EditorGUILayout.Separator();
            
            if (GUILayout.Button("Run Analysis", GUILayout.Width(300f)))
            {
                ListAllUnusedAssetsInProject();
            }
            
            EditorGUILayout.Separator();
            
            if (_launchedAtLeastOnce)
            {
                EditorGUILayout.LabelField($"Unreferenced Assets: {_unusedAssets.Count}");

                if (_unusedAssets.Count > 0)
                {
                    _pagesScroll = EditorGUILayout.BeginScrollView(_pagesScroll);

                    EditorGUILayout.BeginHorizontal();

                    var prevColor = GUI.color;
                    GUI.color = !_pageToShow.HasValue ? Color.yellow : Color.white;

                    if (GUILayout.Button("All", GUILayout.Width(30f)))
                    {
                        _pageToShow = null;
                    }

                    GUI.color = prevColor;

                    var totalCount = _unusedAssets.Count;
                    var pagesCount = totalCount / PageSize + (totalCount % PageSize > 0 ? 1 : 0);

                    for (var i = 0; i < pagesCount; i++)
                    {
                        prevColor = GUI.color;
                        GUI.color = _pageToShow == i ? Color.yellow : Color.white;

                        if (GUILayout.Button((i + 1).ToString(), GUILayout.Width(30f)))
                        {
                            _pageToShow = i;
                        }

                        GUI.color = prevColor;
                    }

                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndScrollView();
                }
            }

            EditorGUILayout.Separator();
            
            _assetsScroll = GUILayout.BeginScrollView(_assetsScroll);

            EditorGUILayout.BeginVertical();

            for (var i = 0; i < _unusedAssets.Count; i++)
            {
                if (_pageToShow.HasValue)
                {
                    var page = _pageToShow.Value;
                    if (i < page * PageSize || i >= (page + 1) * PageSize)
                    {
                        continue;
                    }
                }
                
                var unusedAssetPath = _unusedAssets[i];
                EditorGUILayout.BeginHorizontal();

                var type = AssetDatabase.GetMainAssetTypeAtPath(unusedAssetPath);
                var typeName = type.ToString();
                typeName = typeName.Replace("UnityEngine.", string.Empty);
                typeName = typeName.Replace("UnityEditor.", string.Empty);

                EditorGUILayout.LabelField(i.ToString(), GUILayout.Width(40f));
                EditorGUILayout.LabelField(typeName, GUILayout.Width(150f));

                var guiContent = EditorGUIUtility.ObjectContent(null, type);
                guiContent.text = Path.GetFileName(unusedAssetPath);

                var alignment = GUI.skin.button.alignment;
                GUI.skin.button.alignment = TextAnchor.MiddleLeft;

                if (GUILayout.Button(guiContent,
                    GUILayout.Width(300f),
                    GUILayout.Height(18f)))
                {
                    Selection.objects = new[] {AssetDatabase.LoadMainAssetAtPath(unusedAssetPath)};
                }

                GUI.skin.button.alignment = alignment;

                EditorGUILayout.LabelField(unusedAssetPath);

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void OnPatternsGUI()
        {
            EnsurePatternsLoaded();
            
            _ignorePatternsFoldout = EditorGUILayout.Foldout(_ignorePatternsFoldout, "Ignored in Output Assets Patterns");

            if (!_ignorePatternsFoldout) return;

            var isDirty = false;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Format: RegExp patterns");
            if (GUILayout.Button("Set Default", GUILayout.Width(300f)))
            {
                _ignoreInOutputPatterns = _defaultIgnorePatterns.ToList();
                isDirty = true;
            }

            if (GUILayout.Button("Save to Clipboard"))
            {
                var contents = _ignoreInOutputPatterns.Aggregate("Patterns:", 
                    (current, t) => current + ("\n" + t));

                EditorGUIUtility.systemCopyBuffer = contents;
            }
            
            EditorGUILayout.EndHorizontal();

            var newCount = Mathf.Max(0, EditorGUILayout.IntField("Count:", _ignoreInOutputPatterns.Count));

            if (newCount != _ignoreInOutputPatterns.Count)
            {
                isDirty = true;
            }

            while (newCount < _ignoreInOutputPatterns.Count)
            {
                _ignoreInOutputPatterns.RemoveAt(_ignoreInOutputPatterns.Count - 1);
            }

            if (newCount > _ignoreInOutputPatterns.Count)
            {
                for (var i = _ignoreInOutputPatterns.Count; i < newCount; i++)
                {
                    _ignoreInOutputPatterns.Add(EditorPrefs.GetString($"{PATTERNS_PREFS_KEY}_{i}"));
                }
            }

            for (var i = 0; i < _ignoreInOutputPatterns.Count; i++)
            {
                var newValue = EditorGUILayout.TextField(_ignoreInOutputPatterns[i]);
                if (_ignoreInOutputPatterns[i] != newValue)
                {
                    isDirty = true;
                    _ignoreInOutputPatterns[i] = newValue;
                }
            }

            if (isDirty)
            {
                SavePatterns();
            }
        }

        private void EnsurePatternsLoaded()
        {
            if (_ignoreInOutputPatterns != null)
            {
                return;
            }
            
            var count = EditorPrefs.GetInt(PATTERNS_PREFS_KEY, -1);

            if (count == -1)
            {
                _ignoreInOutputPatterns = _defaultIgnorePatterns.ToList();
            }
            else
            {
                _ignoreInOutputPatterns = new List<string>();
                
                for (var i = 0; i < count; i++)
                {
                    _ignoreInOutputPatterns.Add(EditorPrefs.GetString($"{PATTERNS_PREFS_KEY}_{i}"));
                }    
            }
        }

        private void SavePatterns()
        {
            EditorPrefs.SetInt(PATTERNS_PREFS_KEY, _ignoreInOutputPatterns.Count);

            for (var i = 0; i < _ignoreInOutputPatterns.Count; i++)
            {
                EditorPrefs.SetString($"{PATTERNS_PREFS_KEY}_{i}", _ignoreInOutputPatterns[i]);
            }
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}