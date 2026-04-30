using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif
using UnityEngine;
using UnityEngine.U2D;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// ReSharper disable once CheckNamespace
namespace DependenciesHunter
{
    /// <summary>
    /// Lists all unreferenced assets in a project.
    /// </summary>
    public class AllProjectAssetsReferencesWindow : EditorWindow
    {
        private class Result
        {
            public Result(bool findUnreferencedOnly)
            {
                FindUnreferencedOnly = findUnreferencedOnly;
            }

            public List<AssetData> Assets { get; } = new List<AssetData>();
            public Dictionary<string, int> RefsByTypes { get; } = new Dictionary<string, int>();
            public string OutputDescription { get; set; }
            public bool FindUnreferencedOnly { get; }
        }

        private List<string> DeleteUnreferencedAssets(List<AssetData> assets)
        {
            var deletedPaths = new List<string>();
            foreach (var resultAsset in assets)
            {
                var hasDeletedAsset = AssetDatabase.DeleteAsset(resultAsset.Path);
                if (hasDeletedAsset)
                {
                    deletedPaths.Add(resultAsset.Path);
                }
            }

            if (deletedPaths.Count > 0)
            {
                AssetDatabase.Refresh();
            }
            
            return deletedPaths;
        }

        public class AnalysisSettings
        {
            public static class PrefsKeys
            {
                private const string Prefix = "DependenciesHunter.Analysis.";
                public const string FindUnreferencedOnly = Prefix + "FindUnreferencedOnly";
                public const string ScanForAssetReferences = Prefix + "ScanForAssetReferences";
                public const string TryUseReflectionForAddressablesDetection = Prefix + "TryUseReflectionForAddressablesDetection";
                public const string ScanForTerrainDataReferences = Prefix + "ScanForTerrainDataReferences";
            }

            public bool FindUnreferencedOnly { get; set; } = true;

            /// <summary>
            /// Set to true to scan also for Addressables AssetReference properties
            /// NOTE: this might make scanning longer.
            /// </summary>
            public bool ScanForAssetReferences { get; set; }
            public bool TryUseReflectionForAddressablesDetection { get; set; }
            public bool ScanForTerrainDataReferences { get; set; }

            public void LoadFromEditorPrefs()
            {
                FindUnreferencedOnly = EditorPrefs.GetBool(PrefsKeys.FindUnreferencedOnly, true);
                ScanForAssetReferences = EditorPrefs.GetBool(PrefsKeys.ScanForAssetReferences, false);
                TryUseReflectionForAddressablesDetection =
                    EditorPrefs.GetBool(PrefsKeys.TryUseReflectionForAddressablesDetection, false);
                ScanForTerrainDataReferences = EditorPrefs.GetBool(PrefsKeys.ScanForTerrainDataReferences, false);
            }

            public void SaveToEditorPrefs()
            {
                EditorPrefs.SetBool(PrefsKeys.FindUnreferencedOnly, FindUnreferencedOnly);
                EditorPrefs.SetBool(PrefsKeys.ScanForAssetReferences, ScanForAssetReferences);
                EditorPrefs.SetBool(PrefsKeys.TryUseReflectionForAddressablesDetection,
                    TryUseReflectionForAddressablesDetection);
                EditorPrefs.SetBool(PrefsKeys.ScanForTerrainDataReferences, ScanForTerrainDataReferences);
            }

            private readonly List<string> _defaultIgnorePatterns = new List<string>
            {
                "/Resources/",
                "/Editor/",
                "/Editor Default Resources/",
                "/ThirdParty/",
                "ProjectSettings/",
                "Packages/",
                @"\.asmdef$",
                @"link\.xml$",
                @"\.csv$",
                @"\.md$",
                @"\.json$",
                @"\.xml$",
                @"\.txt$",
                // ReSharper disable StringLiteralTypo
                @"\.cginc",
                @"\.spriteatlas"
                // ReSharper enable StringLiteralTypo
            };
            
            public List<string> IgnoredPatterns
            {
                get
                {
                    if (IgnoredPatternsAsset == null)
                        return _defaultIgnorePatterns;
                    return IgnoredPatternsAsset.IgnoredPatterns;
                }
            }

            public bool IsIgnoredPatternsAssetUsed => IgnoredPatternsAsset != null;
            public bool TriedLoadingIgnoredPatterns { get; private set; }

            public IgnoredPatternsAsset IgnoredPatternsAsset { get; private set; }

            private const string IgnoredPatternsFileName = "DependenciesHunterIgnorePatterns.asset";
            private static readonly string IgnoredPatternsFilePath = Path.Combine("Assets/Editor", IgnoredPatternsFileName);

            public void CreateIgnoredPatternsAsset()
            {
                try
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Editor"))
                    {
                        AssetDatabase.CreateFolder("Assets", "Editor");
                    }
                    
                    var asset = CreateInstance<IgnoredPatternsAsset>();
                    asset.IgnoredPatterns = new List<string>(_defaultIgnorePatterns);

                    AssetDatabase.CreateAsset(asset, IgnoredPatternsFilePath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    IgnoredPatternsAsset = asset;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to save SearchIgnorePatterns: {e}");
                }
            }

            public void TryLoadIgnoredPatternsAsset()
            {
                try
                {
                    TriedLoadingIgnoredPatterns = true;
                    
                    if (!File.Exists(IgnoredPatternsFilePath))
                    {
                        return;
                    }

                    IgnoredPatternsAsset = AssetDatabase.LoadAssetAtPath<IgnoredPatternsAsset>(IgnoredPatternsFilePath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to load IgnoredPatterns file: {e}");
                }
            }

            public void DeleteIgnoredPatternsAsset()
            {
                try
                {
                    if (File.Exists(IgnoredPatternsFilePath))
                    {
                        File.Delete(IgnoredPatternsFilePath);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to delete IgnoredPatterns file: {e}");
                }
                finally
                {
                    AssetDatabase.Refresh();
                }
            }
        }

        public class IgnoredPatternsAsset : ScriptableObject
        {
            // ReSharper disable once InconsistentNaming
            public List<string> IgnoredPatterns = new List<string>();
        }
        
        private class OutputSettings
        {
            public const int PageSize = 50;

            public int? PageToShow { get; set; }
            
            public string PathFilter { get; set; }
            public string TypeFilter { get; set; }
            // ReSharper disable once IdentifierTypo
            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            public bool ShowAddressables { get; set; }
            public bool ShowUnreferencedOnly { get; set; }
            public bool ShowPotentialFalsePositivesOnly { get; set; }
        
            /// <summary>
            /// Sorting types.
            /// By type: 0: A-Z, 1: Z-A
            /// By path: 2: A-Z, 3: Z-A
            /// By size: 4: A-Z, 5: Z-A
            ///  
            /// </summary>
            public int SortType { get; set; }
        }

        private ProjectAssetsAnalysisUtilities _service;
        
        private Result _result;
        private OutputSettings _outputSettings;
        private AnalysisSettings _analysisSettings;
        
        private Vector2 _pagesScroll = Vector2.zero;
        private Vector2 _typesScroll = Vector2.zero;
        private Vector2 _assetsScroll = Vector2.zero;
        private bool _analysisSettingsFoldout;
        private bool _searchPatternsSettingsFoldout;
        private List<AssetData> _cachedFilteredAssets;
        private string _cachedPathFilter;
        private string _cachedTypeFilter;
        private bool _cachedShowAddr;
        private bool _cachedShowUnref;
        private bool _cachedShowWarn;
        private int _cachedSortType;
        private bool _cachedAddrDet;
        private bool _allSelected;
        private string _backupDirectory;

        [MenuItem("Tools/Dependencies Hunter")]
        public static void LaunchUnreferencedAssetsWindow()
        {
            GetWindow<AllProjectAssetsReferencesWindow>("Dependencies Hunter");
        }

        private void PopulateUnreferencedAssetsList()
        {
            _result = new Result(_analysisSettings.FindUnreferencedOnly);
            _outputSettings = new OutputSettings();
            _cachedFilteredAssets = null;
            CommonUtilities.ClearAddressablesCache();

            _service = new ProjectAssetsAnalysisUtilities();
            
            Clear();

            if (!_result.FindUnreferencedOnly)
            {
                _outputSettings.ShowUnreferencedOnly = false;
            }
            
            _outputSettings.PageToShow = 0;

            var stopWatch = new Stopwatch();
            stopWatch.Start();
            
            Show();

            DependenciesMapUtilities.FillReverseDependenciesMap(
                _analysisSettings.ScanForAssetReferences, 
                EditorSettings.serializationMode != SerializationMode.ForceText, 
                _analysisSettings.ScanForTerrainDataReferences,
                out var map);

            EditorUtility.ClearProgressBar();

            var filteredOutput = new StringBuilder();
            filteredOutput.AppendLine("Assets ignored by pattern:");

            var compiledPatterns = ProjectAssetsAnalysisUtilities.CompilePatterns(_analysisSettings.IgnoredPatterns);
            
            var count = 0;
            var mapCount = map.Count;
            var progressInterval = Math.Max(1, mapCount / 100);

            foreach (var mapElement in map)
            {
                if (count % progressInterval == 0)
                {
                    EditorUtility.DisplayProgressBar("Unreferenced Assets", "Searching for unreferenced assets",
                        (float)count / mapCount);
                }
                count++;

                var assetPath = mapElement.Key;
                var falsePositiveWarning = string.Empty;
                var referencesCount = mapElement.Value.Count;

                var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);

                if (referencesCount == 1 && type == typeof(Texture2D))
                {
                    var reference = mapElement.Value[0];
                    var referenceType = AssetDatabase.GetMainAssetTypeAtPath(reference);
                    if (referenceType == typeof(SpriteAtlas))
                    {
                        falsePositiveWarning = $"Sprite references only its atlas {reference}";
                        referencesCount = 0;
                    }
                }

                if (_result.FindUnreferencedOnly && referencesCount != 0) 
                    continue;
                
                var validForOutput = ProjectAssetsAnalysisUtilities.IsValidForOutput(assetPath, compiledPatterns);
                var validAssetType = _service.IsValidAssetType(assetPath, type, validForOutput);

                if (!validAssetType) 
                    continue;
                    
                if (validForOutput)
                {
                    _result.Assets.Add(AssetData.Create(
                        assetPath,
                        type,
                        referencesCount,
                        mapElement.Value,
                        falsePositiveWarning,
                        _analysisSettings.TryUseReflectionForAddressablesDetection));
                }
                else
                {
                    filteredOutput.AppendLine(assetPath);
                }
            }

            foreach (var group in _result.Assets.GroupBy(x => x.TypeName))
            {
                _result.RefsByTypes[group.Key] = group.Count();
            }
            
            if (_analysisSettings.TryUseReflectionForAddressablesDetection)
            {
                if (_analysisSettings.FindUnreferencedOnly)
                {
                    var addressablesCount = _result.Assets.Count(x => x.IsAddressable);

                    var nonAddressablesCount = _result.Assets.Count - addressablesCount;
                    _result.OutputDescription = $"Analysis Done. Unreferenced Assets: Total = {_result.Assets.Count} " +
                                                $"Addressables = {addressablesCount} Common = {nonAddressablesCount}";
                }
                else
                {
                    var unreferencedTotalCount = _result.Assets.Count(x => x.ReferencesCount == 0);
                    
                    var unreferencedAddressablesCount = _result.Assets.Count(x => 
                        x.IsAddressable && x.ReferencesCount == 0);

                    var unreferencedCommonCount = unreferencedTotalCount - unreferencedAddressablesCount;
                    
                    _result.OutputDescription = $"Analysis Done. Assets: Total = {_result.Assets.Count} " +
                                                $"Unreferenced = {unreferencedTotalCount} " +
                                                $"Unreferenced Addressables = {unreferencedAddressablesCount} " +
                                                $"Unreferenced Common = {unreferencedCommonCount}";
                }
            }
            else if (_result.FindUnreferencedOnly)
            {
                _result.OutputDescription = $"Analysis Done. Unreferenced Assets: {_result.Assets.Count}";
            }
            else
            {
                var unreferencedTotalCount = _result.Assets.Count(x => x.ReferencesCount == 0);
                _result.OutputDescription = $"Analysis Done. Assets: Total = {_result.Assets.Count} Unreferenced = {unreferencedTotalCount}";
            }

            SortByPath();

            EditorUtility.ClearProgressBar();
            
            stopWatch.Stop();
            Debug.Log($"Scanning took: {stopWatch.Elapsed.TotalSeconds} sec");
            
            Debug.Log(filteredOutput.ToString());
            Debug.Log(_result.OutputDescription);
            filteredOutput.Clear();
        }

