using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MonoScriptGuidReplacer.Editor
{
    public class MapApplier : EditorWindow
    {
        private static string _oldMapPath = "BROWSE_TO_SET/old_guid_map.json";
        private static string _newMapPath = "BROWSE_TO_SET/new_guid_map.json";
        private static List<string> _replacedFileNames;

        [MenuItem("Assets/MonoScript GUID Replacer/Map Applier (GUID Replacer)", false, 0)]
        public static void OpenWindow()
        {
            var w = GetWindow<MapApplier>();
            w.titleContent = new GUIContent("Map Applier");
            w.minSize = new Vector2(600, 140);
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label("Select your JSON maps", style);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Old GUID Map (JSON)");
            _oldMapPath = EditorGUILayout.TextField(_oldMapPath);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                string path = EditorUtility.OpenFilePanel
                (
                    "Please select the old map JSON file",
                    Path.GetFileName(_oldMapPath),
                    "json"
                );
                if (!string.IsNullOrEmpty(path))
                    _oldMapPath = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("New GUID Map (JSON)");
            _newMapPath = EditorGUILayout.TextField(_newMapPath);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                string path = EditorUtility.OpenFilePanel
                (
                    "Please select the new map JSON file",
                    Path.GetFileName(_newMapPath),
                    "json"
                );
                if (!string.IsNullOrEmpty(path))
                    _newMapPath = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10f);

            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.yellow;
            EditorGUILayout.LabelField("Select the folders containing the prefabs or scenes to replace", style, GUILayout.ExpandWidth(true));

            GUILayout.FlexibleSpace();

            Color bc = GUI.backgroundColor;
            GUI.backgroundColor = new Color32(186, 255, 80, 255);
            if (GUILayout.Button("Apply Map to Selected Assets", GUILayout.Height(30)))
            {
                ApplyMap(_oldMapPath, _newMapPath);
            }
            GUI.backgroundColor = bc;
        }

        public static void ApplyMap(string oldMapPath, string newMapPath)
        {
            string oldFullPath = oldMapPath;
            string newFullPath = newMapPath;

            if (!File.Exists(oldFullPath) || !File.Exists(newFullPath))
            {
                EditorUtility.DisplayDialog
                (
                    "Error",
                    $"Can't find one of the map files:\n{oldFullPath}\nor\n{newFullPath}",
                    "OK"
                );
                return;
            }

            // 反序列化
            string json = File.ReadAllText(oldFullPath);
            var oldWrapper = JsonUtility.FromJson<Wrapper>(json);
            json = File.ReadAllText(newFullPath);
            var newWrapper = JsonUtility.FromJson<Wrapper>(json);

            // 開始處理
            ApplyMap(oldWrapper, newWrapper);
        }

        public static void ApplyMap(Wrapper oldWrapper, Wrapper newWrapper)
        {
            if (oldWrapper == null || newWrapper == null)
                return;

            int total = 0;
            _replacedFileNames = new List<string>();

            // 1. 取出所有選中的 GUID -> path
            var allSelectedPaths = Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();

            // 2. 分出選中的文件夾和選中的非文件夾
            var folderPaths = allSelectedPaths
                .Where(path => AssetDatabase.IsValidFolder(path))
                .ToArray();

            var directAssetPaths = allSelectedPaths
                .Where(path =>
                    !AssetDatabase.IsValidFolder(path) &&
                    (path.EndsWith(".prefab") || path.EndsWith(".unity"))
                )
                .ToArray();

            // 如果都沒有選到 -> 報錯
            if (folderPaths.Length == 0 && directAssetPaths.Length == 0)
            {
                EditorUtility.DisplayDialog
                (
                    "Error",
                    "Please select one or more folders, prefabs or scenes in the Project view before running this command.",
                    "OK"
                );
                return;
            }

            // 3. 從選中的文件夾裡過濾類型
            var foundInFolders = new List<string>();
            if (folderPaths.Length > 0)
            {
                var guidsInFolders = AssetDatabase.FindAssets("t:Prefab t:Scene", folderPaths);
                foundInFolders = guidsInFolders
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .ToList();
            }

            // 4. 合併選擇路徑
            var targets = foundInFolders
                .Concat(directAssetPaths)
                .Distinct();

            // 5. 處理每一個文件
            foreach (var path in targets)
            {
                total += _ProcessTextFile(path, oldWrapper.items, newWrapper.items);
            }

            AssetDatabase.Refresh();

            Debug.Log($"🚀 Total replaced {total} m_Script references");
            var strBuilder = new StringBuilder();
            foreach (var replaceFileName in _replacedFileNames)
                strBuilder.Append($"{replaceFileName}\n");
            if (strBuilder.Length > 0)
                Debug.Log(strBuilder.ToString());
        }

        private static int _ProcessTextFile(string assetPath, ScriptMapEntry[] oldMaps, ScriptMapEntry[] newMaps)
        {
            string fullPath = Path.Combine(Application.dataPath, "..", assetPath);
            string text = File.ReadAllText(fullPath);
            int replaced = 0;

            var replaceList = new List<Replacer>();

            foreach (var oe in oldMaps)
            {
                // 格式 -> m_Script: {fileID: 11500000, guid: abcd1234efgh5678ijkl9101, type: 3}
                string pattern = $@"m_Script:\s*{{\s*fileID:\s*{oe.fileID}\s*,\s*guid:\s*{oe.guid}\s*,";

                // 找到 text 中有匹配的
                if (Regex.IsMatch(text, pattern))
                {
                    foreach (var ne in newMaps)
                    {
                        // 搜尋是否有對應的 fullName, 才會加入替換清單中
                        if (oe.fullName.IndexOf(ne.fullName) != -1 && oe.fullName.Equals(ne.fullName))
                        {
                            var r = new Replacer();
                            r.pattern = pattern;
                            r.replace = $"m_Script: {{fileID: {ne.fileID}, guid: {ne.guid},";
                            replaceList.Add(r);
                        }
                    }
                }

                // 用 Regex 替換 m_Script 字段
                foreach (var r in replaceList)
                {
                    var newText = Regex.Replace(text, r.pattern, r.replace);
                    if (newText != text)
                    {
                        text = newText;
                        replaced++;
                    }
                }
            }

            if (replaced > 0)
            {
                File.WriteAllText(fullPath, text);
                _replacedFileNames.Add(Path.GetFileName(fullPath));
            }

            return replaced;
        }
    }
}