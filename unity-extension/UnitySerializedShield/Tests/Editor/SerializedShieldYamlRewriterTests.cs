using System.Collections.Generic;
using NUnit.Framework;

namespace AlphaBoysLab.SerializedShield.Editor.Tests
{
    public sealed class SerializedShieldYamlRewriterTests
    {
        private const string ScriptGuid = "abc123def456abc123def456abc12345";
        private const string OtherGuid = "ffff23def456abc123def456abc12345";

        private static List<SerializedShieldFieldMigration> Migration(string currentName, params string[] formerNames)
        {
            return new List<SerializedShieldFieldMigration> { Field(currentName, formerNames) };
        }

        private static SerializedShieldFieldMigration Field(string currentName, params string[] formerNames)
        {
            SerializedShieldFieldMigration migration = new SerializedShieldFieldMigration
            {
                CurrentName = currentName
            };
            migration.FormerNames.AddRange(formerNames);
            return migration;
        }

        private static string ScriptComponentBlock(string guid, string body)
        {
            return "--- !u!114 &1234567890\n"
                + "MonoBehaviour:\n"
                + "  m_ObjectHideFlags: 0\n"
                + "  m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}\n"
                + "  m_Name: \n"
                + "  m_EditorClassIdentifier: \n"
                + body;
        }

