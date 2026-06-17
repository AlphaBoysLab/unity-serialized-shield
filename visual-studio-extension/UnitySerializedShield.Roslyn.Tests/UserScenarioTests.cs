using UnitySerializedShield.Roslyn;

namespace UnitySerializedShield.Roslyn.Tests;

// Reproduces the exact field layout from the user's AbacusGameController to verify
// a prefix-extension rename (testValue -> testValue11) is detected and migrated
// even when other fields already carry [FormerlySerializedAs].
public class UserScenarioTests
{
    private static string File(string testValueName) => string.Join("\r\n",
        "using UnityEngine;",
        "using UnityEngine.Serialization;",
        "",
        "namespace WonderAnanna.AbacusMania.GamePlay",
        "{",
        "    public class AbacusGameController : MonoBehaviour, IAbacusGameResult",
        "    {",
        $"        [SerializeField] private int {testValueName} = 999;",
        "        public Camera MainCamera;",
        "        [FormerlySerializedAs(\"maxDistance\")]",
        "        [SerializeField] private float maxDistance1 = 100f;",
        "        [FormerlySerializedAs(\"hitLayers\")]",
        "        [SerializeField] private LayerMask hitLayers1;",
        "    }",
        "}") + "\r\n";

    [Fact]
    public void DetectsPrefixExtensionRenameAmongFieldsWithExistingAttributes()
    {
        var previous = File("testValue");
        var current = File("testValue11");

        var renames = SerializedFieldMigrator.FindRenames(previous, current);

        var rename = Assert.Single(renames);
        Assert.Equal("testValue", rename.PreviousName);
        Assert.Equal("testValue11", rename.CurrentName);
    }

    [Fact]
    public void RenameIsRecognizedAsCleanSubstitution()
    {
        var previous = File("testValue");
        var current = File("testValue11");
        var renames = SerializedFieldMigrator.FindRenames(previous, current);

        Assert.True(IdentifierRenameRecognizer.IsRenameShaped(previous, current, renames));
    }

    [Fact]
    public void MigrateAddsFormerlySerializedAsForTestValue()
    {
        var previous = File("testValue");
        var current = File("testValue11");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"testValue\")]", result);
        Assert.Contains("private int testValue11 = 999;", result);
    }
}
