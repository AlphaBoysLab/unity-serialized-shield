using UnitySerializedShield.Roslyn;

namespace UnitySerializedShield.Roslyn.Tests;

public class SerializedFieldMigratorTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    [Fact]
    public void AddsFormerlySerializedAsAndUsingWhenSerializeFieldIsRenamed()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    [SerializeField] private float maxDistance = 100f;",
            "}");
        var current = previous.Replace("maxDistance", "attackDistance");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("using UnityEngine.Serialization;", result);
        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]", result);
        Assert.Contains("private float attackDistance = 100f;", result);
    }

    [Fact]
    public void PreservesIndentationOfRenamedField()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    [SerializeField] private float maxDistance = 100f;",
            "}");
        var current = previous.Replace("maxDistance", "attackDistance");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains(
            "    [FormerlySerializedAs(\"maxDistance\")]\n    [SerializeField] private float attackDistance = 100f;",
            result);
    }

    [Fact]
    public void ProtectsPublicFieldInMonoBehaviourWithoutSerializeFieldAttribute()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    public float speed = 5f;",
            "}");
        var current = previous.Replace("speed", "moveSpeed");

        var renames = SerializedFieldMigrator.FindRenames(previous, current);

        var rename = Assert.Single(renames);
        Assert.Equal("speed", rename.PreviousName);
        Assert.Equal("moveSpeed", rename.CurrentName);
    }

    [Fact]
    public void IgnoresPublicFieldInPlainNonUnityClass()
    {
        var previous = Lines(
            "public class PlainData",
            "{",
            "    public float speed = 5f;",
            "}");
        var current = previous.Replace("speed", "moveSpeed");

        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
    }

    [Theory]
    [InlineData("static")]
    [InlineData("const")]
    [InlineData("readonly")]
    public void IgnoresStaticConstAndReadonlyFields(string modifier)
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Config : MonoBehaviour",
            "{",
            $"    [SerializeField] private {modifier} float maxDistance = 100f;",
            "}");
        var current = previous.Replace("maxDistance", "attackDistance");

        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
    }

    [Fact]
    public void IgnoresNonSerializedField()
    {
        var previous = Lines(
            "using System;",
            "using UnityEngine;",
            "",
            "public class Config : MonoBehaviour",
            "{",
            "    [NonSerialized] public float maxDistance = 100f;",
            "}");
        var current = previous.Replace("maxDistance", "attackDistance");

        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
    }

    [Fact]
    public void IgnoresMultiDeclaratorField()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Config : MonoBehaviour",
            "{",
            "    [SerializeField] private int a, b;",
            "}");
        var current = previous.Replace("int a, b;", "int x, b;");

        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
    }

    [Fact]
    public void DoesNotDuplicateExistingFormerlySerializedAs()
    {
        var previous = Lines(
            "using UnityEngine;",
            "using UnityEngine.Serialization;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    [FormerlySerializedAs(\"maxDistance\")]",
            "    [SerializeField] private float attackDistance = 100f;",
            "}");
        // Rename attackDistance -> detectionRange; the maxDistance attribute must stay,
        // and a single new attribute for attackDistance must be added.
        var current = previous.Replace("attackDistance", "detectionRange");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]", result);
        Assert.Contains("[FormerlySerializedAs(\"attackDistance\")]", result);
        Assert.Equal(1, CountOccurrences(result, "[FormerlySerializedAs(\"attackDistance\")]"));
    }

    [Fact]
    public void IsIdempotentWhenAttributeAlreadyMatchesRename()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    [SerializeField] private float maxDistance = 100f;",
            "}");
        var current = previous.Replace("maxDistance", "attackDistance");

        var once = SerializedFieldMigrator.Migrate(previous, current);
        // Running the migrator again against the same baseline must not add a second attribute.
        var twice = SerializedFieldMigrator.Migrate(previous, once);

        Assert.Equal(1, CountOccurrences(twice, "[FormerlySerializedAs(\"maxDistance\")]"));
    }

    [Fact]
    public void PreservesCrlfLineEndings()
    {
        var previous = string.Join("\r\n",
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    [SerializeField] private float maxDistance = 100f;",
            "}") + "\r\n";
        var current = previous.Replace("maxDistance", "attackDistance");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]\r\n", result);
        Assert.DoesNotContain("[FormerlySerializedAs(\"maxDistance\")]\n\r", result);
    }

    [Fact]
    public void ReturnsUnchangedSourceWhenNoSerializedRename()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    private float localOnly = 1f;",
            "}");
        var current = previous.Replace("localOnly", "renamedLocal");

        Assert.Equal(current, SerializedFieldMigrator.Migrate(previous, current));
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(token, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