        [Test]
        public void RenamesTopLevelKeyInScriptInstanceBlock()
        {
            string yaml = ScriptComponentBlock(ScriptGuid, "  speed: 5\n");
            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, Migration("movementSpeed", "speed"));

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1, result.Renames.Count);
            StringAssert.Contains("  movementSpeed: 5\n", result.Text);
            StringAssert.DoesNotContain("  speed: 5", result.Text);
        }

        [Test]
        public void NestedKeyWithSameNameIsNotRenamed()
        {
            // Audit U-C1: a nested [Serializable] class member sharing the old name must
            // never be rewritten.
            string yaml = ScriptComponentBlock(
                ScriptGuid,
                "  speed: 5\n"
                + "  nestedSettings:\n"
                + "    speed: 99\n");
            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, Migration("movementSpeed", "speed"));

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1, result.Renames.Count);
            StringAssert.Contains("  movementSpeed: 5\n", result.Text);
            StringAssert.Contains("    speed: 99\n", result.Text);
        }

        [Test]
        public void NestedKeyEqualToNewNameDoesNotBlockMigration()
        {
            // Audit U-C1 (second half): a nested key equal to the NEW name previously made
            // the guard skip the legitimate top-level migration.
            string yaml = ScriptComponentBlock(
                ScriptGuid,
                "  speed: 5\n"
                + "  nestedSettings:\n"
                + "    movementSpeed: 99\n");
            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, Migration("movementSpeed", "speed"));

            Assert.IsTrue(result.Changed);
            StringAssert.Contains("  movementSpeed: 5\n", result.Text);
            StringAssert.Contains("    movementSpeed: 99\n", result.Text);
        }

        [Test]
        public void TopLevelNewKeyBlocksRenameAndWarns()
        {
            string yaml = ScriptComponentBlock(
                ScriptGuid,
                "  movementSpeed: 7\n"
                + "  speed: 5\n");
            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, Migration("movementSpeed", "speed"));

            Assert.IsFalse(result.Changed);
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.Contains("  speed: 5\n", result.Text);
        }

        [Test]
        public void UnrelatedComponentWithSameKeyNameIsNotRenamed()
        {
            // Audit U-C2: a block referencing the script GUID (e.g. a MonoScript object
            // field) but whose m_Script points elsewhere must not be rewritten.
            string yaml = ScriptComponentBlock(ScriptGuid, "  speed: 5\n")
                + "--- !u!114 &222\n"
                + "MonoBehaviour:\n"
                + "  m_Script: {fileID: 11500000, guid: " + OtherGuid + ", type: 3}\n"
                + "  scriptReference: {fileID: 11500000, guid: " + ScriptGuid + ", type: 3}\n"
                + "  speed: 42\n";
            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, Migration("movementSpeed", "speed"));

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1, result.Renames.Count);
            StringAssert.Contains("  movementSpeed: 5\n", result.Text);
            StringAssert.Contains("  speed: 42\n", result.Text);
        }

        [Test]
        public void BlockWithoutScriptAnchorIsNeverTouched()
        {
            string yaml = "--- !u!1 &100\n"
                + "GameObject:\n"
                + "  speed: 5\n";
            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, Migration("movementSpeed", "speed"));

            Assert.IsFalse(result.Changed);
            Assert.AreEqual(yaml, result.Text);
        }

        [Test]
        public void ListElementFirstKeyIsNotRenamed()
        {
            // Audit U-M1 decision: "- key:" list elements belong to nested array fields,
            // never to the component's own top-level fields, so they are deliberately not
            // rewritten. Unity's own reserialization migrates nested FSA fields; the
            // verification pass keeps the attribute when an old-name key remains.
            string yaml = ScriptComponentBlock(
                ScriptGuid,
                "  waypoints:\n"
                + "  - speed: 1\n"
                + "    label: a\n"
                + "  - speed: 2\n"
                + "    label: b\n");
            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, Migration("movementSpeed", "speed"));

            Assert.IsFalse(result.Changed);
            StringAssert.Contains("  - speed: 1\n", result.Text);
        }

        [Test]
        public void NonAsciiKeyIsRenamed()
        {
            string yaml = ScriptComponentBlock(ScriptGuid, "  velocidadMáxima: 5\n");
            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, Migration("maxSpeed", "velocidadMáxima"));

            Assert.IsTrue(result.Changed);
            StringAssert.Contains("  maxSpeed: 5\n", result.Text);
        }

        [Test]
        public void MultipleDocumentsOnlyMatchingOnesAreRewritten()
        {
            string yaml = ScriptComponentBlock(ScriptGuid, "  speed: 1\n")
                + ScriptComponentBlock(OtherGuid, "  speed: 2\n")
                + ScriptComponentBlock(ScriptGuid, "  speed: 3\n");
            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, Migration("movementSpeed", "speed"));

            Assert.AreEqual(2, result.Renames.Count);
            StringAssert.Contains("  speed: 2\n", result.Text);
            StringAssert.DoesNotContain("  speed: 1", result.Text);
            StringAssert.DoesNotContain("  speed: 3", result.Text);
        }

        [Test]
        public void CrlfLineEndingsArePreserved()
        {
            string yaml = ScriptComponentBlock(ScriptGuid, "  speed: 5\n").Replace("\n", "\r\n");
            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, Migration("movementSpeed", "speed"));

            Assert.IsTrue(result.Changed);
            StringAssert.Contains("  movementSpeed: 5\r\n", result.Text);
            Assert.IsTrue(result.Text.Replace("\r\n", string.Empty).IndexOf('\n') < 0, "No bare LF line endings expected.");
        }

        [Test]
        public void FindKeysInScriptBlocksDetectsNestedAndListKeys()
        {
            string yaml = ScriptComponentBlock(
                ScriptGuid,
                "  nested:\n"
                + "    speed: 1\n"
                + "  items:\n"
                + "  - speed: 2\n");
            List<SerializedShieldYamlKeyReference> references = SerializedShieldYamlRewriter.FindKeysInScriptBlocks(
                yaml, ScriptGuid, new HashSet<string> { "speed" });

            Assert.AreEqual(2, references.Count);
        }

        [Test]
        public void FindKeysInScriptBlocksIgnoresUnrelatedBlocks()
        {
            string yaml = ScriptComponentBlock(OtherGuid, "  speed: 1\n")
                + "--- !u!114 &333\n"
                + "MonoBehaviour:\n"
                + "  m_Script: {fileID: 11500000, guid: " + ScriptGuid + ", type: 3}\n"
                + "  other: 1\n";
            List<SerializedShieldYamlKeyReference> references = SerializedShieldYamlRewriter.FindKeysInScriptBlocks(
                yaml, ScriptGuid, new HashSet<string> { "speed" });

            Assert.AreEqual(0, references.Count);
        }

        [Test]
        public void PrefabOverridePropertyPathIsDetected()
        {
            // Audit U-C3: overrides reference the prefab GUID, not the script GUID, so
            // detection must work on the propertyPath alone.
            string yaml = "--- !u!1001 &400\n"
                + "PrefabInstance:\n"
                + "  m_Modifications:\n"
                + "  - target: {fileID: 123, guid: " + OtherGuid + ", type: 3}\n"
                + "    propertyPath: speed\n"
                + "    value: 12\n"
                + "  - target: {fileID: 123, guid: " + OtherGuid + ", type: 3}\n"
                + "    propertyPath: speed.x\n"
                + "    value: 1\n"
                + "  - target: {fileID: 123, guid: " + OtherGuid + ", type: 3}\n"
                + "    propertyPath: speedLimit\n"
                + "    value: 3\n";
            List<SerializedShieldYamlKeyReference> references = SerializedShieldYamlRewriter.FindPropertyPathReferences(
                yaml, new HashSet<string> { "speed" });

            Assert.AreEqual(2, references.Count);
        }

        [Test]
        public void RecycledFieldNameDoesNotCrossWire()
        {
            // Audit N2: field A renamed damage->power while field B was renamed
            // power->attackPower. An asset carrying only 'damage' must become 'power'
            // (field A's data) and must NOT then be re-renamed to 'attackPower'.
            string yaml = ScriptComponentBlock(ScriptGuid, "  damage: 42\n");
            List<SerializedShieldFieldMigration> migrations = new List<SerializedShieldFieldMigration>
            {
                Field("power", "damage"),
                Field("attackPower", "power"),
            };

            SerializedShieldYamlRewriteResult result = SerializedShieldYamlRewriter.RenameComponentKeys(
                yaml, ScriptGuid, migrations);

            StringAssert.Contains("  power: 42\n", result.Text);
            StringAssert.DoesNotContain("attackPower", result.Text);
            Assert.AreEqual(1, result.Renames.Count);
        }

        [Test]
        public void NestedPropertyPathIsDetected()
        {
            // Audit N3: an override on a nested serializable field references the
            // former name as a non-root path segment ("container.oldNested" and
            // "items.Array.data[0].oldNested"); both must be detected as blockers.
            string yaml = "--- !u!1001 &400\n"
                + "PrefabInstance:\n"
                + "  m_Modifications:\n"
                + "  - target: {fileID: 123, guid: " + OtherGuid + ", type: 3}\n"
                + "    propertyPath: container.oldNested\n"
                + "    value: 7\n"
                + "  - target: {fileID: 123, guid: " + OtherGuid + ", type: 3}\n"
                + "    propertyPath: items.Array.data[0].oldNested\n"
                + "    value: 8\n"
                + "  - target: {fileID: 123, guid: " + OtherGuid + ", type: 3}\n"
                + "    propertyPath: unrelated.field\n"
                + "    value: 9\n";

            List<SerializedShieldYamlKeyReference> references = SerializedShieldYamlRewriter.FindPropertyPathReferences(
                yaml, new HashSet<string> { "oldNested" });

            Assert.AreEqual(2, references.Count);
        }

        [Test]
        public void AnimationBindingIsDetectedOnlyWhenGuidPresent()
        {
            string yamlWithGuid = "AnimationClip:\n"
                + "  m_FloatCurves:\n"
                + "  - attribute: speed\n"
                + "    script: {fileID: 11500000, guid: " + ScriptGuid + ", type: 3}\n";
            string yamlWithoutGuid = yamlWithGuid.Replace(ScriptGuid, OtherGuid);

            Assert.AreEqual(1, SerializedShieldYamlRewriter.FindAnimationBindingReferences(
                yamlWithGuid, ScriptGuid, new HashSet<string> { "speed" }).Count);
            Assert.AreEqual(0, SerializedShieldYamlRewriter.FindAnimationBindingReferences(
                yamlWithoutGuid, ScriptGuid, new HashSet<string> { "speed" }).Count);
        }

        [Test]
        public void SplitLinesKeepingEndingsRoundTrips()
        {
            string[] samples =
            {
                "a\nb\nc",
                "a\r\nb\rc\n",
                "",
                "no newline",
                "\n",
                "\r\n\r\n"
            };

            foreach (string sample in samples)
            {
                Assert.AreEqual(sample, string.Concat(SerializedShieldYamlRewriter.SplitLinesKeepingEndings(sample)));
            }
        }
    }
}
