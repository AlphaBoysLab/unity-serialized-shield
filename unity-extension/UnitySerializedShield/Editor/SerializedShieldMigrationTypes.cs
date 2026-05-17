using System;
using System.Collections.Generic;

namespace AlphaBoysLab.SerializedShield.Editor
{
    [Serializable]
    public sealed class SerializedShieldScriptInfo
    {
        public string ScriptPath;
        public string ScriptGuid;
        public int AttributeCount;
        public List<string> FormerNames = new List<string>();
    }

    [Serializable]
    public sealed class SerializedShieldMigrationOptions
    {
        public bool IncludePrefabs = true;
        public bool IncludeScenes = true;
        public bool IncludeAssetFiles = true;
        public bool RemoveAttributesAfterMigration = true;
        public bool CreateBackup = true;
    }

    [Serializable]
    public sealed class SerializedShieldMigrationResult
    {
        public string ScriptPath;
        public string BackupSessionPath;
        public int ReserializedAssetCount;
        public int RemovedAttributeCount;
        public List<string> TargetAssetPaths = new List<string>();
    }

    [Serializable]
    public sealed class SerializedShieldBackupSession
    {
        public string Id;
        public string CreatedAt;
        public string SessionFilePath;
        public List<SerializedShieldBackupEntry> Files = new List<SerializedShieldBackupEntry>();
    }

    [Serializable]
    public sealed class SerializedShieldBackupEntry
    {
        public string AssetPath;
        public string BackupPath;
    }
}
