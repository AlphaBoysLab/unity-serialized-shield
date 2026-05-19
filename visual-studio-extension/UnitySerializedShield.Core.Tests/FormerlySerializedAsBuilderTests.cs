using UnitySerializedShield.Core;

namespace UnitySerializedShield.Core.Tests;

public class FormerlySerializedAsBuilderTests
{
    [Fact]
    public void AddsFormerlySerializedAsWhenSerializedFieldVariableIsRenamed()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "\t[SerializeField] private float maxDistance = 100f;",
            "}",
            "");
        var currentText = previousText.Replace("maxDistance", "attackDistance", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("using UnityEngine.Serialization;", updatedText);
        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]\n\t[SerializeField] private float attackDistance = 100f;", updatedText);
    }

    [Fact]
    public void ReportsRenamedSerializedFieldNames()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "\t[SerializeField] private float maxDistance = 100f;",
            "}",
            "");
        var currentText = previousText.Replace("maxDistance", "attackDistance", StringComparison.Ordinal);

        var rename = Assert.Single(FormerlySerializedAsBuilder.FindRenamedSerializedFields(previousText, currentText));

        Assert.Equal("maxDistance", rename.PreviousName);
        Assert.Equal("attackDistance", rename.CurrentName);
    }

    [Fact]
    public void DoesNotAddDuplicateFormerlySerializedAsAttribute()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "using UnityEngine.Serialization;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "\t[SerializeField] private float maxDistance = 100f;",
            "}",
            "");
        var currentText = previousText.Replace(
            "\t[SerializeField] private float maxDistance = 100f;",
            "\t[FormerlySerializedAs(\"maxDistance\")]\n\t[SerializeField] private float attackDistance = 100f;",
            StringComparison.Ordinal);

        Assert.Empty(FormerlySerializedAsBuilder.Build(previousText, currentText));
    }

    [Fact]
    public void IgnoresNonSerializedVariables()
    {
        var previousText = string.Join('\n',
            "public class PlainClass",
            "{",
            "\tprivate float maxDistance = 100f;",
            "}",
            "");
        var currentText = previousText.Replace("maxDistance", "attackDistance", StringComparison.Ordinal);

        Assert.Empty(FormerlySerializedAsBuilder.Build(previousText, currentText));
    }

    [Fact]
    public void HandlesAttributeAboveField()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "\t[SerializeField]",
            "\tprivate float maxDistance = 100f;",
            "}",
            "");
        var currentText = previousText.Replace("maxDistance", "attackDistance", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]\n\t[SerializeField]", updatedText);
    }

    [Fact]
    public void HandlesMultipleAttributesAboveField()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "\t[Header(\"Movement\")]",
            "\t[SerializeField]",
            "\tprivate float maxDistance = 100f;",
            "}",
            "");
        var currentText = previousText.Replace("maxDistance", "attackDistance", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]\n\t[Header(\"Movement\")]", updatedText);
    }

    [Fact]
    public void SkipsStaticConstAndMultiFieldDeclarations()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "\t[SerializeField] private static float maxDistance = 100f;",
            "\t[SerializeField] private const int maxCount = 5;",
            "\t[SerializeField] private int firstValue, secondValue;",
            "}",
            "");
        var currentText = previousText
            .Replace("maxDistance", "attackDistance", StringComparison.Ordinal)
            .Replace("maxCount", "attackCount", StringComparison.Ordinal)
            .Replace("firstValue", "renamedFirstValue", StringComparison.Ordinal);

        Assert.Empty(FormerlySerializedAsBuilder.Build(previousText, currentText));
    }
}
