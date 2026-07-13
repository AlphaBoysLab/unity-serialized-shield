using UnitySerializedShield.Roslyn;

namespace UnitySerializedShield.Roslyn.Tests;

// V-C7: Unity serializes the compiler-generated backing field of
// [field: SerializeField] auto-properties as <Name>k__BackingField, so renaming
// the property must add [field: FormerlySerializedAs("<Old>k__BackingField")].
public class AutoPropertyMigrationTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    [Fact]
    public void MigratesFieldTargetedSerializeFieldAutoProperty()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [field: SerializeField] public float Health { get; private set; } = 10f;",
            "}");
        var current = previous.Replace("Health", "MaxHealth");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[field: FormerlySerializedAs(\"<Health>k__BackingField\")]", result);
        Assert.Contains("public float MaxHealth { get; private set; } = 10f;", result);
        Assert.Contains("using UnityEngine.Serialization;", result);
    }

    [Fact]
    public void ReportsPropertyRenameWithPropertyNames()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [field: SerializeField] public float Health { get; set; }",
            "}");
        var current = previous.Replace("Health", "MaxHealth");

        var renames = SerializedFieldMigrator.FindRenames(previous, current);

        var rename = Assert.Single(renames);
        Assert.Equal("Health", rename.PreviousName);
        Assert.Equal("MaxHealth", rename.CurrentName);
    }

    [Fact]
    public void IsIdempotentForAutoPropertyMigration()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [field: SerializeField] public float Health { get; set; }",
            "}");
        var current = previous.Replace("Health", "MaxHealth");

        var once = SerializedFieldMigrator.Migrate(previous, current);
        var twice = SerializedFieldMigrator.Migrate(previous, once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void IgnoresPropertyWithBodiedAccessors()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    private float health;",
            "    [field: SerializeField] public float Health { get => health; set => health = value; }",
            "}");
        var current = previous.Replace("public float Health", "public float MaxHealth");

        // Not an auto-property: no backing field is serialized.
        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
    }

    [Fact]
    public void IgnoresAutoPropertyWithoutFieldTargetedSerializeField()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    public float Health { get; set; }",
            "}");
        var current = previous.Replace("Health", "MaxHealth");

        // Unity does not serialize plain auto-properties.
        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
    }

    [Fact]
    public void IgnoresStaticFieldTargetedAutoProperty()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [field: SerializeField] public static float Health { get; set; }",
            "}");
        var current = previous.Replace("Health", "MaxHealth");

        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
    }
}
