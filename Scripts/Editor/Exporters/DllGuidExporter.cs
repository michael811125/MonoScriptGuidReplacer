using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MonoScriptGuidReplacer.Editor
{
    public class DllGuidExporter
    {
        [MenuItem("Assets/MonoScript GUID Replacer/Export DLL GUID to JSON Map (Select the DLL file)", false, 20)]
        public static void Export()
        {
            // 1. 選擇 Dll 物件
            UnityEngine.Object selectedObject = Selection.activeObject;
            string dllAssetPath = AssetDatabase.GetAssetPath(selectedObject);
            string dllFullPath = Path.Combine(Application.dataPath, "../" + dllAssetPath);
            string extensionSuffix = ".dll";

            if (!File.Exists(dllFullPath))
            {
                Debug.LogError($"DLL not found: {dllFullPath}");
                return;
            }

            string extensionCheck = Path.GetExtension(dllFullPath);
            if (!string.Equals(extensionCheck, extensionSuffix, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"Please select a .dll file.");
                return;
            }

            // 2. 加載 dll
            Assembly asm = Assembly.LoadFrom(dllFullPath);
            // 3. 找到所有 MonoBehaviour 或 ScriptableObject 子類
            var mbTypes = asm.GetTypes()
               .Where(t => !t.IsAbstract && (typeof(MonoBehaviour).IsAssignableFrom(t) || typeof(ScriptableObject).IsAssignableFrom(t)))
               .ToArray();

            // 4. 構建映射表
            var list = mbTypes.Select(t =>
            {
                string guid = "";
                long fileID = 0;

                // 用一個臨時實例獲取 MonoScript 資源
                // 嘗試通過反射訪問類型並獲取 MonoScript
                if (typeof(MonoBehaviour).IsAssignableFrom(t))
                {
                    // 用臨時 GameObject + Component 取得 MonoScript
                    var go = new GameObject("Dummy");
                    var mb = go.AddComponent(t) as MonoBehaviour;
                    var mono = MonoScript.FromMonoBehaviour(mb);
                    UnityEngine.Object.DestroyImmediate(go);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mono, out guid, out fileID);
                }
                // ScriptableObject
                else
                {
                    // 用 CreateInstance 取得 MonoScript
                    var so = ScriptableObject.CreateInstance(t);
                    var mono = MonoScript.FromScriptableObject(so);
                    UnityEngine.Object.DestroyImmediate(so);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mono, out guid, out fileID);
                }

                return new ScriptMapEntry
                {
                    fullName = t.FullName,
                    guid = guid,
                    fileID = fileID
                };
            }).ToArray();

            // 5. 寫入 JSON
            string json = JsonUtility.ToJson(new Wrapper { items = list }, true);
            string fileName = Path.GetFileNameWithoutExtension(dllFullPath);

            // 6. 讓使用者在專案內選擇儲存位置和檔名
            string defaultFileNameSuffix = "_dll_guid_map.json";
            string savePath = EditorUtility.SaveFilePanelInProject
            (
                "Save DLL Mapping File",
                $"{fileName.ToLower()}{defaultFileNameSuffix}",
                "json",
                "Please select the location and filename to save the JSON file"
            );

            // 使用者按了 Cancel 會拿到空字串
            if (string.IsNullOrEmpty(savePath))
            {
                Debug.LogWarning("Save canceled.");
                return;
            }

            // 7. 寫入並更新 AssetDatabase
            File.WriteAllText(savePath, json);
            AssetDatabase.Refresh();

            Debug.Log($"Export complete: {list.Length} script entries written to {savePath}");
        }
    }
}