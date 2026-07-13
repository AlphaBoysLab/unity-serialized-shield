using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AlphaBoysLab.SerializedShield.Editor.Tests
{
    public sealed class SerializedShieldScriptAnalyzerTests
    {
        [Test]
        public void CountsSimpleAttribute()
        {
            string source = "[FormerlySerializedAs(\"oldName\")]\nprivate int newName;\n";
            Assert.AreEqual(1, SerializedShieldScriptAnalyzer.CountFormerlySerializedAsAttributes(source));
            CollectionAssert.AreEqual(
                new[] { "oldName" },
                SerializedShieldScriptAnalyzer.ExtractFormerlySerializedAsNames(source));
        }

        [Test]
        public void IgnoresAttributesInCommentsAndStrings()
        {
            // Audit U-M4: occurrences in comments/strings must be invisible everywhere.
            string source =
                "// [FormerlySerializedAs(\"commented\")]\n"
                + "/* [FormerlySerializedAs(\"blockCommented\")] */\n"
                + "string s = \"[FormerlySerializedAs(\\\"inString\\\")]\";\n"
                + "[FormerlySerializedAs(\"real\")]\n"
                + "private int field;\n";

            CollectionAssert.AreEqual(
                new[] { "real" },
                SerializedShieldScriptAnalyzer.ExtractFormerlySerializedAsNames(source));

            string removed = SerializedShieldScriptAnalyzer.RemoveFormerlySerializedAsAttributes(source);
            StringAssert.Contains("commented", removed);
            StringAssert.Contains("blockCommented", removed);
            StringAssert.Contains("inString", removed);
            StringAssert.DoesNotContain("\"real\"", removed);
        }

        [Test]
        public void CombinedAttributeListIsDetectedAndRemovalKeepsSiblings()
        {
            // Audit U-M3.
            string source = "[SerializeField, FormerlySerializedAs(\"oldName\")]\nprivate int newName;\n";

            CollectionAssert.AreEqual(
                new[] { "oldName" },
                SerializedShieldScriptAnalyzer.ExtractFormerlySerializedAsNames(source));

            string removed = SerializedShieldScriptAnalyzer.RemoveFormerlySerializedAsAttributes(source);
            StringAssert.Contains("[SerializeField]", removed);
            StringAssert.DoesNotContain("FormerlySerializedAs", removed);
        }

        [Test]
        public void CombinedListWithAttributeFirstKeepsSiblings()
        {
            string source = "[FormerlySerializedAs(\"oldName\"), SerializeField]\nprivate int newName;\n";
            string removed = SerializedShieldScriptAnalyzer.RemoveFormerlySerializedAsAttributes(source);

            StringAssert.Contains("[SerializeField]", removed);
            StringAssert.DoesNotContain("FormerlySerializedAs", removed);
        }

        [Test]
        public void StandaloneAttributeLineIsRemovedEntirely()
        {
            string source = "    [FormerlySerializedAs(\"oldName\")]\n    private int newName;\n";
            string removed = SerializedShieldScriptAnalyzer.RemoveFormerlySerializedAsAttributes(source);

            Assert.AreEqual("    private int newName;\n", removed);
        }

        [Test]
        public void InlineAttributeIsRemovedKeepingDeclaration()
        {
            string source = "[FormerlySerializedAs(\"oldName\")] private int newName;\n";
            string removed = SerializedShieldScriptAnalyzer.RemoveFormerlySerializedAsAttributes(source);

            Assert.AreEqual("private int newName;\n", removed);
        }

        [Test]
        public void SelectiveRemovalKeepsOtherNames()
        {
            // Feature gap 8: only verified names may be removed.
            string source =
                "[FormerlySerializedAs(\"verified\")]\n"
                + "[FormerlySerializedAs(\"unverified\")]\n"
                + "private int field;\n";
            int removedCount;
            string removed = SerializedShieldScriptAnalyzer.RemoveFormerlySerializedAsAttributes(
                source,
                new List<string> { "verified" },
                out removedCount);

            Assert.AreEqual(1, removedCount);
            StringAssert.DoesNotContain("(\"verified\")", removed);
            StringAssert.Contains("[FormerlySerializedAs(\"unverified\")]", removed);
        }

        [Test]
        public void QualifiedAndVerbatimFormsAreRecognized()
        {
            string source =
                "[UnityEngine.Serialization.FormerlySerializedAs(\"qualified\")]\n"
                + "[FormerlySerializedAsAttribute(@\"verbatim\")]\n"
                + "private int field;\n";

            CollectionAssert.AreEquivalent(
                new[] { "qualified", "verbatim" },
                SerializedShieldScriptAnalyzer.ExtractFormerlySerializedAsNames(source));
        }

        [Test]
        public void FindsMigrationForAttributeOnPrecedingLine()
        {
            string source =
                "[FormerlySerializedAs(\"oldName\")]\n"
                + "[SerializeField]\n"
                + "private float newName = 1f;\n";
            List<SerializedShieldFieldMigration> migrations = SerializedShieldScriptAnalyzer.FindFieldMigrations(source);

            Assert.AreEqual(1, migrations.Count);
            Assert.AreEqual("newName", migrations[0].CurrentName);
            CollectionAssert.AreEqual(new[] { "oldName" }, migrations[0].FormerNames);
        }

        [Test]
        public void FindsMigrationForInlineAttribute()
        {
            // Audit U-H4: inline form was previously counted but never detected.
            string source = "[FormerlySerializedAs(\"oldName\")] public int newName;\n";
            List<SerializedShieldFieldMigration> migrations = SerializedShieldScriptAnalyzer.FindFieldMigrations(source);

            Assert.AreEqual(1, migrations.Count);
            Assert.AreEqual("newName", migrations[0].CurrentName);
        }

        [Test]
        public void CommentLineBetweenAttributeAndFieldDoesNotBreakDetection()
        {
            // Audit U-H5.
            string source =
                "[FormerlySerializedAs(\"oldName\")]\n"
                + "// speed in meters per second\n"
                + "private float newName;\n";
            List<SerializedShieldFieldMigration> migrations = SerializedShieldScriptAnalyzer.FindFieldMigrations(source);

            Assert.AreEqual(1, migrations.Count);
            Assert.AreEqual("newName", migrations[0].CurrentName);
        }

        [Test]
        public void NonAsciiFieldNameIsDetected()
        {
            // Audit U-M2.
            string source =
                "[FormerlySerializedAs(\"velocidad\")]\n"
                + "private float velocidadMáxima;\n";
            List<SerializedShieldFieldMigration> migrations = SerializedShieldScriptAnalyzer.FindFieldMigrations(source);

            Assert.AreEqual(1, migrations.Count);
            Assert.AreEqual("velocidadMáxima", migrations[0].CurrentName);
        }

        [Test]
        public void MultiDeclaratorFieldIsSkippedWithWarning()
        {
            // Audit U-M5.
            string source =
                "[FormerlySerializedAs(\"oldName\")]\n"
                + "private int first, second;\n";
            List<string> warnings = new List<string>();
            List<SerializedShieldFieldMigration> migrations = SerializedShieldScriptAnalyzer.FindFieldMigrations(source, warnings);

            Assert.AreEqual(0, migrations.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("multi-declarator", warnings[0]);
        }

        [Test]
        public void GenericFieldWithCommaIsNotTreatedAsMultiDeclarator()
        {
            string source =
                "[FormerlySerializedAs(\"oldMap\")]\n"
                + "private Dictionary<string, int> newMap = new Dictionary<string, int>();\n";
            List<SerializedShieldFieldMigration> migrations = SerializedShieldScriptAnalyzer.FindFieldMigrations(source);

            Assert.AreEqual(1, migrations.Count);
            Assert.AreEqual("newMap", migrations[0].CurrentName);
        }

        [Test]
        public void CombinedAttributeListYieldsMigration()
        {
            string source = "[SerializeField, FormerlySerializedAs(\"oldName\")]\nprivate int newName;\n";
            List<SerializedShieldFieldMigration> migrations = SerializedShieldScriptAnalyzer.FindFieldMigrations(source);

            Assert.AreEqual(1, migrations.Count);
            CollectionAssert.AreEqual(new[] { "oldName" }, migrations[0].FormerNames);
        }

        [Test]
        public void AttributeInCommentDoesNotCreateMigration()
        {
            string source =
                "// [FormerlySerializedAs(\"ghost\")]\n"
                + "private int field;\n";
            List<SerializedShieldFieldMigration> migrations = SerializedShieldScriptAnalyzer.FindFieldMigrations(source);

            Assert.AreEqual(0, migrations.Count);
        }

        [Test]
        public void MultipleFormerNamesStackOnOneField()
        {
            string source =
                "[FormerlySerializedAs(\"first\")]\n"
                + "[FormerlySerializedAs(\"second\")]\n"
                + "private int newest;\n";
            List<SerializedShieldFieldMigration> migrations = SerializedShieldScriptAnalyzer.FindFieldMigrations(source);

            Assert.AreEqual(1, migrations.Count);
            CollectionAssert.AreEquivalent(new[] { "first", "second" }, migrations[0].FormerNames);
        }

        [Test]
        public void UnrelatedCodeLineBreaksAssociation()
        {
            string source =
                "[FormerlySerializedAs(\"oldName\")]\n"
                + "public void SomeMethod() { }\n"
                + "private int stray;\n";
            List<SerializedShieldFieldMigration> migrations = SerializedShieldScriptAnalyzer.FindFieldMigrations(source);

            Assert.AreEqual(0, migrations.Count);
        }

        [Test]
        public void RemovalInsideFullClassKeepsRestOfFileIntact()
        {
            string source =
                "using UnityEngine;\n"
                + "using UnityEngine.Serialization;\n"
                + "\n"
                + "public class Player : MonoBehaviour\n"
                + "{\n"
                + "    // old name kept for data migration\n"
                + "    [FormerlySerializedAs(\"speed\")]\n"
                + "    [SerializeField] private float movementSpeed = 3f;\n"
                + "}\n";
            string removed = SerializedShieldScriptAnalyzer.RemoveFormerlySerializedAsAttributes(source);

            StringAssert.DoesNotContain("FormerlySerializedAs", removed);
            StringAssert.Contains("[SerializeField] private float movementSpeed = 3f;", removed);
            StringAssert.Contains("// old name kept for data migration", removed);
        }
    }
}
