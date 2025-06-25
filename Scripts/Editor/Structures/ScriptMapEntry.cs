using System;

namespace MonoScriptGuidReplacer.Editor
{
    [Serializable]
    public class ScriptMapEntry
    {
        public string fullName;
        public string guid;
        public long fileID;
    }

    [Serializable]
    public class Wrapper
    {
        public ScriptMapEntry[] items;
    }
}