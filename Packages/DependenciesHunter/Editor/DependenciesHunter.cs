using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// ReSharper disable once CheckNamespace
namespace DependenciesHunter
{
    /// <summary>
    /// Lists all unreferenced assets in a project.
    /// </summary>
    public class AllProjectAssetsReferencesWindow : EditorWindow
    {
        private ProjectAssetsAnalysisUtilities _service;

        private List<UnusedAssetData> _unusedAssets;

        // ReSharper disable once InconsistentNaming
        private const string PATTERNS_PREFS_KEY = "DependencyHunterIgnorePatterns";

        private int? _pageToShow;
        private const int PageSize = 50;

        private int _sortType;
        
        private Vector2 _pagesScroll = Vector2.zero;
        private Vector2 _assetsScroll = Vector2.zero;
        
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

        private void PopulateUnusedAssetsList()
        {
            _pageToShow = null;
            
            if (_service == null)
            {
                _service = new ProjectAssetsAnalysisUtilities();
            }

            if (_unusedAssets == null)
            {
                _unusedAssets = new List<UnusedAssetData>();
            }
            
            Clear();
            Show();

            DependenciesMapUtilities.FillReverseDependenciesMap(out var map);

            EditorUtility.ClearProgressBar();

            var filteredOutput = new StringBuilder();
            filteredOutput.AppendLine("Assets ignored by pattern:");
            
            var count = 0;
            foreach (var mapElement in map)
            {
                EditorUtility.DisplayProgressBar("Unreferenced Assets", "Searching for unreferenced assets",
                    (float) count / map.Count);
                count++;

                if (mapElement.Value.Count == 0)
                {
                    var validForOutput = ProjectAssetsAnalysisUtilities.IsValidForOutput(mapElement.Key, _ignoreInOutputPatterns);
                    var validAssetType = _service.IsValidAssetType(mapElement.Key, validForOutput);

                    if (!validAssetType) 
                        continue;
                    
                    if (validForOutput)
                    {
                        _unusedAssets.Add(UnusedAssetData.Create(mapElement.Key));
                    }
                    else
                    {
                        filteredOutput.AppendLine(mapElement.Key);
                    }

                }
            }

            SortByPath();

            EditorUtility.ClearProgressBar();
            
            Debug.Log(filteredOutput.ToString());
            filteredOutput.Clear();
        }

