using UnitySerializedShield.Roslyn;

namespace UnitySerializedShield.Roslyn.Tests;

// Pins the fixes from the 2026-07-12 audit: the pure gating decision
// (SerializedFieldMigrator.Migrate) must migrate genuine renames even when the
// old name appears in comments/strings, and must NEVER migrate delete+add,
// reorder/swap, or mixed edits.
public class AuditRegressionTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    // V-C1: the old name in a comment or string must not disable protection.
    [Fact]
    public void MigratesWhenOldNameAppearsInComment()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    // maxDistance is measured in meters",
            "    [SerializeField] private float maxDistance = 100f;",
            "    private void Awake() { maxDistance += 1f; }",
            "}");
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    // maxDistance is measured in meters",
            "    [SerializeField] private float attackDistance = 100f;",
            "    private void Awake() { attackDistance += 1f; }",
            "}");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]", result);
        Assert.Contains("// maxDistance is measured in meters", result);
    }

    [Fact]
    public void MigratesWhenOldNameAppearsInStringLiteral()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    [SerializeField] private float maxDistance = 100f;",
            "    private void Awake() { Debug.Log(\"maxDistance changed\"); }",
            "}");
        var current = previous.Replace("float maxDistance", "float attackDistance");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]", result);
        Assert.Contains("Debug.Log(\"maxDistance changed\");", result);
    }

    [Fact]
    public void MigratesWhenOldNameAppearsInDocComment()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    /// <summary>maxDistance in meters.</summary>",
            "    [SerializeField] private float maxDistance = 100f;",
            "}");
        var current = previous.Replace("float maxDistance", "float attackDistance");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]", result);
    }

    // V-C2: Unity DOES serialize [SerializeField, HideInInspector] fields.
    [Fact]
    public void ProtectsHideInInspectorSerializedField()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Config : MonoBehaviour",
            "{",
            "    [SerializeField, HideInInspector] private float hidden = 1f;",
            "}");
        var current = previous.Replace("hidden", "concealed");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"hidden\")]", result);
    }

    // V-C3: deleting a field and adding a same-shaped one is not a rename.
    [Fact]
    public void DoesNotMigrateDeletePlusAdd()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private int health = 10;",
            "    [SerializeField] private float speed = 5f;",
            "}");
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private int armor = 25;",
            "    [SerializeField] private float speed = 5f;",
            "}");

        // health was deleted and armor added (different initializer): pouring
        // health's serialized data into armor would corrupt it.
        Assert.Equal(current, SerializedFieldMigrator.Migrate(previous, current));
    }

    // V-C4: swapping/reordering same-typed fields must not cross-wire attributes.
    [Fact]
    public void DoesNotMigrateSwappedFields()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private int a = 1;",
            "    [SerializeField] private int b = 2;",
            "}");
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private int b = 2;",
            "    [SerializeField] private int a = 1;",
            "}");

        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
        Assert.Equal(current, SerializedFieldMigrator.Migrate(previous, current));
    }

    [Fact]
    public void DoesNotMigrateReorderedFields()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private float first = 1f;",
            "    [SerializeField] private float second = 2f;",
            "    [SerializeField] private float third = 3f;",
            "}");
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private float third = 3f;",
            "    [SerializeField] private float first = 1f;",
            "    [SerializeField] private float second = 2f;",
            "}");

        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
    }

    // Case-only renames change the serialized name for Unity and need migration.
    [Fact]
    public void MigratesCaseOnlyRename()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private float speed = 5f;",
            "}");
        var current = previous.Replace("speed", "Speed");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"speed\")]", result);
        Assert.Contains("private float Speed = 5f;", result);
    }

    // V-C6: verbatim identifiers (@name) — Unity serializes the name without '@'.
    [Fact]
    public void MigratesVerbatimIdentifierRename()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private int @class = 1;",
            "    private void Awake() { @class += 1; }",
            "}");
        var current = previous.Replace("@class", "@version");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"class\")]", result);
        Assert.Contains("private int @version = 1;", result);
    }

    [Fact]
    public void MigratesRenameFromPlainToVerbatimIdentifier()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private int order = 1;",
            "}");
        var current = previous.Replace("int order", "int @event");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"order\")]", result);
    }

    // Partial classes: the declaration part being edited is matched on its own.
    [Fact]
    public void MigratesRenameInPartialClassPartWithoutBaseList()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public partial class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private int hp = 1;",
            "}",
            "",
            "public partial class Player",
            "{",
            "    [SerializeField] private float speed = 2f;",
            "}");
        var current = previous.Replace("speed", "moveSpeed");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"speed\")]", result);
    }

    // Nested serializable types participate in Unity serialization.
    [Fact]
    public void MigratesRenameInNestedSerializableClass()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Outer : MonoBehaviour",
            "{",
            "    [System.Serializable]",
            "    public class Inner",
            "    {",
            "        public int count = 1;",
            "    }",
            "",
            "    [SerializeField] private Inner inner;",
            "}");
        var current = previous.Replace("count", "total");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"count\")]", result);
    }

    [Fact]
    public void MigratesRenameInSerializableStruct()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "[System.Serializable]",
            "public struct Stats",
            "{",
            "    public int strength;",
            "}");
        var current = previous.Replace("strength", "power");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"strength\")]", result);
    }

    // Fully-qualified migration attributes must dedup exactly like short ones.
    [Fact]
    public void DoesNotDuplicateFullyQualifiedFormerlySerializedAs()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private float speed = 5f;",
            "}");
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [UnityEngine.Serialization.FormerlySerializedAs(\"speed\")]",
            "    [SerializeField] private float velocity = 5f;",
            "}");

        // The attribute (in fully qualified form) is already present for the old
        // name — a re-run must not add a duplicate short-form attribute.
        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
        Assert.Equal(current, SerializedFieldMigrator.Migrate(previous, current));
    }

    // V-C9: a base class defined in another file may derive from MonoBehaviour;
    // without semantic information the collector must be permissive.
    [Fact]
    public void ProtectsPublicFieldUnderIndirectUnityBaseWithoutSemanticModel()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Enemy : EnemyBase",
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
    public void DoesNotProtectPublicFieldWhenOnlyBaseLooksLikeInterface()
    {
        var previous = Lines(
            "public class Plain : IDamageable",
            "{",
            "    public float speed = 5f;",
            "}");
        var current = previous.Replace("speed", "moveSpeed");

        Assert.Empty(SerializedFieldMigrator.FindRenames(previous, current));
    }

    // The rename-shape gate applies to the string API too (V-C3/V-C4): an edit
    // that changes anything besides the identifiers must never migrate.
    [Fact]
    public void StringApiRejectsRenameMixedWithOtherEdits()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private float speed = 5f;",
            "}");
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private float velocity = 9f;",
            "}");

        // Name AND initializer changed: not a clean rename.
        Assert.Equal(current, SerializedFieldMigrator.Migrate(previous, current));
    }

    // Recognizer-level pins for the token comparison itself.
    [Fact]
    public void RenameShapeIgnoresOldNameInCommentsAndStrings()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class C : MonoBehaviour",
            "{",
            "    // speed in m/s",
            "    [SerializeField] private float speed = 1f;",
            "    private void L() { Debug.Log(\"speed\"); }",
            "}");
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class C : MonoBehaviour",
            "{",
            "    // speed in m/s",
            "    [SerializeField] private float velocity = 1f;",
            "    private void L() { Debug.Log(\"speed\"); }",
            "}");
        var renames = SerializedFieldMigrator.FindRenames(previous, current);

        Assert.True(IdentifierRenameRecognizer.IsRenameShaped(previous, current, renames));
    }

    // NEW-1 (regression of the V-C1 data-loss class): Rename Symbol only rewrites
    // the references of the one symbol it targets. An unrelated identifier that
    // happens to share the field's old name (a method parameter here) legitimately
    // keeps the old name after the rename. The shape gate must NOT treat that
    // leftover as a partial substitution, or the migration is silently skipped and
    // Unity drops the serialized value.
    [Fact]
    public void MigratesWhenAnUnrelatedParameterSharesTheOldName()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Mover : MonoBehaviour",
            "{",
            "    [SerializeField] private float speed = 5f;",
            "    public void SetSpeed(float speed) { this.speed = speed; }",
            "}");
        // Field 'speed' renamed to 'velocity' (declaration + this.speed reference);
        // the SetSpeed parameter 'speed' is a different symbol and stays.
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class Mover : MonoBehaviour",
            "{",
            "    [SerializeField] private float velocity = 5f;",
            "    public void SetSpeed(float speed) { this.velocity = speed; }",
            "}");
        var renames = SerializedFieldMigrator.FindRenames(previous, current);

        Assert.True(IdentifierRenameRecognizer.IsRenameShaped(previous, current, renames));
        Assert.Contains("[FormerlySerializedAs(\"speed\")]", SerializedFieldMigrator.Migrate(previous, current));
    }

    // NEW-1: two components in the same file sharing a field name; renaming one
    // must migrate even though the other still carries the old name.
    [Fact]
    public void MigratesWhenASecondTypeInTheFileSharesTheFieldName()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class A : MonoBehaviour",
            "{",
            "    [SerializeField] private float speed = 1f;",
            "}",
            "",
            "public class B : MonoBehaviour",
            "{",
            "    [SerializeField] private float speed = 2f;",
            "}");
        // Only A.speed is renamed; B.speed is a different symbol and stays.
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class A : MonoBehaviour",
            "{",
            "    [SerializeField] private float velocity = 1f;",
            "}",
            "",
            "public class B : MonoBehaviour",
            "{",
            "    [SerializeField] private float speed = 2f;",
            "}");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"speed\")]", result);
        // B's field is untouched.
        Assert.Contains("private float speed = 2f;", result);
    }

    // The shape gate still rejects an edit that changes anything besides applying a
    // detected rename (here an extra statement is inserted alongside the rename).
    [Fact]
    public void RenameShapeRejectsRenameMixedWithAddedTokens()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class C : MonoBehaviour",
            "{",
            "    [SerializeField] private float speed = 1f;",
            "    private void A() { }",
            "}");
        var current = Lines(
            "using UnityEngine;",
            "",
            "public class C : MonoBehaviour",
            "{",
            "    [SerializeField] private float velocity = 1f;",
            "    private void A() { Debug.Log(velocity); }",
            "}");
        var renames = SerializedFieldMigrator.FindRenames(previous, current);

        Assert.False(IdentifierRenameRecognizer.IsRenameShaped(previous, current, renames));
    }
}
