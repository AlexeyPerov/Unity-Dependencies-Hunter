using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DependenciesHunter
{
    public class ProjectAssetsAnalysisHelper
    {
        private List<string> _iconPaths;

        public bool IsValidAssetType(string path)
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

            if (type == typeof(Texture2D) && UsedAsProjectIcon(path))
            {
                return false;
            }
            
            return true;
        }
        
        public bool IsValidForOutput(string path, List<string> ignoreInOutputPatterns)
        {
            foreach (var pattern in ignoreInOutputPatterns)
            {
                if (!string.IsNullOrEmpty(pattern) && Regex.Match(path, pattern).Success)
                {
                    return false;
                }
            }
            
            return true;
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
}