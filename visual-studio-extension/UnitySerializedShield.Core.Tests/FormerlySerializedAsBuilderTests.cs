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
    public void AddsNextFormerlySerializedAsWhenAlreadyMigratedFieldIsRenamedAgain()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "using UnityEngine.Serialization;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "\t[FormerlySerializedAs(\"StringTest\")]",
            "\t[SerializeField] private string StringTest1 = \"\";",
            "}",
            "");
        var currentText = previousText.Replace("StringTest1", "StringTest2", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"StringTest1\")]\n\t[FormerlySerializedAs(\"StringTest\")]", updatedText);
    }

    [Fact]
    public void HandlesRenameWhenAnotherSerializedFieldHasSameTypeAndNoInitializer()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "",
            "public class MyTestClass : MonoBehaviour",
            "{",
            "\t[SerializeField] private string testname;",
            "\t[SerializeField] private string description;",
            "\t[SerializeField] private int testnumber;",
            "}",
            "");
        var currentText = previousText.Replace("testname", "testname2", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"testname\")]\n\t[SerializeField] private string testname2;", updatedText);
    }

    [Fact]
    public void HandlesSmallSerializedFieldNameEdit()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "using UnityEngine.Serialization;",
            "",
            "public class TestSerialized : MonoBehaviour",
            "{",
            "\t[SerializeField] private string StringValue_1;",
            "\t[SerializeField] private int IntValue1;",
            "}",
            "");
        var currentText = previousText.Replace("StringValue_1", "StringValue1", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"StringValue_1\")]\n\t[SerializeField] private string StringValue1;", updatedText);
    }

    [Fact]
    public void HandlesRenameAfterFieldsThatAlreadyHaveFormerlySerializedAs()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "using UnityEngine.Serialization;",
            "",
            "public class TestSerialized : MonoBehaviour",
            "{",
            "\t[FormerlySerializedAs(\"StringValue_1\")]",
            "\t[SerializeField] private string StringValue_2;",
            "\t[FormerlySerializedAs(\"IntValue1\")]",
            "\t[SerializeField] private int IntValue2;",
            "\t[SerializeField] private float FloatValue;",
            "\t[SerializeField] private string StringNameTest;",
            "}",
            "");
        var currentText = previousText.Replace("FloatValue", "FloatValue1", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"FloatValue\")]\n\t[SerializeField] private float FloatValue1;", updatedText);
    }

    [Fact]
    public void HandlesUnityBackingFieldStyleRename()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "using UnityEngine.Serialization;",
            "",
            "public class TestSerialized : MonoBehaviour",
            "{",
            "\t[SerializeField] private string m_FileName;",
            "}",
            "");
        var currentText = previousText.Replace("m_FileName", "fileName", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"m_FileName\")]\n\t[SerializeField] private string fileName;", updatedText);
    }

    [Fact]
    public void HandlesUnityBackingFieldPrefixRemovalWithUppercaseName()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "using UnityEngine.Serialization;",
            "",
            "public class TestSerialized : MonoBehaviour",
            "{",
            "\t[SerializeField] private string m_PlayerName;",
            "}",
            "");
        var currentText = previousText.Replace("m_PlayerName", "PlayerName", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"m_PlayerName\")]\n\t[SerializeField] private string PlayerName;", updatedText);
    }

    [Fact]
    public void HandlesNumericSuffixRename()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "using UnityEngine.Serialization;",
            "",
            "public class TestSerialized : MonoBehaviour",
            "{",
            "\t[SerializeField] private string enemyName;",
            "\t[SerializeField] private int velocity;",
            "}",
            "");
        var currentText = previousText
            .Replace("enemyName", "enemyName_1", StringComparison.Ordinal)
            .Replace("velocity", "velocity_1", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"enemyName\")]\n\t[SerializeField] private string enemyName_1;", updatedText);
        Assert.Contains("[FormerlySerializedAs(\"velocity\")]\n\t[SerializeField] private int velocity_1;", updatedText);
    }

    [Fact]
    public void HandlesNumericSuffixNumberChange()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "using UnityEngine.Serialization;",
            "",
            "public class TestSerialized : MonoBehaviour",
            "{",
            "\t[SerializeField] private string enemyName_1;",
            "\t[SerializeField] private int velocity_1;",
            "}",
            "");
        var currentText = previousText
            .Replace("enemyName_1", "enemyName_3", StringComparison.Ordinal)
            .Replace("velocity_1", "velocity_2", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"enemyName_1\")]\n\t[SerializeField] private string enemyName_3;", updatedText);
        Assert.Contains("[FormerlySerializedAs(\"velocity_1\")]\n\t[SerializeField] private int velocity_2;", updatedText);
    }

    [Fact]
    public void HandlesNumericSuffixRenameWithUnityStylePrivatePrefix()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "using UnityEngine.Serialization;",
            "",
            "public class TestSerialized : MonoBehaviour",
            "{",
            "\t[SerializeField] private string playerName;",
            "\t[SerializeField] private int m_playerLevel;",
            "\t[SerializeField] private string m_EnemyName;",
            "}",
            "");
        var currentText = previousText
            .Replace("playerName", "playerName_2", StringComparison.Ordinal)
            .Replace("m_playerLevel", "m_playerLevel_1", StringComparison.Ordinal)
            .Replace("m_EnemyName", "enemyName", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"playerName\")]\n\t[SerializeField] private string playerName_2;", updatedText);
        Assert.Contains("[FormerlySerializedAs(\"m_playerLevel\")]\n\t[SerializeField] private int m_playerLevel_1;", updatedText);
        Assert.Contains("[FormerlySerializedAs(\"m_EnemyName\")]\n\t[SerializeField] private string enemyName;", updatedText);
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

    [Fact]
    public void HandlesWindowsLineEndings()
    {
        var previousText = string.Join("\r\n",
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "\t[SerializeField] private float health = 100f;",
            "}",
            "");
        var currentText = previousText.Replace("health", "playerHealth", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("using UnityEngine.Serialization;\r\n", updatedText);
        Assert.Contains("[FormerlySerializedAs(\"health\")]\r\n\t[SerializeField] private float playerHealth = 100f;", updatedText);
    }

    [Fact]
    public void HandlesAtPrefixedFieldNames()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "",
            "public class DataModel : MonoBehaviour",
            "{",
            "\t[SerializeField] private string @class = \"\";",
            "}",
            "");
        var currentText = previousText.Replace("@class", "@category", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        // The serialized name strips the @ prefix.
        Assert.Contains("[FormerlySerializedAs(\"class\")]", updatedText);
    }

    [Fact]
    public void HandlesGenericTypeFields()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "using System.Collections.Generic;",
            "",
            "public class Inventory : MonoBehaviour",
            "{",
            "\t[SerializeField] private List<Dictionary<string, int>> itemCounts = new();",
            "}",
            "");
        var currentText = previousText.Replace("itemCounts", "slotCounts", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"itemCounts\")]", updatedText);
        Assert.Contains("List<Dictionary<string, int>> slotCounts", updatedText);
    }

    [Fact]
    public void HandlesVerbatimStringInitializerWithSlashes()
    {
        var previousText = string.Join('\n',
            "using UnityEngine;",
            "",
            "public class Config : MonoBehaviour",
            "{",
            "\t[SerializeField] private string configPath = @\"Assets//Data//config.json\";",
            "}",
            "");
        var currentText = previousText.Replace("configPath", "dataPath", StringComparison.Ordinal);
        var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(
            currentText,
            FormerlySerializedAsBuilder.Build(previousText, currentText));

        Assert.Contains("[FormerlySerializedAs(\"configPath\")]", updatedText);
    }
}
