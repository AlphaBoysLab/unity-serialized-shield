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
        public List<SerializedShieldFieldMigration> FieldMigrations = new List<SerializedShieldFieldMigration>();
        public List<string> Warnings = new List<string>();
    }

    [Serializable]
    public sealed class SerializedShieldFieldMigration
    {
        public string CurrentName;
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
        public int TextMigratedAssetCount;
        public int TextMigratedFieldCount;
        public List<string> TargetAssetPaths = new List<string>();

        /// <summary>True when the migration stopped before changing any serialized file on disk.</summary>
        public bool Aborted;
        public string AbortReason = string.Empty;

        /// <summary>True when attribute removal was requested but refused (coverage or verification failure).</summary>
        public bool AttributeRemovalSkipped;
        public string AttributeRemovalSkipReason = string.Empty;

        public List<string> Warnings = new List<string>();

        /// <summary>Asset paths that could not be read or written during this migration.</summary>
        public List<string> FailedAssetPaths = new List<string>();

        /// <summary>Former names whose attributes were verified as fully migrated and removed.</summary>
        public List<string> RemovedAttributeNames = new List<string>();

        /// <summary>Former names whose attributes were kept because references to them may still exist.</summary>
        public List<string> KeptAttributeNames = new List<string>();
    }

    [Serializable]
    public sealed class SerializedShieldAssetScanResult
    {
        public bool Cancelled;
        public List<string> TargetAssetPaths = new List<string>();
        public List<string> UnreadableAssetPaths = new List<string>();
    }

    [Serializable]
    public sealed class SerializedShieldDryRunResult
    {
        public bool Cancelled;
        public int TotalRenameCount;
        public List<string> Lines = new List<string>();
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

        /// <summary>
        /// Backup file name relative to the session folder. Sessions created before 2.0.0
        /// stored an absolute path here; SerializedShieldMigrationBackup resolves both forms.
        /// </summary>
        public string BackupPath;
    }
}
