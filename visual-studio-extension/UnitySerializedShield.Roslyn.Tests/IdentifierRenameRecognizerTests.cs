using UnitySerializedShield.Roslyn;

namespace UnitySerializedShield.Roslyn.Tests;

public class IdentifierRenameRecognizerTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static string Source(string fieldName, string initializer) => Lines(
        "using UnityEngine;",
        "",
        "public class EnemySensor : MonoBehaviour",
        "{",
        $"    [SerializeField] private float {fieldName} = {initializer};",
        $"    private void Awake() {{ var x = {fieldName}; }}",
        "}");

    [Fact]
    public void RecognizesPureIdentifierSubstitutionAcrossAllOccurrences()
    {
        var previous = Source("maxDistance", "100f");
        var current = Source("attackDistance", "100f");
        var renames = SerializedFieldMigrator.FindRenames(previous, current);

        Assert.True(IdentifierRenameRecognizer.IsRenameShaped(previous, current, renames));
    }

    [Fact]
    public void RejectsEditThatAlsoChangesUnrelatedText()
    {
        // Field renamed AND the initializer changed in the same edit — not a clean
        // rename, so we must not treat it as one.
        var previous = Source("maxDistance", "100f");
        var current = Source("attackDistance", "250f");
        var renames = SerializedFieldMigrator.FindRenames(previous, current);

        Assert.False(IdentifierRenameRecognizer.IsRenameShaped(previous, current, renames));
    }

    [Fact]
    public void DoesNotMatchPartialIdentifierOccurrences()
    {
        // "max" must not be substituted inside "maxDistance"; only the whole token.
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class C : MonoBehaviour",
            "{",
            "    [SerializeField] private int max = 1;",
            "    [SerializeField] private int maxDistance = 2;",
            "}");
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class C : MonoBehaviour",
            "{",
            "    [SerializeField] private int limit = 1;",
            "    [SerializeField] private int maxDistance = 2;",
            "}");
        var renames = SerializedFieldMigrator.FindRenames(previous, current);

        Assert.True(IdentifierRenameRecognizer.IsRenameShaped(previous, current, renames));
    }

    [Fact]
    public void ReturnsFalseWhenNoRenames()
    {
        var text = Source("maxDistance", "100f");

        Assert.False(IdentifierRenameRecognizer.IsRenameShaped(text, text, System.Array.Empty<RenamedSerializedField>()));
    }
}
