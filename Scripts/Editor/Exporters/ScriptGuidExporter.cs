using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MonoScriptGuidReplacer.Editor
{
    public class ScriptGuidExporter
    {
        [MenuItem("Assets/MonoScript GUID Replacer/Export MonoScript GUID to JSON Map (Select the source code folders or files)", false, 20)]
        public static void Export()
        {
            // 1. 取出所有選中的 GUID -> path
            var allSelectedPaths = Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();

            // 2. 選中文件夾類型
            var folderPaths = allSelectedPaths
                .Where(path => AssetDatabase.IsValidFolder(path))
                .ToArray();

            // 3. 選中非文件夾類型
            var directAssetPaths = allSelectedPaths
                .Where(path =>
                    !AssetDatabase.IsValidFolder(path) &&
                    // 支持文件類型
                    (path.EndsWith(".cs"))
                )
                .ToArray();

            // 如果都沒有選到 -> 報錯
            if (folderPaths.Length == 0 && directAssetPaths.Length == 0)
            {
                EditorUtility.DisplayDialog
                (
                    "Error",
                    "Please select one or more folders, script (.cs) in the Project view before running this command.",
                    "OK"
                );
                return;
            }

            // 4. 從選中的文件夾裡過濾類型
            var foundInFolders = new List<string>();
            if (folderPaths.Length > 0)
            {
                // 支持文件類型
                var guidsInFolders = AssetDatabase.FindAssets("t:MonoScript t:Script", folderPaths);
                foundInFolders = guidsInFolders
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .ToList();
            }

            // 5. 合併選擇路徑
            var targets = foundInFolders
                .Concat(directAssetPaths)
                .Distinct();

            // 6. 處理每一個文件
            var list = targets
                .Select(path =>
                {
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    var type = ms.GetClass();

                    // 過濾類別
                    if (type == null ||
                        // 如果不是 MonoBehaviour 也不是 ScriptableObject 就過濾掉
                        !(typeof(MonoBehaviour).IsAssignableFrom(type) || typeof(ScriptableObject).IsAssignableFrom(type)))
                        return null;

                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ms, out string assetGuid, out long fileId);

                    return new ScriptMapEntry
                    {
                        fullName = type.FullName,
                        guid = assetGuid,
                        fileID = fileId
                    };
                })
                .Where(info => info != null)
                .ToArray();

            // 7. 寫入 JSON
            var wrapper = new Wrapper { items = list };
            string json = JsonUtility.ToJson(wrapper, true);

            // 8. 讓使用者在專案內選擇儲存位置和檔名
            string defaultFileName = "mono_source_code_guid_map.json";
            string savePath = EditorUtility.SaveFilePanelInProject
            (
                "Save Mono Source Code Mapping File",
                $"{defaultFileName}",
                "json",
                "Please select the location and filename to save the JSON file"
            );

            // 使用者按了 Cancel 會拿到空字串
            if (string.IsNullOrEmpty(savePath))
            {
                Debug.LogWarning("Save canceled.");
                return;
            }

            // 9. 寫入並更新 AssetDatabase
            File.WriteAllText(savePath, json);
            AssetDatabase.Refresh();

            Debug.Log($"Export complete: {list.Length} script entries written to {savePath}");
        }
    }
}