        private static void Clear()
        {
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        private void OnGUI()
        {
            GUIUtilities.HorizontalLine();

            EditorGUILayout.BeginHorizontal();
            
            GUILayout.FlexibleSpace();
            
            var prevColor = GUI.color;
            GUI.color = Color.green;
            
            if (GUILayout.Button(
                    new GUIContent("Run Analysis",
                        "Rebuilds a reverse dependency map for the whole project, then fills the list according to the current mode. Duration grows with project size."),
                    GUILayout.Width(300f)))
            {
                PopulateUnreferencedAssetsList();
            }
            
            GUI.color = prevColor;
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndHorizontal();
            
            GUIUtilities.HorizontalLine();
            
            OnAnalysisSettingsGUI();
            
            GUIUtilities.HorizontalLine();

            if (_result == null)
            {
                return;
            }
            
            if (_result.Assets.Count == 0)
            {
                EditorGUILayout.LabelField("No assets found");
                return;
            }
            
            if (_cachedFilteredAssets == null
                || _cachedPathFilter != _outputSettings.PathFilter
                || _cachedTypeFilter != _outputSettings.TypeFilter
                || _cachedShowAddr != _outputSettings.ShowAddressables
                || _cachedShowUnref != _outputSettings.ShowUnreferencedOnly
                || _cachedShowWarn != _outputSettings.ShowPotentialFalsePositivesOnly
                || _cachedSortType != _outputSettings.SortType
                || _cachedAddrDet != _analysisSettings.TryUseReflectionForAddressablesDetection)
            {
                RebuildFilteredCache();
            }

            var filteredAssets = _cachedFilteredAssets;
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_result.OutputDescription);
            
            if (filteredAssets?.Count < 1000)
            {
                if (GUILayout.Button(new GUIContent("Export to Clipboard",
                        "Copies the currently filtered list as plain text (type, size, path per line) to the system clipboard. Shown only when the filtered count is below 1000."),
                    GUILayout.Width(170f)))
                {
                    var toClipboard = new StringBuilder();

                    toClipboard.AppendLine($"Assets [{filteredAssets.Count}]:");

                    foreach (var asset in filteredAssets)
                    {
                        toClipboard.AppendLine($"[{asset.TypeName}][{asset.ReadableSize}][Refs:{asset.ReferencesCount}] {asset.Path}");
                    }

                    EditorGUIUtility.systemCopyBuffer = toClipboard.ToString();
                }
            }

            if (filteredAssets != null && filteredAssets.Count > 0
                && GUILayout.Button(new GUIContent("Export to CSV",
                        "Writes the currently filtered rows to a CSV file you choose. Columns: Type, Size, Path, References, Addressable, Warning. Fields are quoted when needed."),
                    GUILayout.Width(170f)))
            {
                var outputPath = EditorUtility.SaveFilePanel(
                    "Export to CSV",
                    Application.dataPath,
                    "dependencies_hunter_export.csv",
                    "csv");
                if (!string.IsNullOrEmpty(outputPath))
                    ExportFilteredAssetsToCsv(outputPath, filteredAssets);
            }

            EditorGUILayout.EndHorizontal();

            _pagesScroll = EditorGUILayout.BeginScrollView(_pagesScroll);

            EditorGUILayout.BeginHorizontal();
            
            var totalCount = filteredAssets?.Count ?? 0;
            var pagesCount = totalCount / OutputSettings.PageSize + (totalCount % OutputSettings.PageSize > 0 ? 1 : 0);
            var showAllButton = totalCount <= 150;
            
            if (showAllButton)
            {
                prevColor = GUI.color;
                GUI.color = !_outputSettings.PageToShow.HasValue ? Color.yellow : Color.white;

                if (GUILayout.Button("All", GUILayout.Width(30f)))
                {
                    _outputSettings.PageToShow = null;
                }

                GUI.color = prevColor;
            }
            
            if (!showAllButton && !_outputSettings.PageToShow.HasValue && pagesCount > 0)
            {
                _outputSettings.PageToShow = 0;
            }

            for (var i = 0; i < pagesCount; i++)
            {
                prevColor = GUI.color;
                GUI.color = _outputSettings.PageToShow == i ? Color.yellow : Color.white;

                if (GUILayout.Button((i + 1).ToString(), GUILayout.Width(30f)))
                {
                    _outputSettings.PageToShow = i;
                }

                GUI.color = prevColor;
            }

            if (_outputSettings.PageToShow.HasValue && _outputSettings.PageToShow > pagesCount - 1)
            {
                _outputSettings.PageToShow = pagesCount - 1;
            }

            if (_outputSettings.PageToShow.HasValue && pagesCount == 0)
            {
                _outputSettings.PageToShow = null;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
            
            GUIUtilities.HorizontalLine();
        
            EditorGUILayout.BeginHorizontal();

            var textFieldStyle = EditorStyles.textField;
            var prevTextFieldAlignment = textFieldStyle.alignment;
            textFieldStyle.alignment = TextAnchor.MiddleCenter;
            
            _outputSettings.PathFilter = EditorGUILayout.TextField(
                new GUIContent("Path Contains:",
                    "Shows only rows whose asset path contains this substring (case-insensitive). Not a regular expression."),
                _outputSettings.PathFilter, GUILayout.Width(400f));

            textFieldStyle.alignment = prevTextFieldAlignment;
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            
            if (_analysisSettings.TryUseReflectionForAddressablesDetection)
            {
                _outputSettings.ShowAddressables = EditorGUILayout.ToggleLeft(
                    new GUIContent("Show Addressables:",
                        "When addressable detection is enabled, include rows for assets registered as Addressable in the filtered list."),
                    _outputSettings.ShowAddressables, GUILayout.Width(150));
                
                GUILayout.Space(5f);
            }
            else
            {
                _outputSettings.ShowAddressables = false;
            }
            
            if (!_result.FindUnreferencedOnly)
            {
                _outputSettings.ShowUnreferencedOnly = EditorGUILayout.ToggleLeft(
                    new GUIContent("Unreferenced Only:",
                        "After a full scan, show only assets with zero incoming references in the dependency map."),
                    _outputSettings.ShowUnreferencedOnly, GUILayout.Width(150));
                
                GUILayout.Space(5f);
            }

            _outputSettings.ShowPotentialFalsePositivesOnly = EditorGUILayout.ToggleLeft(new GUIContent("Show Only False Positive", 
                    "Shows only assets flagged as unreferenced but likely used indirectly (for example, sprites referenced only via a SpriteAtlas). Review them carefully before deleting."), 
                _outputSettings.ShowPotentialFalsePositivesOnly, GUILayout.Width(200));
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            
            prevColor = GUI.color;

            var sortType = _outputSettings.SortType;
            
            GUI.color = sortType == 0 || sortType == 1 ? Color.yellow : Color.white;
            var orderType = sortType == 1 ? "Z-A" : "A-Z";
            if (GUILayout.Button(new GUIContent("Sort by type " + orderType,
                    "Sorts by asset type name. Click again on this control to flip between ascending and descending."),
                    GUILayout.Width(150f)))
            {
                SortByType();
            }
        
            GUI.color = sortType == 2 || sortType == 3 ? Color.yellow : Color.white;
            orderType = sortType == 3 ? "Z-A" : "A-Z";
            if (GUILayout.Button(new GUIContent("Sort by path " + orderType,
                    "Sorts by full asset path. Click again to flip sort order."),
                    GUILayout.Width(150f)))
            {
                SortByPath();
            }
            
            GUI.color = sortType == 4 || sortType == 5 ? Color.yellow : Color.white;
            orderType = sortType == 5 ? "Z-A" : "A-Z";
            if (GUILayout.Button(new GUIContent("Sort by size " + orderType,
                    "Sorts by file size on disk. Click again to flip sort order."),
                    GUILayout.Width(150f)))
            {
                SortBySize();
            }
            
            GUI.color = prevColor;
            
            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();

            GUIUtilities.HorizontalLine();

            OnSelectionAndActionsGUI(filteredAssets);

            GUIUtilities.HorizontalLine();

            _typesScroll = EditorGUILayout.BeginScrollView(_typesScroll);
            
            EditorGUILayout.BeginHorizontal();

            prevColor = GUI.color;
            GUI.color = string.IsNullOrEmpty(_outputSettings.TypeFilter) ? Color.yellow : Color.white;
            
            if (GUILayout.Button("All Types", GUILayout.Width(100f)))
            {
                _outputSettings.TypeFilter = string.Empty;
            }
            
            var prevAlignment = GUI.skin.button.alignment;
            GUI.skin.button.alignment = TextAnchor.MiddleLeft;

            foreach (var typeInfo in _result.RefsByTypes)
            {
                GUI.color = _outputSettings.TypeFilter == typeInfo.Key ? Color.yellow : Color.white;

                var typeName = typeInfo.Key;
                var dotIndex = typeName.LastIndexOf(".", StringComparison.Ordinal);

                if (dotIndex != -1 && dotIndex + 1 < typeName.Length - 3)
                {
                    typeName = typeName.Substring(dotIndex + 1);
                }
                
                if (GUILayout.Button($"[{typeInfo.Value}] {typeName}", GUILayout.Width(150f)))
                {
                    _outputSettings.TypeFilter = typeInfo.Key;
                }
            }

            GUI.skin.button.alignment = prevAlignment;
            GUI.color = prevColor;

            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndScrollView();

            GUIUtilities.HorizontalLine();
            
            _assetsScroll = GUILayout.BeginScrollView(_assetsScroll);

            EditorGUILayout.BeginVertical();

            for (var i = 0; i < filteredAssets.Count; i++)
            {
                if (_outputSettings.PageToShow.HasValue)
                {
                    var page = _outputSettings.PageToShow.Value;
                    if (i < page * OutputSettings.PageSize || i >= (page + 1) * OutputSettings.PageSize)
                    {
                        continue;
                    }
                }
                
                var asset = filteredAssets[i];
                EditorGUILayout.BeginHorizontal();

                var isEligibleForDeletion = asset.ReferencesCount == 0 && !asset.IsAddressable;
                if (isEligibleForDeletion)
                {
                    asset.Selected = EditorGUILayout.Toggle(asset.Selected, GUILayout.Width(16f));
                }
                else
                {
                    GUILayout.Space(20f);
                }
                
                prevColor = GUI.color;

                var color = Color.white;
                if (!asset.ValidType)
                {
                    color = Color.red;
                }
                else if (!string.IsNullOrEmpty(asset.FalsePositiveWarning))
                {
                    color = Color.yellow;
                }

                GUI.color = color;
                
                if (string.IsNullOrEmpty(asset.FalsePositiveWarning))
                {
                    EditorGUILayout.LabelField(i.ToString(), GUILayout.Width(40f));
                }
                else
                {
                    asset.Foldout = EditorGUILayout.Foldout(asset.Foldout, i + " (i)");
                }

                var typeStr = asset.TypeName.Length > 13 ? asset.TypeName.Substring(0, 13) + ".." : asset.TypeName;
                EditorGUILayout.LabelField(typeStr, GUILayout.Width(100f));    
                GUI.color = prevColor;

                if (asset.ValidType)
                {
                    var guiContent = EditorGUIUtility.ObjectContent(null, asset.Type);
                    guiContent.text = Path.GetFileName(asset.Path);

                    var alignment = GUI.skin.button.alignment;
                    GUI.skin.button.alignment = TextAnchor.MiddleLeft;

                    if (GUILayout.Button(guiContent, GUILayout.Width(300f), GUILayout.Height(18f)))
                    {
                        Selection.objects = new[] { AssetDatabase.LoadMainAssetAtPath(asset.Path) };
                    }

                    GUI.skin.button.alignment = alignment;
                }

                EditorGUILayout.LabelField(asset.ReadableSize, GUILayout.Width(70f));
                
                if (_analysisSettings.TryUseReflectionForAddressablesDetection && _outputSettings.ShowAddressables)
                {
                    EditorGUILayout.LabelField(asset.IsAddressable ? "Addressable" : string.Empty,
                        GUILayout.Width(70f));
                }
                
                prevColor = GUI.color;
                
                if (asset.ReferencesCount > 0 && asset.ReferencedByPaths.Count > 0)
                {
                    GUI.color = asset.ShowReferencedByAssets ? Color.yellow : Color.white;

                    var refsButtonText = asset.ShowReferencedByAssets
                        ? $"Refs:{asset.ReferencesCount} >>"
                        : $"Refs:{asset.ReferencesCount}";
                    if (GUILayout.Button(new GUIContent(refsButtonText), GUILayout.Width(90f)))
                    {
                        asset.ShowReferencedByAssets = !asset.ShowReferencedByAssets;
                    }
                }
                else
                {
                    GUI.color = Color.yellow;
                    EditorGUILayout.LabelField(new GUIContent($"        Refs:{asset.ReferencesCount}"),
                        GUILayout.Width(90f));
                }

                GUI.color = prevColor;
                
                EditorGUILayout.LabelField(asset.ShortPath);

                EditorGUILayout.EndHorizontal();

                if (asset.Foldout)
                {
                    EditorGUILayout.LabelField($"[{asset.FalsePositiveWarning}]");
                }

                if (asset.ShowReferencedByAssets && asset.ReferencesCount > 0 && asset.ReferencedByPaths.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(50f);
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(new GUIContent("Used by:",
                        "Assets that reference this asset in the reverse dependency map."));

                    foreach (var referencedByPath in asset.ReferencedByPaths)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(16f);
                        GUIUtilities.DrawAssetButton(referencedByPath, 300f);
                        GUILayout.Space(8f);
                        EditorGUILayout.LabelField(referencedByPath);
                        EditorGUILayout.EndHorizontal();
                    }
                    
                    GUILayout.Space(10f);

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                }
            }

            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void OnAnalysisSettingsGUI()
        {
            EnsurePatternsLoaded();

            if (EditorSettings.serializationMode != SerializationMode.ForceText)
            {
                EditorGUILayout.HelpBox(
                    "It is recommended to set serializationMode to ForceText. Force Text serialization makes AssetReference-style GUID scanning in source files more reliable; Binary can limit what text scans see.",
                    MessageType.Error);
            }
            
            _analysisSettingsFoldout = EditorGUILayout.Foldout(_analysisSettingsFoldout,
                new GUIContent("Analysis Settings",
                    "Controls how the dependency map is built and what is listed. All changes apply on the next analysis launch."));

            if (!_analysisSettingsFoldout) 
                return;
            
            GUIUtilities.HorizontalLine();
            
            EditorGUILayout.HelpBox(
                "Analysis options are saved to Editor preferences and apply on the next analysis launch.",
                MessageType.Info);
            
            GUIUtilities.HorizontalLine();
            
            EditorGUILayout.BeginHorizontal();

            var findUnreferencedOnly = EditorGUILayout.ToggleLeft(
                new GUIContent("Find Unreferenced Assets Only",
                    "When enabled, the list only includes assets with no incoming references. When disabled, every eligible asset is listed with its reference count."),
                _analysisSettings.FindUnreferencedOnly);
            if (findUnreferencedOnly != _analysisSettings.FindUnreferencedOnly)
            {
                _analysisSettings.FindUnreferencedOnly = findUnreferencedOnly;
                _analysisSettings.SaveToEditorPrefs();
            }
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();

            var scanForAssetReferences = EditorGUILayout.ToggleLeft(
                new GUIContent("Scan Addressables AssetReferences",
                    "Also treat serialized AssetReference GUID fields as dependencies. Slower since YAML text mode parsing is used."),
                _analysisSettings.ScanForAssetReferences, GUILayout.Width(350));
            if (scanForAssetReferences != _analysisSettings.ScanForAssetReferences)
            {
                _analysisSettings.ScanForAssetReferences = scanForAssetReferences;
                _analysisSettings.SaveToEditorPrefs();
            }
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            var tryAddressables = EditorGUILayout.ToggleLeft(
                new GUIContent("Detect Addressables",
                    "Uses Addressables settings so assets that are registered Addressable can be labeled and filtered; reduces mistaken delete eligibility."),
                _analysisSettings.TryUseReflectionForAddressablesDetection);
            if (tryAddressables != _analysisSettings.TryUseReflectionForAddressablesDetection)
            {
                _analysisSettings.TryUseReflectionForAddressablesDetection = tryAddressables;
                _analysisSettings.SaveToEditorPrefs();
                _cachedFilteredAssets = null;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            var scanTerrain = EditorGUILayout.ToggleLeft(
                new GUIContent("Scan Terrain References",
                    "Adds Terrain-to-TerrainData links that the default dependency walk can miss, so terrain assets are less often marked unreferenced."),
                _analysisSettings.ScanForTerrainDataReferences);
            if (scanTerrain != _analysisSettings.ScanForTerrainDataReferences)
            {
                _analysisSettings.ScanForTerrainDataReferences = scanTerrain;
                _analysisSettings.SaveToEditorPrefs();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            GUIUtilities.HorizontalLine();

            OnSearchPatternsSettingsGUI();
        }
        
        private void OnSearchPatternsSettingsGUI()
        {
            _searchPatternsSettingsFoldout = EditorGUILayout.Foldout(_searchPatternsSettingsFoldout,
                new GUIContent(
                    $"Search Patterns Settings. Total Patterns Used: {_analysisSettings.IgnoredPatterns.Count}.",
                    "Regular expressions matched against asset paths: matches are skipped during scanning so they never appear in the result list."));

            if (!_searchPatternsSettingsFoldout) 
                return;
            
            EditorGUILayout.LabelField("Here you can setup a list of RegExp to IGNORE parts of project", GUILayout.Width(370f));
            
            GUIUtilities.HorizontalLine();
            
            if (!_analysisSettings.IsIgnoredPatternsAssetUsed)
            {
                EditorGUILayout.LabelField("By default we ignore following folders and assets:", GUILayout.Width(350f));
                
                for (var i = 0; i < _analysisSettings.IgnoredPatterns.Count; i++)
                {
                    EditorGUILayout.LabelField($"{i + 1}. {_analysisSettings.IgnoredPatterns[i]}");
                }
                
                GUIUtilities.HorizontalLine();
                
                EditorGUILayout.LabelField("However you may override it by setting you own RegExp list in a file", GUILayout.Width(450f));

                GUILayout.BeginHorizontal();
                
                GUILayout.FlexibleSpace();
                
                if (GUILayout.Button(new GUIContent("Create Settings File for Custom RegExp Patterns",
                        "Creates DependenciesHunterIgnorePatterns.asset under Assets/Editor for a custom ignore-regex list.")))
                {
                    _analysisSettings.CreateIgnoredPatternsAsset();
                }
                
                GUILayout.FlexibleSpace();
                
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button(new GUIContent("Open Settings File",
                        "Selects the ignore-patterns asset in the Project window for editing.")))
                {
                    var settings = _analysisSettings.IgnoredPatternsAsset;
                    Selection.activeObject = settings;
                    EditorGUIUtility.PingObject(settings);
                }
                
                if (GUILayout.Button(new GUIContent("Delete Settings File and Reset to Defaults",
                        "Deletes the custom asset and restores built-in default ignore patterns on the next run.")))
                {
                    _analysisSettings.DeleteIgnoredPatternsAsset();
                }
            }
            
            EditorGUILayout.HelpBox(
                "Ignore patterns changes will be applied on the next analysis launch.",
                MessageType.Info);
        }

        private void EnsurePatternsLoaded()
        {
            // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
            if (_analysisSettings == null)
            {
                _analysisSettings = new AnalysisSettings();
                _analysisSettings.LoadFromEditorPrefs();
            }
            
            if (!_analysisSettings.TriedLoadingIgnoredPatterns)
            {
                _analysisSettings.TryLoadIgnoredPatternsAsset();
            }
        }
        
        private void SortByType()
        {
            if (_outputSettings.SortType == 0)
            {
                _outputSettings.SortType = 1;
                _result.Assets?.Sort((a, b) =>
                    string.Compare(b.TypeName, a.TypeName, StringComparison.Ordinal));
            }
            else
            {
                _outputSettings.SortType = 0;
                _result.Assets?.Sort((a, b) =>
                    string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal));
            }
        }
        
        private void SortByPath()
        {
            if (_outputSettings.SortType == 2)
            {
                _outputSettings.SortType = 3;
                _result.Assets?.Sort((a, b) =>
                    string.Compare(b.Path, a.Path, StringComparison.Ordinal));
            }
            else
            {
                _outputSettings.SortType = 2;
                _result.Assets?.Sort((a, b) =>
                    string.Compare(a.Path, b.Path, StringComparison.Ordinal));
            }
        }

        private void SortBySize()
        {
            if (_outputSettings.SortType == 4)
            {
                _outputSettings.SortType = 5;
                _result.Assets?.Sort((b, a) => a.BytesSize.CompareTo(b.BytesSize));
            }
            else
            {
                _outputSettings.SortType = 4;
                _result.Assets?.Sort((a, b) => a.BytesSize.CompareTo(b.BytesSize));
            }
        }

        private void RebuildFilteredCache()
        {
            var filtered = (IEnumerable<AssetData>)_result.Assets;
            
            if (!string.IsNullOrEmpty(_outputSettings.PathFilter))
                filtered = filtered.Where(x =>
                    x.Path.IndexOf(_outputSettings.PathFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            
            if (_analysisSettings.TryUseReflectionForAddressablesDetection && !_outputSettings.ShowAddressables)
                filtered = filtered.Where(x => !x.IsAddressable);
            
            if (!string.IsNullOrEmpty(_outputSettings.TypeFilter))
                filtered = filtered.Where(x => x.TypeName == _outputSettings.TypeFilter);
            
            if (_outputSettings.ShowPotentialFalsePositivesOnly)
                filtered = filtered.Where(x => !string.IsNullOrEmpty(x.FalsePositiveWarning));
            
            if (!_result.FindUnreferencedOnly && _outputSettings.ShowUnreferencedOnly)
                filtered = filtered.Where(x => x.ReferencesCount == 0);
            
            _cachedFilteredAssets = filtered.ToList();
            
            _cachedPathFilter = _outputSettings.PathFilter;
            _cachedTypeFilter = _outputSettings.TypeFilter;
            _cachedShowAddr = _outputSettings.ShowAddressables;
            _cachedShowUnref = _outputSettings.ShowUnreferencedOnly;
            _cachedShowWarn = _outputSettings.ShowPotentialFalsePositivesOnly;
            _cachedSortType = _outputSettings.SortType;
            _cachedAddrDet = _analysisSettings.TryUseReflectionForAddressablesDetection;
        }

        private void OnSelectionAndActionsGUI(List<AssetData> filteredAssets)
        {
            if (filteredAssets.Count == 0)
                return;

            var eligibleAssets = filteredAssets.Where(a => !a.IsAddressable && a.ReferencesCount == 0).ToList();
            var selectedAssets = eligibleAssets.Where(a => a.Selected).ToList();
            var selectedCount = selectedAssets.Count;

            EditorGUILayout.BeginHorizontal();

            var newAllSelected = EditorGUILayout.Toggle(
                new GUIContent(string.Empty, "Selects or clears every asset for backup or delete (unreferenced and not Addressable)."),
                _allSelected, GUILayout.Width(16f));
            if (newAllSelected != _allSelected)
            {
                _allSelected = newAllSelected;
                foreach (var asset in eligibleAssets)
                    asset.Selected = _allSelected;
            }

            EditorGUILayout.LabelField(new GUIContent("Select All Unreferenced", "Applies to all assets in the filtered list."), GUILayout.Width(150f));
            GUILayout.Space(10f);

            var selectedColor = GUI.color;
            GUI.color = selectedCount > 0 ? Color.yellow : Color.gray;
            EditorGUILayout.LabelField($"Selected: {selectedCount}", GUILayout.Width(90f));
            GUI.color = selectedColor;

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
            
            if (selectedCount > 0)
            {
                EditorGUILayout.BeginHorizontal();

                if (string.IsNullOrEmpty(_backupDirectory))
                {
                    var directory = Directory.GetParent(Application.dataPath);
                    _backupDirectory = directory != null ? Path.Combine(directory.FullName, "Backups", "DependenciesHunter") : Application.dataPath;
                }

                EditorGUILayout.LabelField(new GUIContent("Backup Dir:", "Folder where Backup operations copy Assets-relative paths (each asset and its .meta)."), GUILayout.Width(75f));
                _backupDirectory = EditorGUILayout.TextField(_backupDirectory);
                if (GUILayout.Button(new GUIContent("Browse", "Choose the backup root directory on disk."), GUILayout.Width(60f)))
                {
                    var chosen = EditorUtility.OpenFolderPanel("Select Backup Directory", _backupDirectory, "");
                    if (!string.IsNullOrEmpty(chosen))
                        _backupDirectory = chosen;
                }

                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
                if (GUILayout.Button(new GUIContent($"Backup Selected ({selectedCount})",
                        "Copies selected eligible assets and their .meta files into Backup Dir, preserving the Assets/ path structure."), GUILayout.Width(170f)))
                {
                    if (EditorUtility.DisplayDialog("DependenciesHunter",
                        $"Back up {selectedCount} asset(s) to\n{_backupDirectory}?",
                        "Ok", "Cancel"))
                    {
                        var backedUpCount = BackupUtilities.BackupAssets(selectedAssets, _backupDirectory);
                        Debug.Log($"Backed up {backedUpCount} assets to {_backupDirectory}");
                        EditorUtility.DisplayDialog("DependenciesHunter",
                            $"Backed up {backedUpCount} asset(s) to\n{_backupDirectory}", "Ok");
                    }
                }

                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button(new GUIContent($"Delete Selected ({selectedCount})",
                        "Permanently deletes selected eligible assets from the project. Addressable or referenced assets cannot be selected. This cannot be undone."), GUILayout.Width(170f)))
                {
                    if (EditorUtility.DisplayDialog("DependenciesHunter",
                        $"Delete {selectedCount} asset(s)? This cannot be undone.",
                        "Delete", "Cancel"))
                    {
                        var deletedPaths = DeleteUnreferencedAssets(selectedAssets);
                        Debug.Log($"Deleted {deletedPaths.Count} assets");
                        _result.Assets.RemoveAll(a => deletedPaths.Contains(a.Path));
                        _cachedFilteredAssets = null;
                        _allSelected = false;
                        EditorUtility.DisplayDialog("DependenciesHunter",
                            $"Deleted {deletedPaths.Count} asset(s).", "Ok");
                    }
                }

                GUI.backgroundColor = new Color(1f, 0.6f, 0.2f);
                if (GUILayout.Button(new GUIContent($"Backup + Delete ({selectedCount})",
                        "Runs backup to Backup Dir, then deletes the same assets from the project. Confirms once before proceeding."), GUILayout.Width(170f)))
                {
                    if (EditorUtility.DisplayDialog("DependenciesHunter",
                            $"Back up and delete {selectedCount} asset(s)?\n\nBackup: {_backupDirectory}",
                            "Ok", "Cancel"))
                    {
                        var backedUpCount = BackupUtilities.BackupAssets(selectedAssets, _backupDirectory);
                        Debug.Log($"Backed up {backedUpCount} assets to {_backupDirectory}");
                        var deletedPaths = DeleteUnreferencedAssets(selectedAssets);
                        Debug.Log($"Deleted {deletedPaths.Count} assets");
                        _result.Assets.RemoveAll(a => deletedPaths.Contains(a.Path));
                        _cachedFilteredAssets = null;
                        _allSelected = false;
                        EditorUtility.DisplayDialog("DependenciesHunter",
                            $"Backed up {backedUpCount}, Deleted {deletedPaths.Count} asset(s).", "Ok");
                    }
                }

                GUI.backgroundColor = Color.white;

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private static void ExportFilteredAssetsToCsv(string path, List<AssetData> assets)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Type,Size,Path,References,Addressable,Warning");
            foreach (var asset in assets)
            {
                sb.Append(EscapeCsvField(asset.TypeName)).Append(',')
                    .Append(EscapeCsvField(asset.ReadableSize)).Append(',')
                    .Append(EscapeCsvField(asset.Path)).Append(',')
                    .Append(asset.ReferencesCount).Append(',')
                    .Append(asset.IsAddressable ? "True" : "False").Append(',')
                    .Append(EscapeCsvField(asset.FalsePositiveWarning ?? string.Empty))
                    .AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeCsvField(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.IndexOfAny(new[] { '"', ',', '\n', '\r' }) >= 0)
                return '"' + value.Replace("\"", "\"\"") + '"';

            return value;
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
        private static SelectedAssetsAnalysisUtilities _service;
        private static bool _searchForAssetReferences;
        
        private Dictionary<Object, List<string>> _lastResults;
        private Object[] _selectedObjects;
        private List<string> _selectedAssetPaths = new List<string>();
        private List<string> _missingAssetPaths = new List<string>();
        private bool _hasProjectChangesSinceLastRun;
        private readonly Dictionary<string, bool> _foldoutByPath = new Dictionary<string, bool>();

        private bool _analysisSettingsFoldout;
        private string _searchFilter = string.Empty;
        private ListFilterMode _listFilterMode;
        private ResultsSortMode _resultsSortMode = ResultsSortMode.RefsAsc;
        private DependenciesSortMode _dependenciesSortMode = DependenciesSortMode.PathAsc;

        private enum ListFilterMode
        {
            // ReSharper disable once UnusedMember.Local
            All,
            WithDependenciesOnly,
            ZeroDependenciesOnly
        }

        private enum DependenciesSortMode
        {
            PathAsc,
            PathDesc,
            TypeAsc,
            TypeDesc
        }

        private enum ResultsSortMode
        {
            RefsAsc,
            RefsDesc,
            PathAsc,
            PathDesc
        }

        private class SelectedAssetEntry
        {
            public string SelectedPath;
            public List<string> Dependencies;
            public bool IsAddressable;
            public bool IsInResources;

            public bool HasWarning => IsAddressable || IsInResources;
        }

        private Vector2 _scrollPos = Vector2.zero;

        [MenuItem("Assets/[DH] Find References In Project", false, 20)]
        public static void FindReferences()
        {
            var window = GetWindow<SelectedAssetsReferencesWindow>("Selected Assets");
            _searchForAssetReferences = EditorPrefs.GetBool(AllProjectAssetsReferencesWindow.AnalysisSettings.PrefsKeys.ScanForAssetReferences, false);
            window.CaptureSelectionPaths();
            window.RefreshAnalysis();
        }

        private void CaptureSelectionPaths()
        {
            _selectedAssetPaths = Selection.objects
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .ToList();
        }

        private void RefreshAnalysis()
        {
            _service ??= new SelectedAssetsAnalysisUtilities();

            Show();

            _selectedObjects = ResolveSelectedObjectsFromPaths(out _missingAssetPaths);
            _hasProjectChangesSinceLastRun = false;

            var stopWatch = new Stopwatch();
            stopWatch.Start();

            _lastResults = _service.GetReferences(_selectedObjects,
                _searchForAssetReferences,
                EditorSettings.serializationMode != SerializationMode.ForceText);

            EditorUtility.DisplayProgressBar("DependenciesHunter", "Preparing Assets", 1f);
            EditorUtility.UnloadUnusedAssetsImmediate();
            EditorUtility.ClearProgressBar();

            InitializeFoldouts();

            stopWatch.Stop();
            Debug.Log($"Scanning took: {stopWatch.Elapsed.TotalSeconds} sec");
        }

        private Object[] ResolveSelectedObjectsFromPaths(out List<string> missingPaths)
        {
            missingPaths = new List<string>();
            var resolved = new List<Object>(_selectedAssetPaths.Count);

            foreach (var path in _selectedAssetPaths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null)
                {
                    missingPaths.Add(path);
                    continue;
                }

                resolved.Add(asset);
            }

            return resolved.ToArray();
        }

        private void InitializeFoldouts()
        {
            if (_selectedObjects == null)
                return;

            var validPaths = new HashSet<string>();
            for (var i = 0; i < _selectedObjects.Length; i++)
            {
                var path = AssetDatabase.GetAssetPath(_selectedObjects[i]);
                if (string.IsNullOrEmpty(path))
                    continue;

                validPaths.Add(path);
                if (_foldoutByPath.ContainsKey(path))
                    continue;

                _foldoutByPath[path] = _selectedObjects.Length < 7 || i == 0;
            }

            var toRemove = _foldoutByPath.Keys.Where(k => !validPaths.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                _foldoutByPath.Remove(key);
            }
        }

        private void Clear()
        {
            _selectedObjects = null;
            _lastResults = null;
            _service = null;
            _selectedAssetPaths.Clear();
            _missingAssetPaths.Clear();
            _hasProjectChangesSinceLastRun = false;
            _foldoutByPath.Clear();

            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        private void DrawEmptyWindowInfo()
        {
            EditorGUILayout.HelpBox(
                "Please select assets in context menu and select '[DH] Find References In Project' to start analysis.",
                MessageType.Info);
        }

        private void DrawStateWarnings()
        {
            if (_hasProjectChangesSinceLastRun)
            {
                EditorGUILayout.HelpBox(
                    "Project changed since last analysis. Press Re-run to refresh references for the preserved selection.",
                    MessageType.Warning);
            }

            if (_missingAssetPaths.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{_missingAssetPaths.Count} selected asset(s) are missing or were removed. Re-run includes only existing assets.",
                    MessageType.Warning);
            }
        }

        private void OnGUI()
        {
            if (_lastResults == null)
            {
                DrawEmptyWindowInfo();
                return;
            }

            if (_selectedObjects == null)
            {
                Clear();
                return;
            }

            GUILayout.BeginVertical();
            DrawStateWarnings();
            
            GUILayout.BeginHorizontal();
            
            GUILayout.FlexibleSpace();
            
            var prevColor = GUI.color;
            GUI.color = Color.yellow;
            
            if (GUILayout.Button(new GUIContent("Re-run",
                        "Rebuilds references for current selection with current shared settings."),
                    GUILayout.Width(100f)))
            {
                RefreshAnalysis();
            }
            
            GUI.color = prevColor;
            
            GUILayout.FlexibleSpace();
            
            GUILayout.EndHorizontal();
            
            OnAnalysisSettingsGUI();
            GUIUtilities.HorizontalLine();

            var entries = BuildEntries();
            entries = SortEntries(entries).ToList();
            DrawHeaderToolbar(entries);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            foreach (var entry in entries)
            {
                if (!PassesFilters(entry))
                    continue;

                DrawAssetEntry(entry);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void OnAnalysisSettingsGUI()
        {
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
            {
                EditorGUILayout.HelpBox(
                    "It is recommended to set serializationMode to ForceText. Force Text serialization makes AssetReference-style GUID scanning in source files more reliable; Binary can limit what text scans see.",
                    MessageType.Error);
            }
            
            _analysisSettingsFoldout = EditorGUILayout.Foldout(_analysisSettingsFoldout,
                new GUIContent("Analysis Settings",
                    "Controls how the dependency map is built and what is listed. All changes apply on the next analysis launch."));

            if (!_analysisSettingsFoldout)
                return;

            GUIUtilities.HorizontalLine();
            
            var scanForAssetReferencesDefault = EditorPrefs.GetBool(AllProjectAssetsReferencesWindow.AnalysisSettings.PrefsKeys.ScanForAssetReferences, false);
            var scanForAssetReferences = EditorGUILayout.ToggleLeft(
                new GUIContent("Scan Addressables AssetReferences",
                    "Treat serialized AssetReference GUID fields as dependencies in analysis."),
                scanForAssetReferencesDefault);
            if (scanForAssetReferences != scanForAssetReferencesDefault)
            {
                EditorPrefs.SetBool(AllProjectAssetsReferencesWindow.AnalysisSettings.PrefsKeys.ScanForAssetReferences, scanForAssetReferences);
                _searchForAssetReferences = scanForAssetReferences;
            }

            var detectAddressablesDefault =
                EditorPrefs.GetBool(AllProjectAssetsReferencesWindow.AnalysisSettings.PrefsKeys.TryUseReflectionForAddressablesDetection, false);
            var detectAddressables = EditorGUILayout.ToggleLeft(
                new GUIContent("Detect Addressables",
                    "Uses reflection over Addressables settings; helps label potential false positives."),
                detectAddressablesDefault);
            if (detectAddressables != detectAddressablesDefault)
                EditorPrefs.SetBool(AllProjectAssetsReferencesWindow.AnalysisSettings.PrefsKeys.TryUseReflectionForAddressablesDetection, detectAddressables);

            var scanTerrainDefault = EditorPrefs.GetBool(AllProjectAssetsReferencesWindow.AnalysisSettings.PrefsKeys.ScanForTerrainDataReferences, false);
            var scanTerrain = EditorGUILayout.ToggleLeft(
                new GUIContent("Scan Terrain References",
                    "Adds extra TerrainData reference detection in full-project map."),
                scanTerrainDefault);
            if (scanTerrain != scanTerrainDefault)
                EditorPrefs.SetBool(AllProjectAssetsReferencesWindow.AnalysisSettings.PrefsKeys.ScanForTerrainDataReferences, scanTerrain);

            EditorGUILayout.HelpBox(
                "Settings are saved immediately. Press Re-run to apply them to this view.",
                MessageType.Info);
        }

        private void DrawHeaderToolbar(List<SelectedAssetEntry> entries)
        {
            var withDependenciesCount = entries.Count(e => e.Dependencies.Count > 0);

            EditorGUILayout.BeginHorizontal();
            
            GUILayout.Label($"Selected: {entries.Count}");
            GUILayout.Space(6f);
            GUILayout.Label($"With dependencies: {withDependenciesCount}");
            GUILayout.Space(6f);
            GUILayout.Label($"Zero dependencies: {entries.Count - withDependenciesCount}");
            
            GUILayout.Space(6f);
            
            EditorGUILayout.LabelField(new GUIContent("Filter:", "Show all rows, only with deps, only zero-deps, or only warning rows."), GUILayout.Width(60f));
            _listFilterMode = (ListFilterMode)EditorGUILayout.EnumPopup(
                _listFilterMode, GUILayout.Width(150f));
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            
            GUILayout.Space(5f);
            
            EditorGUILayout.LabelField(new GUIContent("Search:", "Filters selected assets by path and dependency paths (case-insensitive)."), GUILayout.Width(60f));
            _searchFilter = EditorGUILayout.TextField(_searchFilter, GUILayout.Width(150f));

            GUILayout.Space(6f);
            
            EditorGUILayout.LabelField(new GUIContent("Sort dependencies:", "Sort order used inside each expanded dependency list."), GUILayout.Width(120f));
            _dependenciesSortMode = (DependenciesSortMode)EditorGUILayout.EnumPopup(
                _dependenciesSortMode, GUILayout.Width(150));
            
            GUILayout.Space(6f);

            EditorGUILayout.LabelField(new GUIContent("Sort results:", "Sorting order for selected assets list."), GUILayout.Width(80f));
            _resultsSortMode = (ResultsSortMode)EditorGUILayout.EnumPopup(
                _resultsSortMode, GUILayout.Width(100f));

            GUILayout.FlexibleSpace();

            var hasCollapsed = _foldoutByPath.Values.Any(x => !x);
            var hasExpanded = _foldoutByPath.Values.Any(x => x);

            using (new EditorGUI.DisabledScope(!hasCollapsed))
            {
                if (GUILayout.Button("Expand All", GUILayout.Width(90f)))
                    SetAllFoldouts(true);
            }
            using (new EditorGUI.DisabledScope(!hasExpanded))
            {
                if (GUILayout.Button("Collapse All", GUILayout.Width(90f)))
                    SetAllFoldouts(false);
            }

            EditorGUILayout.EndHorizontal();
        }

        private List<SelectedAssetEntry> BuildEntries()
        {
            var detectAddressables = EditorPrefs.GetBool(AllProjectAssetsReferencesWindow.AnalysisSettings.PrefsKeys.TryUseReflectionForAddressablesDetection, false);
            var entries = new List<SelectedAssetEntry>(_selectedObjects.Length);

            foreach (var selectedObject in _selectedObjects)
            {
                if (selectedObject == null)
                    continue;

                var selectedPath = AssetDatabase.GetAssetPath(selectedObject);
                if (string.IsNullOrEmpty(selectedPath))
                    continue;

                if (!_lastResults.TryGetValue(selectedObject, out var dependencies))
                    dependencies = new List<string>();

                var pathToSearch = selectedPath.Replace("\\", "/");
                entries.Add(new SelectedAssetEntry
                {
                    SelectedPath = selectedPath,
                    Dependencies = dependencies,
                    IsAddressable = CommonUtilities.IsAssetAddressable(selectedPath, detectAddressables),
                    IsInResources = pathToSearch.Contains("/Resources/")
                });
            }

            return entries;
        }

        private IEnumerable<SelectedAssetEntry> SortEntries(IEnumerable<SelectedAssetEntry> entries)
        {
            switch (_resultsSortMode)
            {
                case ResultsSortMode.RefsDesc:
                    return entries.OrderByDescending(e => e.Dependencies.Count)
                        .ThenBy(e => e.SelectedPath, StringComparer.Ordinal);
                case ResultsSortMode.PathAsc:
                    return entries.OrderBy(e => e.SelectedPath, StringComparer.Ordinal);
                case ResultsSortMode.PathDesc:
                    return entries.OrderByDescending(e => e.SelectedPath, StringComparer.Ordinal);
                case ResultsSortMode.RefsAsc:
                default:
                    return entries.OrderBy(e => e.Dependencies.Count)
                        .ThenBy(e => e.SelectedPath, StringComparer.Ordinal);
            }
        }

        private bool PassesFilters(SelectedAssetEntry entry)
        {
            if (!string.IsNullOrEmpty(_searchFilter))
            {
                var hasPathMatch = entry.SelectedPath.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hasPathMatch)
                {
                    hasPathMatch = entry.Dependencies.Any(d =>
                        d.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (!hasPathMatch)
                    return false;
            }

            switch (_listFilterMode)
            {
                case ListFilterMode.WithDependenciesOnly:
                    return entry.Dependencies.Count > 0;
                case ListFilterMode.ZeroDependenciesOnly:
                    return entry.Dependencies.Count == 0;
                default:
                    return true;
            }
        }

        private void DrawAssetEntry(SelectedAssetEntry entry)
        {
            GUIUtilities.HorizontalLine();

            if (!_foldoutByPath.TryGetValue(entry.SelectedPath, out var foldout))
            {
                foldout = false;
                _foldoutByPath[entry.SelectedPath] = false;
            }

            EditorGUILayout.BeginHorizontal();

            var prevColor = GUI.color;
            if (entry.Dependencies.Count == 0)
                GUI.color = Color.yellow;
            
            var selectedObjectName = Path.GetFileNameWithoutExtension(entry.SelectedPath);
            var header = $"{selectedObjectName} is used by [{entry.Dependencies.Count}] " + (entry.Dependencies.Count == 1 ? "asset" : "assets");
            
            if (entry.Dependencies.Count == 0)
            {
                GUILayout.Space(14f);
                EditorGUILayout.LabelField(header);
            }
            else
            {
                foldout = EditorGUILayout.Foldout(foldout, header, true);
            }
            
            _foldoutByPath[entry.SelectedPath] = foldout;
            
            GUILayout.FlexibleSpace();
            
            GUI.color = entry.IsAddressable ? Color.cyan : prevColor;
            if (entry.IsAddressable)
                GUILayout.Label("[Addressable]");

            GUI.color = entry.IsInResources ? Color.cyan : prevColor;
            if (entry.IsInResources)
                GUILayout.Label("[Resources]");

            GUI.color = prevColor;
            
            GUIUtilities.DrawAssetButton(entry.SelectedPath, 350f);
            
            EditorGUILayout.EndHorizontal();
            
            if (!foldout || entry.Dependencies.Count == 0)
                return;
            
            GUILayout.Space(5f);

            if (entry.Dependencies.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20f);
                
                if (GUILayout.Button(
                    new GUIContent("Export to Clipboard", "Copies dependency paths for this selected asset."),
                    GUILayout.Width(120)))
                {
                    EditorGUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, entry.Dependencies);
                    Debug.Log($"Copied {entry.Dependencies.Count} dependencies for {entry.SelectedPath}");
                }
                
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                if (entry.HasWarning)
                {
                    var warnings = new List<string>();
                    if (entry.IsAddressable)
                        warnings.Add("Addressable");
                    if (entry.IsInResources)
                        warnings.Add("Resources");

                    EditorGUILayout.HelpBox(
                        $"Potential false positive: no incoming refs, but asset is reachable via {string.Join(" + ", warnings)}.",
                        MessageType.Warning);
                }
                else
                {
                    GUILayout.Label("No dependencies found");
                }
            }

            foreach (var resultPath in SortDependencies(entry.Dependencies))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20f);
                GUIUtilities.DrawAssetButton(resultPath, 350f);
                GUILayout.Space(10f);
                GUILayout.Label(resultPath);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private IEnumerable<string> SortDependencies(IEnumerable<string> dependencies)
        {
            switch (_dependenciesSortMode)
            {
                case DependenciesSortMode.PathDesc:
                    return dependencies.OrderByDescending(x => x, StringComparer.Ordinal);
                case DependenciesSortMode.TypeAsc:
                    return dependencies.OrderBy(GetTypeNameForPath, StringComparer.Ordinal)
                        .ThenBy(x => x, StringComparer.Ordinal);
                case DependenciesSortMode.TypeDesc:
                    return dependencies.OrderByDescending(GetTypeNameForPath, StringComparer.Ordinal)
                        .ThenBy(x => x, StringComparer.Ordinal);
                case DependenciesSortMode.PathAsc:
                default:
                    return dependencies.OrderBy(x => x, StringComparer.Ordinal);
            }
        }

        private static string GetTypeNameForPath(string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null ? type.Name : "Unknown";
        }

        private void SetAllFoldouts(bool value)
        {
            var keys = _foldoutByPath.Keys.ToList();
            foreach (var key in keys)
            {
                _foldoutByPath[key] = value;
            }
        }

        private void OnProjectChange()
        {
            _hasProjectChangesSinceLastRun = true;
            _service = null;
        }

        private void OnDestroy()
        {
            Clear();
        }
    }

    public class ProjectAssetsAnalysisUtilities
    {
        private HashSet<string> _iconPaths;

        public bool IsValidAssetType(string path, Type type, bool validForOutput)
        {
            if (type == null)
            {
                if (validForOutput)
                    Debug.Log($"Unable to detect asset type at {path}");
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
        
        public static List<Regex> CompilePatterns(List<string> patterns)
        {
            var compiled = new List<Regex>(patterns.Count);
            foreach (var pattern in patterns)
            {
                if (!string.IsNullOrEmpty(pattern))
                    compiled.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant));
            }
            return compiled;
        }

        public static bool IsValidForOutput(string path, List<Regex> compiledPatterns)
        {
            foreach (var t in compiledPatterns)
            {
                if (t.IsMatch(path))
                    return false;
            }

            return true;
        }

        private bool UsedAsProjectIcon(string texturePath)
        {
            if (_iconPaths == null)
            {
                FindAllIcons();
            }

            return _iconPaths != null && _iconPaths.Contains(texturePath);
        }

        private void FindAllIcons()
        {
            _iconPaths = new HashSet<string>();

            var icons = new List<Texture2D>();

            #if UNITY_2021_2_OR_NEWER
            foreach (var buildTargetField in typeof(NamedBuildTarget).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (buildTargetField.Name == "Unknown")
                    continue;
                if (buildTargetField.FieldType != typeof(NamedBuildTarget))
                    continue;

                NamedBuildTarget buildTarget = (NamedBuildTarget) buildTargetField.GetValue(null);
                icons.AddRange(PlayerSettings.GetIcons(buildTarget, IconKind.Any));
            }
            #else
            foreach (var targetGroup in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                icons.AddRange(PlayerSettings.GetIconsForTargetGroup((BuildTargetGroup) targetGroup));
            }
            #endif

            foreach (var icon in icons)
            {
                _iconPaths.Add(AssetDatabase.GetAssetPath(icon));
            }
        }
    }

    public class SelectedAssetsAnalysisUtilities
    {
        private Dictionary<string, List<string>> _cachedAssetsMap;
        private bool? _cachedScanForAssetReferences;
        private bool? _cachedBinarySerialization;

        public Dictionary<Object, List<string>> GetReferences(Object[] selectedObjects, bool scanAssetReferences, bool binarySerialization)
        {
            if (selectedObjects == null)
            {
                Debug.Log("No selected objects passed");
                return new Dictionary<Object, List<string>>();
            }

            var shouldRebuildCache = _cachedAssetsMap == null
                                     || _cachedScanForAssetReferences != scanAssetReferences
                                     || _cachedBinarySerialization != binarySerialization;

            if (shouldRebuildCache)
            {
                DependenciesMapUtilities.FillReverseDependenciesMap(scanAssetReferences, binarySerialization, false, out _cachedAssetsMap);
                _cachedScanForAssetReferences = scanAssetReferences;
                _cachedBinarySerialization = binarySerialization;
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

                if (source.TryGetValue(selectedObjectPath, out var deps))
                {
                    results.Add(selectedObject, deps);
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
        public static void FillReverseDependenciesMap(bool scanAssetReferences, bool binarySerialization, bool scanTerrainDataReferences, out Dictionary<string, List<string>> reverseDependencies)
        {
            var assetPaths = AssetDatabase.GetAllAssetPaths().ToList();

            reverseDependencies = assetPaths.ToDictionary(assetPath => assetPath, _ => new List<string>());

            Debug.Log($"Total Assets Count: {assetPaths.Count}");

            var totalAssets = assetPaths.Count;
            var progressInterval = Math.Max(1, totalAssets / 100);

            for (var i = 0; i < totalAssets; i++)
            {
                if (i % progressInterval == 0)
                {
                    EditorUtility.DisplayProgressBar("Dependencies Hunter", "Creating a map of dependencies",
                        (float)i / totalAssets);
                }
                
                var assetDependencies =
                    scanAssetReferences ? GetAllDependencies(assetPaths[i], binarySerialization, false)
                        : AssetDatabase.GetDependencies(assetPaths[i], false);

                foreach (var assetDependency in assetDependencies)
                {
                    if (reverseDependencies.TryGetValue(assetDependency, out var list) && assetDependency != assetPaths[i])
                    {
                        list.Add(assetPaths[i]);
                    }
                }
            }

            if (scanTerrainDataReferences)
                ScanTerrainDataReferences(reverseDependencies);
        }
        
        private static readonly Regex GuidRegex = new Regex(@"m_AssetGUID:\s*([0-9a-fA-F]{32})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static string[] GetAllDependencies(string assetPath, bool binarySerialization, bool recursive = true)
        {
            var regularDependencies = AssetDatabase.GetDependencies(assetPath, recursive);
            
            if (!CanContainAssetReferencesByExtension(assetPath))
                return regularDependencies;

            if (binarySerialization)
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);

                if (obj == null)
                    return regularDependencies;

                HashSet<string> result = null;

                var serializedObj = new SerializedObject(obj);
                var iterator = serializedObj.GetIterator();

                while (iterator.NextVisible(true))
                {
                    if (iterator.propertyType != SerializedPropertyType.Generic ||
                        !iterator.type.Contains("AssetReference"))
                        continue;

                    var guidProp = iterator.FindPropertyRelative("m_AssetGUID");
                    if (guidProp == null || string.IsNullOrEmpty(guidProp.stringValue))
                        continue;

                    var refPath = AssetDatabase.GUIDToAssetPath(guidProp.stringValue);
                    if (!string.IsNullOrEmpty(refPath))
                    {
                        result ??= regularDependencies.ToHashSet();
                        result.Add(refPath);
                    }
                }
                
                return result != null ? result.ToArray() : regularDependencies;
            }
            else
            {
                if (!File.Exists(assetPath))
                    return regularDependencies;

                var content = File.ReadAllText(assetPath);

                if (!content.Contains("m_AssetGUID"))
                    return regularDependencies;
                
                HashSet<string> result = null;

                foreach (Match match in GuidRegex.Matches(content))
                {
                    if (match == null || match.Groups.Count <= 1)
                        continue;
                    
                    var guid = match.Groups[1].Value;
                    
                    if (string.IsNullOrEmpty(guid))
                        continue;

                    var refPath = AssetDatabase.GUIDToAssetPath(guid);

                    if (!string.IsNullOrEmpty(refPath))
                    {
                        result ??= regularDependencies.ToHashSet();
                        result.Add(refPath);
                    }
                }

                return result != null ? result.ToArray() : regularDependencies;
            }
        }

        private static bool CanContainAssetReferencesByExtension(string assetPath)
        {
            var extension = Path.GetExtension(assetPath).ToLowerInvariant();

            switch (extension)
            {
                case ".asset":
                case ".prefab":
                case ".unity":
                    return true;
                default:
                    return false;
            }
        }

        private static void ScanTerrainDataReferences(Dictionary<string, List<string>> reverseDependencies)
        {
            var terrainDataGuids = AssetDatabase.FindAssets("t:TerrainData");
            if (terrainDataGuids.Length == 0) return;

            var guidToPath = new Dictionary<string, string>();
            foreach (var guid in terrainDataGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    guidToPath[guid] = path;
            }

            if (guidToPath.Count == 0) return;

            var candidateExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".prefab", ".unity" };
            var candidatePaths = AssetDatabase.GetAllAssetPaths()
                .Where(p => candidateExtensions.Contains(Path.GetExtension(p)))
                .ToList();

            foreach (var candidatePath in candidatePaths)
            {
                if (!File.Exists(candidatePath)) continue;

                string content = null;
                foreach (var kvp in guidToPath)
                {
                    content ??= File.ReadAllText(candidatePath);

                    if (!content.Contains(kvp.Key)) continue;

                    if (reverseDependencies.TryGetValue(kvp.Value, out var list) &&
                        !list.Contains(candidatePath))
                    {
                        list.Add(candidatePath);
                    }
                }
            }
        }
    }

    public class AssetData
    {
        public static AssetData Create(
            string path,
            Type type,
            int referencesCount,
            List<string> referencedByPaths,
            string falsePositiveWarning,
            bool tryUseReflectionForAddressablesDetection)
        {
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

            var isAddressable = CommonUtilities.IsAssetAddressable(
                path,
                tryUseReflectionForAddressablesDetection);

            var bytesSize = 0L;

            try
            {
                var fileInfo = new FileInfo(path);
                bytesSize = fileInfo.Length;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error reading file {path} with error: {e}. Unable to detect its size.");
            }

            return new AssetData(path, type, typeName, bytesSize, 
                CommonUtilities.GetReadableSize(bytesSize), isAddressable, referencesCount, referencedByPaths,
                falsePositiveWarning);
        }
        
        private AssetData(string path, Type type, string typeName, long bytesSize, 
            string readableSize, bool addressable, int referencesCount, List<string> referencedByPaths,
            string falsePositiveWarning)
        {
            Path = path;
            ShortPath = Path.Replace("Assets/", string.Empty);
            Type = type;
            TypeName = typeName;
            BytesSize = bytesSize;
            ReadableSize = readableSize;
            IsAddressable = addressable;
            ReferencesCount = referencesCount;
            ReferencedByPaths = referencedByPaths != null
                ? new List<string>(referencedByPaths)
                : new List<string>();
            FalsePositiveWarning = falsePositiveWarning;
        }

        public string Path { get; }
        public string ShortPath { get; }
        public Type Type { get; }
        public string TypeName { get; }
        public long BytesSize { get; }
        public string ReadableSize { get; }
        public bool IsAddressable { get; }
        public int ReferencesCount { get; }
        public List<string> ReferencedByPaths { get; }
        public string FalsePositiveWarning { get; }
        public bool ValidType => Type != null;
        public bool Foldout { get; set; }
        public bool ShowReferencedByAssets { get; set; }
        public bool Selected { get; set; }
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
        
        public static bool DrawColoredFoldout(bool value, string text, Color color)
        {
            var prevColor = GUI.color;
            GUI.color = color;
            var result = EditorGUILayout.Foldout(value, text);
            GUI.color = prevColor;
            return result;
        }

        public static void DrawColoredLabel(string text, Color color)
        {
            var prevColor = GUI.color;
            GUI.color = color;
            GUILayout.Label(text);
            GUI.color = prevColor;
        }
        
        public static void DrawAssetButton(string assetPath, float width)
        {
            var selectedObjectType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            var selectedObjectContent = EditorGUIUtility.ObjectContent(null, selectedObjectType);
            selectedObjectContent.text = Path.GetFileName(assetPath);

            var alignment = GUI.skin.button.alignment;
            GUI.skin.button.alignment = TextAnchor.MiddleLeft;

            if (GUILayout.Button(selectedObjectContent, GUILayout.Width(width), GUILayout.Height(18f)))
            {
                Selection.objects = new[] { AssetDatabase.LoadMainAssetAtPath(assetPath) };
            }

            GUI.skin.button.alignment = alignment;
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
        
        private static readonly Dictionary<string, bool> AddressablesByGuidCache = new Dictionary<string, bool>();
        private static bool _addressablesReflectionInitialized;
        private static bool _addressablesReflectionAvailable;
        private static bool _addressablesReflectionWarningLogged;
        private static PropertyInfo _addressablesSettingsProperty;
        private static MethodInfo _findAssetEntryMethod;
        private static int _findAssetEntryParametersCount;
        private static readonly object[] FindAssetEntrySingleArgument = new object[1];
        private static readonly object[] FindAssetEntryDoubleArguments = new object[2];

        public static void ClearAddressablesCache()
        {
            AddressablesByGuidCache.Clear();
        }

        public static bool IsAssetAddressable(string assetPath, bool tryUseReflection)
        {
            if (!tryUseReflection || string.IsNullOrEmpty(assetPath))
                return false;
            
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                    return false;

                if (AddressablesByGuidCache.TryGetValue(guid, out var cachedResult))
                    return cachedResult;

                var result = IsGuidAddressable(guid);
                AddressablesByGuidCache[guid] = result;
                return result;
            }
            catch (Exception e)
            {
                LogAddressablesReflectionWarning($"checking asset {assetPath}", e);
                return false;
            }
        }

        private static bool IsGuidAddressable(string guid)
        {
            EnsureAddressablesReflectionInitialized();
            if (!_addressablesReflectionAvailable)
                return false;

            try
            {
                var settings = _addressablesSettingsProperty.GetValue(null, null);
                if (settings == null)
                    return false;

                object entry;
                if (_findAssetEntryParametersCount == 1)
                {
                    FindAssetEntrySingleArgument[0] = guid;
                    entry = _findAssetEntryMethod.Invoke(settings, FindAssetEntrySingleArgument);
                    FindAssetEntrySingleArgument[0] = null;
                }
                else
                {
                    FindAssetEntryDoubleArguments[0] = guid;
                    FindAssetEntryDoubleArguments[1] = true;
                    entry = _findAssetEntryMethod.Invoke(settings, FindAssetEntryDoubleArguments);
                    FindAssetEntryDoubleArguments[0] = null;
                    FindAssetEntryDoubleArguments[1] = null;
                }

                return entry != null;
            }
            catch (Exception e)
            {
                _addressablesReflectionAvailable = false;
                LogAddressablesReflectionWarning($"checking guid {guid}", e);
                return false;
            }
        }

        private static void EnsureAddressablesReflectionInitialized()
        {
            if (_addressablesReflectionInitialized)
                return;

            _addressablesReflectionInitialized = true;

            try
            {
                Type defaultObjectType = null;
                Type settingsType = null;
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (var assembly in assemblies)
                {
                    defaultObjectType ??= assembly.GetType(
                        "UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsDefaultObject",
                        false);
                    defaultObjectType ??= assembly.GetType(
                        "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject",
                        false);

                    settingsType ??= assembly.GetType(
                        "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings",
                        false);
                    settingsType ??= assembly.GetType(
                        "UnityEditor.AddressableAssets.AddressableAssetSettings",
                        false);

                    if (defaultObjectType != null && settingsType != null)
                        break;
                }

                if (defaultObjectType == null || settingsType == null)
                    return;
                

                _addressablesSettingsProperty = defaultObjectType.GetProperty(
                    "Settings",
                    BindingFlags.Public | BindingFlags.Static);
                _findAssetEntryMethod =
                    settingsType.GetMethod("FindAssetEntry", new[] { typeof(string) }) ??
                    settingsType.GetMethod("FindAssetEntry", new[] { typeof(string), typeof(bool) });
                _findAssetEntryParametersCount = _findAssetEntryMethod?.GetParameters().Length ?? 0;

                _addressablesReflectionAvailable =
                    _addressablesSettingsProperty != null && _findAssetEntryMethod != null;
            }
            catch (Exception e)
            {
                _addressablesReflectionAvailable = false;
                LogAddressablesReflectionWarning("initializing Addressables reflection", e);
            }
        }

        private static void LogAddressablesReflectionWarning(string context, Exception exception)
        {
            if (_addressablesReflectionWarningLogged)
                return;

            _addressablesReflectionWarningLogged = true;
            Debug.LogWarning($"Failed to detect Addressables via reflection while {context}: {exception}");
        }
    }

    public static class BackupUtilities
    {
        public static int BackupAssets(List<AssetData> assets, string backupDirectory)
        {
            var backedUpCount = 0;

            foreach (var asset in assets)
            {
                try
                {
                    var destPath = Path.Combine(backupDirectory, asset.Path);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(asset.Path, destPath, true);

                    var metaPath = asset.Path + ".meta";
                    if (File.Exists(metaPath))
                    {
                        var destMetaPath = destPath + ".meta";
                        File.Copy(metaPath, destMetaPath, true);
                    }

                    backedUpCount++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to back up {asset.Path}: {e.Message}");
                }
            }

            return backedUpCount;
        }
    }
}