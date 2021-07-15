using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

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
                _service = new SelectedAssetsAnalysisHelper();
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
                EditorGUILayout.Separator();
                
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
}