        private void Clear()
        {
            _unusedAssets?.Clear();
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        private void OnGUI()
        {
            GUIUtilities.HorizontalLine();

            EditorGUILayout.BeginHorizontal();
            
            GUILayout.FlexibleSpace();
            
            var prevColor = GUI.color;
            GUI.color = Color.green;
            
            if (GUILayout.Button("Run Analysis", GUILayout.Width(300f)))
            {
                PopulateUnusedAssetsList();
            }
            
            GUI.color = prevColor;
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndHorizontal();
            
            GUIUtilities.HorizontalLine();
            
            OnPatternsGUI();
            
            GUIUtilities.HorizontalLine();

            if (_unusedAssets == null)
            {
                return;
            }
            
            if (_unusedAssets.Count == 0)
            {
                EditorGUILayout.LabelField("No unreferenced found");
                return;
            }

            EditorGUILayout.LabelField($"Unreferenced Assets: {_unusedAssets.Count}");
                
            _pagesScroll = EditorGUILayout.BeginScrollView(_pagesScroll);

            EditorGUILayout.BeginHorizontal();
            
            prevColor = GUI.color;
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
            
            GUIUtilities.HorizontalLine();
        
            EditorGUILayout.BeginHorizontal();
            
            prevColor = GUI.color;

            GUI.color = _sortType == 0 || _sortType == 1 ? Color.yellow : Color.white;
            if (GUILayout.Button("Sort by type", GUILayout.Width(100f)))
            {
                SortByType();
            }
        
            GUI.color = _sortType == 2 || _sortType == 3 ? Color.yellow : Color.white;
            if (GUILayout.Button("Sort by path", GUILayout.Width(100f)))
            {
                SortByPath();
            }
            
            GUI.color = _sortType == 4 || _sortType == 5 ? Color.yellow : Color.white;
            if (GUILayout.Button("Sort by size", GUILayout.Width(100f)))
            {
                SortBySize();
            }
            
            GUI.color = prevColor;

            EditorGUILayout.EndHorizontal();

            GUIUtilities.HorizontalLine();
            
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
                
                var unusedAsset = _unusedAssets[i];
                EditorGUILayout.BeginHorizontal();
                
                EditorGUILayout.LabelField(i.ToString(), GUILayout.Width(40f));
                
                prevColor = GUI.color;
                GUI.color = unusedAsset.ValidType ? Color.white : Color.red;
                EditorGUILayout.LabelField(unusedAsset.TypeName, GUILayout.Width(150f));    
                GUI.color = prevColor;

                if (unusedAsset.ValidType)
                {
                    var guiContent = EditorGUIUtility.ObjectContent(null, unusedAsset.Type);
                    guiContent.text = Path.GetFileName(unusedAsset.Path);

                    var alignment = GUI.skin.button.alignment;
                    GUI.skin.button.alignment = TextAnchor.MiddleLeft;

                    if (GUILayout.Button(guiContent, GUILayout.Width(300f), GUILayout.Height(18f)))
                    {
                        Selection.objects = new[] { AssetDatabase.LoadMainAssetAtPath(unusedAsset.Path) };
                    }

                    GUI.skin.button.alignment = alignment;
                }

                EditorGUILayout.LabelField(unusedAsset.ReadableSize, GUILayout.Width(70f));    
                
                EditorGUILayout.LabelField(unusedAsset.Path);

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void OnPatternsGUI()
        {
            EnsurePatternsLoaded();
            
            _ignorePatternsFoldout = EditorGUILayout.Foldout(_ignorePatternsFoldout,
                $"Patterns Ignored in Output: {_ignoreInOutputPatterns.Count}");

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
                    (current, t) => current + "\n" + t);

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
        
        private void SortByType()
        {
            if (_sortType == 0)
            {
                _sortType = 1;
                _unusedAssets?.Sort((a, b) =>
                    string.Compare(b.TypeName, a.TypeName, StringComparison.Ordinal));
            }
            else
            {
                _sortType = 0;
                _unusedAssets?.Sort((a, b) =>
                    string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal));
            }
        }
        
        private void SortByPath()
        {
            if (_sortType == 2)
            {
                _sortType = 3;
                _unusedAssets?.Sort((a, b) =>
                    string.Compare(b.Path, a.Path, StringComparison.Ordinal));
            }
            else
            {
                _sortType = 2;
                _unusedAssets?.Sort((a, b) =>
                    string.Compare(a.Path, b.Path, StringComparison.Ordinal));
            }
        }

        private void SortBySize()
        {
            if (_sortType == 4)
            {
                _sortType = 5;
                _unusedAssets?.Sort((b, a) => a.BytesSize.CompareTo(b.BytesSize));
            }
            else
            {
                _sortType = 4;
                _unusedAssets?.Sort((a, b) => a.BytesSize.CompareTo(b.BytesSize));
            }
        }

        private void OnDestroy()
        {
            Clear();
        }
    }

    /// <summary>
    /// Lists all references of the selected assets.
    /// </summary>
    public class SelectedAssetsReferencesWindow : EditorWindow
    {
        private SelectedAssetsAnalysisUtilities _service;

        private const float TabLength = 60f;
        private const TextAnchor ResultButtonAlignment = TextAnchor.MiddleLeft;

        private Dictionary<Object, List<string>> _lastResults;

        private Object[] _selectedObjects;

        private bool[] _selectedObjectsFoldouts;

        private float _workTime;

        private Vector2 _scrollPos = Vector2.zero;
        private Vector2[] _foldoutsScrolls;

        [MenuItem("Assets/Find References in Project", false, 20)]
        public static void FindReferences()
        {
            var window = GetWindow<SelectedAssetsReferencesWindow>();
            window.Start();
        }

