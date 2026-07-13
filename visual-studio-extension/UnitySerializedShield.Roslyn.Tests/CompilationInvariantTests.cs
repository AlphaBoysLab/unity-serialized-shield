using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnitySerializedShield.Roslyn;

namespace UnitySerializedShield.Roslyn.Tests;

// Invariant: migrated output must COMPILE with zero errors against a UnityEngine
// stub — a migration that breaks the build (e.g. the alias-only using bug V-C5)
// is worse than no migration. Also exercises the semantic-model overload the
// VSIX host actually calls, which previously had zero coverage.
public class CompilationInvariantTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    // Minimal stand-in for the Unity assemblies referenced by real projects.
    private const string UnityEngineStub = @"
namespace UnityEngine
{
    public class MonoBehaviour { }
    public class ScriptableObject { }
    public class StateMachineBehaviour { }
    public sealed class SerializeField : System.Attribute { }
    public sealed class SerializeReference : System.Attribute { }
    public sealed class HideInInspector : System.Attribute { }
    public static class Debug { public static void Log(object message) { } }
}

namespace UnityEngine.Serialization
{
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = true)]
    public sealed class FormerlySerializedAsAttribute : System.Attribute
    {
        public FormerlySerializedAsAttribute(string oldName) { }
    }
}
";

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var trees = sources
            .Concat(new[] { UnityEngineStub })
            .Select(source => CSharpSyntaxTree.ParseText(source))
            .ToList();

        var trustedAssemblies = ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator);
        var references = trustedAssemblies
            .Where(path => System.IO.Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll")
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList<MetadataReference>();

        return CSharpCompilation.Create(
            "MigratedOutput",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static void AssertCompilesWithoutErrors(string source)
    {
        var compilation = CreateCompilation(source);
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0, "Migrated output must compile. Errors:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void MigratedOutputCompiles()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    [SerializeField] private float maxDistance = 100f;",
            "    private void Awake() { Debug.Log(maxDistance); }",
            "}");
        var current = previous.Replace("maxDistance", "attackDistance");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]", result);
        AssertCompilesWithoutErrors(result);
    }

    // V-C5: an alias-only using does not import the namespace; the migrator must
    // add the real using so the short attribute name still resolves.
    [Fact]
    public void MigratedOutputCompilesWithAliasOnlyUsing()
    {
        var previous = Lines(
            "using UnityEngine;",
            "using UES = UnityEngine.Serialization;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    [UES.FormerlySerializedAs(\"old\")]",
            "    [SerializeField] private float maxDistance = 100f;",
            "}");
        var current = previous.Replace("float maxDistance", "float attackDistance");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]", result);
        Assert.Contains("using UnityEngine.Serialization;", result);
        AssertCompilesWithoutErrors(result);
    }

    [Fact]
    public void MigratedAutoPropertyOutputCompiles()
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
        AssertCompilesWithoutErrors(result);
    }

    [Fact]
    public void MigratedVerbatimIdentifierOutputCompiles()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class Player : MonoBehaviour",
            "{",
            "    [SerializeField] private int @class = 1;",
            "}");
        var current = previous.Replace("@class", "@event");

        var result = SerializedFieldMigrator.Migrate(previous, current);

        Assert.Contains("[FormerlySerializedAs(\"class\")]", result);
        AssertCompilesWithoutErrors(result);
    }

    // ----- Semantic-model overload (the entry point the VSIX host calls) -----

    private static (SyntaxNode Root, SemanticModel Model) ParseWithModel(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CreateCompilation().AddSyntaxTrees(tree);

        return (tree.GetRoot(), compilation.GetSemanticModel(tree));
    }

    [Fact]
    public void SemanticOverloadMigratesSerializeFieldRename()
    {
        var previous = Lines(
            "using UnityEngine;",
            "",
            "public class EnemySensor : MonoBehaviour",
            "{",
            "    [SerializeField] private float maxDistance = 100f;",
            "}");
        var current = previous.Replace("maxDistance", "attackDistance");

        var (previousRoot, previousModel) = ParseWithModel(previous);
        var (currentRoot, currentModel) = ParseWithModel(current);

        var migrated = SerializedFieldMigrator.Migrate(previousRoot, currentRoot, previousModel, currentModel, out var renames);

        Assert.NotNull(migrated);
        var rename = Assert.Single(renames);
        Assert.Equal("maxDistance", rename.PreviousName);
        Assert.Equal("attackDistance", rename.CurrentName);
        Assert.Contains("[FormerlySerializedAs(\"maxDistance\")]", migrated!.ToFullString());
    }

    // V-C9: with a semantic model, an INDIRECT Unity base (declared elsewhere in
    // the compilation) is resolved and the public field is protected.
    [Fact]
    public void SemanticOverloadResolvesIndirectUnityBase()
    {
        const string baseSource = "using UnityEngine;\n\npublic class EnemyBase : MonoBehaviour { }\n";
        var previous = Lines(
            "public class Enemy : EnemyBase",
            "{",
            "    public float speed = 5f;",
            "}");
        var current = previous.Replace("speed", "moveSpeed");

        var previousTree = CSharpSyntaxTree.ParseText(previous);
        var currentTree = CSharpSyntaxTree.ParseText(current);
        var previousCompilation = CreateCompilation(baseSource).AddSyntaxTrees(previousTree);
        var currentCompilation = CreateCompilation(baseSource).AddSyntaxTrees(currentTree);

        var migrated = SerializedFieldMigrator.Migrate(
            previousTree.GetRoot(),
            currentTree.GetRoot(),
            previousCompilation.GetSemanticModel(previousTree),
            currentCompilation.GetSemanticModel(currentTree),
            out var renames);

        Assert.NotNull(migrated);
        var rename = Assert.Single(renames);
        Assert.Equal("speed", rename.PreviousName);
    }

    // When the semantic model FULLY resolves a non-Unity base chain, it is
    // authoritative: the permissive syntactic fallback must not fire.
    [Fact]
    public void SemanticOverloadRejectsResolvedNonUnityBase()
    {
        const string baseSource = "public class PlainBase { }\n";
        var previous = Lines(
            "public class Data : PlainBase",
            "{",
            "    public float speed = 5f;",
            "}");
        var current = previous.Replace("speed", "moveSpeed");

        var previousTree = CSharpSyntaxTree.ParseText(previous);
        var currentTree = CSharpSyntaxTree.ParseText(current);
        var previousCompilation = CreateCompilation(baseSource).AddSyntaxTrees(previousTree);
        var currentCompilation = CreateCompilation(baseSource).AddSyntaxTrees(currentTree);

        var migrated = SerializedFieldMigrator.Migrate(
            previousTree.GetRoot(),
            currentTree.GetRoot(),
            previousCompilation.GetSemanticModel(previousTree),
            currentCompilation.GetSemanticModel(currentTree),
            out var renames);

        Assert.Null(migrated);
        Assert.Empty(renames);
    }

    // An UNRESOLVED base under a semantic model (missing reference) falls back to
    // the permissive syntactic path, mirroring the no-model behavior.
    [Fact]
    public void SemanticOverloadStaysPermissiveForUnresolvedBase()
    {
        var previous = Lines(
            "public class Enemy : UnknownBase",
            "{",
            "    public float speed = 5f;",
            "}");
        var current = previous.Replace("speed", "moveSpeed");

        var previousTree = CSharpSyntaxTree.ParseText(previous);
        var currentTree = CSharpSyntaxTree.ParseText(current);
        var previousCompilation = CreateCompilation().AddSyntaxTrees(previousTree);
        var currentCompilation = CreateCompilation().AddSyntaxTrees(currentTree);

        var migrated = SerializedFieldMigrator.Migrate(
            previousTree.GetRoot(),
            currentTree.GetRoot(),
            previousCompilation.GetSemanticModel(previousTree),
            currentCompilation.GetSemanticModel(currentTree),
            out var renames);

        Assert.NotNull(migrated);
        Assert.Single(renames);
    }
}