        private void Start()
        {
            if (_service == null)
            {
                _service = new SelectedAssetsAnalysisUtilities();
            }

            Show();

            var startTime = Time.realtimeSinceStartup;

            _selectedObjects = Selection.objects;

            _lastResults = _service.GetReferences(_selectedObjects);

            EditorUtility.DisplayProgressBar("DependenciesHunter", "Preparing Assets", 1f);
            EditorUtility.UnloadUnusedAssetsImmediate();
            EditorUtility.ClearProgressBar();

            _workTime = Time.realtimeSinceStartup - startTime;
            _selectedObjectsFoldouts = new bool[_selectedObjects.Length];
            if (_selectedObjectsFoldouts.Length == 1)
            {
                _selectedObjectsFoldouts[0] = true;
            }

            _foldoutsScrolls = new Vector2[_selectedObjectsFoldouts.Length];
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

            GUILayout.Label($"Analysis done in: {_workTime} s");

            var results = _lastResults;

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            for (var i = 0; i < _selectedObjectsFoldouts.Length; i++)
            {
                GUIUtilities.HorizontalLine();
                
                GUILayout.BeginHorizontal();

                var dependencies = results[_selectedObjects[i]];

                _selectedObjectsFoldouts[i] = EditorGUILayout.Foldout(_selectedObjectsFoldouts[i], string.Empty);
                EditorGUILayout.ObjectField(_selectedObjects[i], typeof(Object), true);

                var content = dependencies.Count > 0 ? $"Dependencies: {dependencies.Count}" : "No dependencies found";
                EditorGUILayout.LabelField(content);

                GUILayout.EndHorizontal();

                if (_selectedObjectsFoldouts[i])
                {
                    _foldoutsScrolls[i] = GUILayout.BeginScrollView(_foldoutsScrolls[i]);

                    foreach (var resultPath in dependencies)
                    {
                        EditorGUILayout.BeginHorizontal();

                        GUILayout.Space(TabLength);

                        var type = AssetDatabase.GetMainAssetTypeAtPath(resultPath);
                        var guiContent = EditorGUIUtility.ObjectContent(null, type);
                        guiContent.text = Path.GetFileName(resultPath);

                        var alignment = GUI.skin.button.alignment;
                        GUI.skin.button.alignment = ResultButtonAlignment;

                        if (GUILayout.Button(guiContent, GUILayout.MinWidth(300f), GUILayout.Height(18f)))
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

    public class ProjectAssetsAnalysisUtilities
    {
        private List<string> _iconPaths;

        public bool IsValidAssetType(string path, bool validForOutput)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);

            if (type == null)
            {
                if (validForOutput)
                    Debug.LogWarning($"Invalid asset type found at {path}");
                return false;
            }
            
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

            return type != typeof(Texture2D) || !UsedAsProjectIcon(path);
        }
        
        public static bool IsValidForOutput(string path, List<string> ignoreInOutputPatterns)
        {
            return ignoreInOutputPatterns.All(pattern 
                => string.IsNullOrEmpty(pattern) || !Regex.Match(path, pattern).Success);
        }

        private bool UsedAsProjectIcon(string texturePath)
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

    public class SelectedAssetsAnalysisUtilities
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

    public static class DependenciesMapUtilities
    {
        public static void FillReverseDependenciesMap(out Dictionary<string, List<string>> reverseDependencies)
        {
            var assetPaths = AssetDatabase.GetAllAssetPaths().ToList();

            reverseDependencies = assetPaths.ToDictionary(assetPath => assetPath, assetPath => new List<string>());

            Debug.Log($"Total Assets Count: {assetPaths.Count}");

            for (var i = 0; i < assetPaths.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Dependencies Hunter", "Creating a map of dependencies",
                    (float) i / assetPaths.Count);

                var assetDependencies = AssetDatabase.GetDependencies(assetPaths[i], false);

                foreach (var assetDependency in assetDependencies)
                {
                    if (reverseDependencies.ContainsKey(assetDependency) && assetDependency != assetPaths[i])
                    {
                        reverseDependencies[assetDependency].Add(assetPaths[i]);
                    }
                }
            }
        }
    }

    public class UnusedAssetData
    {
        public static UnusedAssetData Create(string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            string typeName;
            
            if (type != null)
            {
                typeName = type.ToString();
                typeName = typeName.Replace("UnityEngine.", string.Empty);
                typeName = typeName.Replace("UnityEditor.", string.Empty);
            }
            else
            {
                typeName = "Unknown Type";
            }

            var fileInfo = new FileInfo(path);
            var bytesSize = fileInfo.Length;
            return new UnusedAssetData(path, type, typeName, bytesSize, CommonUtilities.GetReadableSize(bytesSize));
        }
        
        private UnusedAssetData(string path, Type type, string typeName, long bytesSize, string readableSize)
        {
            Path = path;
            Type = type;
            TypeName = typeName;
            BytesSize = bytesSize;
            ReadableSize = readableSize;
        }

        public string Path { get; }
        public Type Type { get; }
        public string TypeName { get; }
        public long BytesSize { get; }
        public string ReadableSize { get; }
        public bool ValidType => Type != null;
    }

    public static class GUIUtilities
    {
        private static void HorizontalLine(
            int marginTop,
            int marginBottom,
            int height,
            Color color
        )
        {
            EditorGUILayout.BeginHorizontal();
            var rect = EditorGUILayout.GetControlRect(
                false,
                height,
                new GUIStyle { margin = new RectOffset(0, 0, marginTop, marginBottom) }
            );

            EditorGUI.DrawRect(rect, color);
            EditorGUILayout.EndHorizontal();
        }

        public static void HorizontalLine(
            int marginTop = 5,
            int marginBottom = 5,
            int height = 2
        )
        {
            HorizontalLine(marginTop, marginBottom, height, new Color(0.5f, 0.5f, 0.5f, 1));
        }
    }

    public static class CommonUtilities
    {
        public static string GetReadableSize(long bytesSize)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytesSize;
            var order = 0;
            while (len >= 1024 && order < sizes.Length - 1) 
            {
                order++;
                len = len/1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}