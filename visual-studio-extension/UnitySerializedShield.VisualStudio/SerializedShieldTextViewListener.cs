using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;
using UnitySerializedShield.Core;

namespace UnitySerializedShield.VisualStudio;

[VisualStudioContribution]
internal sealed class SerializedShieldTextViewListener :
    ExtensionPart,
    ITextViewExtension,
    ITextViewOpenClosedListener,
    ITextViewChangedListener
{
    private const int RenameSettleDelayMilliseconds = 400;
    private const int RenameCommandApplyDelayMilliseconds = 500;
    private const int PrefixRenameApplyDelayMilliseconds = 650;
    private const int PostInsertVerificationDelayMilliseconds = 900;
    private static readonly ConcurrentDictionary<string, string> DocumentSnapshots = new();
    private static readonly ConcurrentDictionary<string, byte> DocumentsBeingUpdated = new();
    private static readonly ConcurrentDictionary<string, PendingRenameOperation> PendingRenameOperations = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastAppliedEditTimes = new();
    private static int openedCount;
    private static int changedCount;
    private static int protectedRenameCount;
    private static volatile string lastDocumentKey = "none";
    private static volatile string lastChangeSummary = "No text view changes observed yet.";

    public TextViewExtensionConfiguration TextViewExtensionConfiguration => new()
    {
        AppliesTo = [DocumentFilter.FromDocumentType(DocumentType.KnownValues.Text)],
    };

    public static string Diagnostics =>
        $"Opened: {openedCount}, Changed: {changedCount}, Protected renames: {protectedRenameCount}\n"
        + $"Last document: {lastDocumentKey}\n"
        + $"Last change: {lastChangeSummary}";

    public Task TextViewOpenedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken)
    {
        WriteDiagnostic("TextViewOpenedAsync called.");

        if (TryGetCSharpDocumentKey(textView, out var documentKey))
        {
            Interlocked.Increment(ref openedCount);
            lastDocumentKey = documentKey;
            DocumentSnapshots[documentKey] = textView.Document.Text.CopyToString();
            WriteDiagnostic($"Opened C# document: {documentKey}");
        }

        return Task.CompletedTask;
    }

    public Task TextViewClosedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken)
    {
        if (TryGetCSharpDocumentKey(textView, out var documentKey))
        {
            DocumentSnapshots.TryRemove(documentKey, out _);
            DocumentsBeingUpdated.TryRemove(documentKey, out _);
            PendingRenameOperations.TryRemove(documentKey, out _);
            LastAppliedEditTimes.TryRemove(documentKey, out _);
        }

        return Task.CompletedTask;
    }

    public async Task TextViewChangedAsync(TextViewChangedArgs args, CancellationToken cancellationToken)
    {
        WriteDiagnostic("TextViewChangedAsync called.");
        var textView = args.AfterTextView;

        if (!TryGetCSharpDocumentKey(textView, out var documentKey))
        {
            return;
        }

        Interlocked.Increment(ref changedCount);
        lastDocumentKey = documentKey;

        var currentText = textView.Document.Text.CopyToString();

        if (LastAppliedEditTimes.TryGetValue(documentKey, out var lastAppliedTime)
            && DateTimeOffset.UtcNow - lastAppliedTime < TimeSpan.FromMilliseconds(1000))
        {
            DocumentSnapshots[documentKey] = currentText;
            lastChangeSummary = "Ignored change within edit post-apply cool-down period.";
            WriteDiagnostic($"{documentKey}: ignored edit in cool-down period. CurrentText update stored.");
            return;
        }

        if (DocumentsBeingUpdated.ContainsKey(documentKey))
        {
            DocumentSnapshots[documentKey] = currentText;
            lastChangeSummary = "Ignored extension-authored edit.";
            WriteDiagnostic($"{documentKey}: ignored extension-authored edit.");
            return;
        }

        var eventBeforeText = args.BeforeTextView.Document.Text.CopyToString();
        var previousText = DocumentSnapshots.TryGetValue(documentKey, out var snapshotText)
            ? snapshotText
            : eventBeforeText;
        var pendingOperation = PendingRenameOperations.GetValueOrDefault(documentKey);
        var baselineText = pendingOperation is not null
            ? pendingOperation.BaselineText
            : previousText;

        if (previousText is null || baselineText == currentText)
        {
            DocumentSnapshots[documentKey] = currentText;
            lastChangeSummary = "Change observed, but previous/current text were equivalent.";
            WriteDiagnostic($"{documentKey}: ignored equivalent change.");
            return;
        }

        var renames = FormerlySerializedAsBuilder.FindRenamedSerializedFields(baselineText, currentText);

        if (renames.Count == 0)
        {
            DocumentSnapshots[documentKey] = currentText;
            PendingRenameOperations.TryRemove(documentKey, out _);
            lastChangeSummary = $"Ignored change. No serialized field rename found. Edits: {args.Edits.Count}.";
            WriteDiagnostic($"{documentKey}: no serialized field rename found. Edits: {args.Edits.Count}.");
            return;
        }

        // Detect whether this event looks like a bulk identifier replacement (Ctrl+R)
        // rather than incremental character-by-character typing.
        var bulkEdit = IsBulkIdentifierEdit(eventBeforeText, currentText) || args.Edits.Count > 1;
        var renameCommandEdit = IsWholeSerializedFieldIdentifierEdit(args.Edits, renames)
            || IsSerializedFieldNumericSuffixRename(args.Edits, renames)
            || IsSerializedFieldUnityPrefixCleanupRename(renames);

        var renameSignature = BuildRenameSignature(renames);
        var operation = QueuePendingRename(documentKey, baselineText, renameSignature, bulkEdit, renameCommandEdit);
        lastChangeSummary = $"Queued serialized rename migration candidate. Version: {operation.Version}. Seen: {operation.SeenCount}. Bulk: {operation.IsBulkEdit}. Rename edit: {operation.IsRenameCommandEdit}.";
        WriteDiagnostic($"{documentKey}: queued serialized rename candidate v{operation.Version}. Seen: {operation.SeenCount}. Bulk: {operation.IsBulkEdit}. Rename edit: {operation.IsRenameCommandEdit}. Renames: {string.Join(", ", renames.Select(rename => $"{rename.PreviousName}->{rename.CurrentName}"))}");

        // Do NOT update DocumentSnapshots here when a rename is pending.
        // This preserves the original baseline so we never lose the true old field name.

        _ = ApplyPendingRenameAsync(textView, documentKey, operation.Version);
    }

    private static bool TryGetCSharpDocumentKey(ITextViewSnapshot textView, out string documentKey)
    {
        var filePath = textView.FilePath;

        if (string.IsNullOrWhiteSpace(filePath) || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            documentKey = string.Empty;
            return false;
        }

        documentKey = textView.Uri?.ToString() ?? filePath;
        return true;
    }

    /// <summary>
    /// Determines whether a text change is a bulk identifier replacement (typical of
    /// Ctrl+R / Rename Symbol) rather than incremental character-by-character typing.
    ///
    /// Approach: find the single contiguous differing span between the event's immediate
    /// before and after text. If the replaced span is longer than 2 characters on either
    /// side, it is almost certainly a whole-identifier replacement, not a single keystroke.
    /// Multiple non-adjacent changed spans also indicate a bulk rename across several
    /// references in the same file.
    /// </summary>
    private static bool IsBulkIdentifierEdit(string beforeText, string afterText)
    {
        if (beforeText == afterText)
        {
            return false;
        }

        var minLength = Math.Min(beforeText.Length, afterText.Length);

        // Find first position that differs (from the start).
        var prefixEnd = 0;

        while (prefixEnd < minLength && beforeText[prefixEnd] == afterText[prefixEnd])
        {
            prefixEnd++;
        }

        // Find first position that differs (from the end).
        var beforeEnd = beforeText.Length - 1;
        var afterEnd = afterText.Length - 1;

        while (beforeEnd >= prefixEnd && afterEnd >= prefixEnd
               && beforeText[beforeEnd] == afterText[afterEnd])
        {
            beforeEnd--;
            afterEnd--;
        }

        var oldSpanLength = beforeEnd - prefixEnd + 1;
        var newSpanLength = afterEnd - prefixEnd + 1;

        // Single-char insert, delete, or replacement → incremental typing.
        // Multi-char span on either side → bulk replacement (Ctrl+R).
        if (oldSpanLength > 2 || newSpanLength > 2)
        {
            return true;
        }

        // Even if the single diff span is small, check whether there are additional
        // non-adjacent changed regions (multiple occurrences renamed at once).
        // With the greedy prefix/suffix match above, a second occurrence would make the
        // suffix match stop early, inflating the span — so oldSpanLength/newSpanLength
        // would already be large. But as a safety net, if the number of edits reported
        // by VS is > 1, treat it as a bulk rename.
        // Note: args.Edits.Count is not available here, but the caller already checked.
        return false;
    }

    private static bool IsSmallSerializedFieldRename(string beforeText, string afterText)
    {
        var renames = FormerlySerializedAsBuilder.FindRenamedSerializedFields(beforeText, afterText);

        if (renames.Count != 1)
        {
            return false;
        }

        var rename = renames[0];
        var oldName = rename.PreviousName;
        var newName = rename.CurrentName;

        if (Math.Abs(oldName.Length - newName.Length) > 2)
        {
            return false;
        }

        var minLength = Math.Min(oldName.Length, newName.Length);
        var prefixEnd = 0;

        while (prefixEnd < minLength && oldName[prefixEnd] == newName[prefixEnd])
        {
            prefixEnd++;
        }

        var oldEnd = oldName.Length - 1;
        var newEnd = newName.Length - 1;

        while (oldEnd >= prefixEnd
               && newEnd >= prefixEnd
               && oldName[oldEnd] == newName[newEnd])
        {
            oldEnd--;
            newEnd--;
        }

        var oldSpanLength = oldEnd - prefixEnd + 1;
        var newSpanLength = newEnd - prefixEnd + 1;

        return oldSpanLength <= 2 && newSpanLength <= 2;
    }

    private static bool IsWholeSerializedFieldIdentifierEdit(
        IReadOnlyList<TextEdit> edits,
        IReadOnlyList<SerializedFieldRename> renames)
    {
        if (edits.Count == 0 || renames.Count == 0)
        {
            return false;
        }

        var expectedRenames = renames
            .Select(rename => (rename.PreviousName, rename.CurrentName))
            .ToHashSet();
        var changedSerializedFieldName = false;

        foreach (var edit in edits)
        {
            var previousName = edit.Range.CopyToString();
            var currentName = edit.Text;

            if (!IsIdentifier(previousName) || !IsIdentifier(currentName))
            {
                return false;
            }

            if (expectedRenames.Contains((previousName, currentName)))
            {
                changedSerializedFieldName = true;
            }
        }

        return changedSerializedFieldName;
    }

    private static bool IsSerializedFieldNumericSuffixRename(
        IReadOnlyList<TextEdit> edits,
        IReadOnlyList<SerializedFieldRename> renames)
    {
        if (renames.Count != 1)
        {
            return false;
        }

        var rename = renames[0];

        if (IsAddedNumericSuffix(rename.PreviousName, rename.CurrentName))
        {
            return true;
        }

        if (TrySplitNumericSuffix(rename.PreviousName, out var previousBaseName, out var previousNumber)
            && TrySplitNumericSuffix(rename.CurrentName, out var currentBaseName, out var currentNumber)
            && previousBaseName == currentBaseName
            && previousNumber != currentNumber)
        {
            return true;
        }

        return false;
    }

    private static bool IsSerializedFieldUnityPrefixCleanupRename(
        IReadOnlyList<SerializedFieldRename> renames)
    {
        if (renames.Count != 1)
        {
            return false;
        }

        var rename = renames[0];

        return TryRemoveUnityPrivatePrefix(rename.PreviousName, out var unprefixedName)
            && rename.CurrentName == unprefixedName;
    }

    private static bool IsAddedNumericSuffix(string previousName, string currentName)
    {
        return currentName.StartsWith(previousName, StringComparison.Ordinal)
            && Regex.IsMatch(currentName[previousName.Length..], @"^_\d+$");
    }

    private static bool TryRemoveUnityPrivatePrefix(string name, out string unprefixedName)
    {
        if (name.StartsWith("m_", StringComparison.Ordinal) && name.Length > 2)
        {
            unprefixedName = name[2..];
            return true;
        }

        unprefixedName = string.Empty;
        return false;
    }

    private static bool TrySplitNumericSuffix(string name, out string baseName, out string number)
    {
        var match = Regex.Match(name, @"^(?<base>.+_)(?<number>\d+)$");

        if (!match.Success)
        {
            baseName = string.Empty;
            number = string.Empty;
            return false;
        }

        baseName = match.Groups["base"].Value;
        number = match.Groups["number"].Value;
        return true;
    }

    private static bool IsIdentifier(string text)
    {
        return Regex.IsMatch(text, @"^@?[A-Za-z_]\w*$");
    }

    private static PendingRenameOperation QueuePendingRename(
        string documentKey,
        string baselineText,
        string renameSignature,
        bool isBulkEdit,
        bool isRenameCommandEdit)
    {
        return PendingRenameOperations.AddOrUpdate(
            documentKey,
            _ => new PendingRenameOperation(baselineText, 1, renameSignature, 1, isBulkEdit, isRenameCommandEdit),
            (_, operation) => operation.RenameSignature == renameSignature
                ? operation with
                {
                    Version = operation.Version + 1,
                    SeenCount = operation.SeenCount + 1,
                    IsBulkEdit = operation.IsBulkEdit || isBulkEdit,
                    IsRenameCommandEdit = operation.IsRenameCommandEdit || isRenameCommandEdit
                }
                : new PendingRenameOperation(baselineText, operation.Version + 1, renameSignature, 1, isBulkEdit, isRenameCommandEdit));
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UnitySerializedShield",
                "VisualStudioExtension.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break editor behavior.
        }
    }

    private static string BuildRenameSignature(IReadOnlyList<SerializedFieldRename> renames)
    {
        return string.Join("|", renames.Select(rename =>
            $"{rename.PreviousName}->{rename.CurrentName}"));
    }

    private async Task ApplyPendingRenameAsync(
        ITextViewSnapshot textView,
        string documentKey,
        int operationVersion)
    {
        try
        {
            if (!PendingRenameOperations.TryGetValue(documentKey, out var operation)
                || operation.Version != operationVersion)
            {
                WriteDiagnostic($"{documentKey}: skipped stale pending rename v{operationVersion}.");
                return;
            }

            var applyDelayMilliseconds = operation.IsRenameCommandEdit
                ? GetRenameCommandApplyDelayMilliseconds(operation.RenameSignature)
                : RenameSettleDelayMilliseconds;
            await Task.Delay(applyDelayMilliseconds);

            if (!PendingRenameOperations.TryGetValue(documentKey, out operation)
                || operation.Version != operationVersion)
            {
                WriteDiagnostic($"{documentKey}: skipped stale pending rename v{operationVersion} after delay.");
                return;
            }

            var currentText = textView.Document.Text.CopyToString();
            var renames = FormerlySerializedAsBuilder.FindRenamedSerializedFields(operation.BaselineText, currentText);

            if (!operation.IsRenameCommandEdit)
            {
                PendingRenameOperations.TryRemove(documentKey, out _);
                DocumentSnapshots[documentKey] = currentText;
                lastChangeSummary = "Ignored serialized field name edit that was not a Rename Symbol identifier replacement.";
                WriteDiagnostic($"{documentKey}: ignored non-rename-command edit v{operationVersion}. Signature: {operation.RenameSignature}");
                return;
            }

            if (renames.Count == 0)
            {
                DocumentSnapshots[documentKey] = currentText;
                PendingRenameOperations.TryRemove(documentKey, out _);
                lastChangeSummary = "Pending rename no longer matched the final editor text.";
                WriteDiagnostic($"{documentKey}: pending rename no longer matched final text.");
                return;
            }

            var insertions = FormerlySerializedAsBuilder.Build(operation.BaselineText, currentText);
            var removals = FormerlySerializedAsBuilder.BuildSelfAttributeRemovals(currentText);
            lastChangeSummary = $"Serialized rename confirmed (bulk={operation.IsBulkEdit}, renameEdit={operation.IsRenameCommandEdit}, seen={operation.SeenCount}). Insertions: {insertions.Count}, Removals: {removals.Count}.";
            WriteDiagnostic($"{documentKey}: applying pending rename v{operationVersion}. Bulk: {operation.IsBulkEdit}. Rename edit: {operation.IsRenameCommandEdit}. Insertions: {insertions.Count}. Removals: {removals.Count}. Renames: {string.Join(", ", renames.Select(rename => $"{rename.PreviousName}->{rename.CurrentName}"))}");

            if (insertions.Count == 0 && removals.Count == 0)
            {
                PendingRenameOperations.TryRemove(documentKey, out _);
                DocumentSnapshots[documentKey] = currentText;
                WriteDiagnostic($"{documentKey}: no edits needed.");
                return;
            }

            await ApplyEditsAsync(textView, documentKey, operation.BaselineText, currentText, insertions, removals);
            PendingRenameOperations.TryRemove(documentKey, out _);
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"{documentKey}: failed to apply pending rename. {exception}");
        }
    }

    private async Task ApplyEditsAsync(
        ITextViewSnapshot textView,
        string documentKey,
        string baselineText,
        string currentText,
        IReadOnlyList<TextInsertion> insertions,
        IReadOnlyList<TextRemoval> removals)
    {
        DocumentsBeingUpdated[documentKey] = 0;

        try
        {
            var editResponse = await this.Extensibility.Editor().EditAsync(editBatch =>
            {
                var editor = textView.Document.AsEditable(editBatch);

                var adjustedInsertions = new List<TextInsertion>();
                foreach (var insertion in insertions)
                {
                    var offset = insertion.Offset;
                    foreach (var removal in removals)
                    {
                        if (removal.Offset == offset)
                        {
                            offset = removal.Offset + removal.Length;
                            break;
                        }
                    }
                    adjustedInsertions.Add(new TextInsertion(offset, insertion.Text));
                }

                foreach (var removal in removals.OrderByDescending(removal => removal.Offset))
                {
                    editor.Replace(new TextRange(textView.Document, removal.Offset, removal.Length), string.Empty);
                }

                foreach (var insertion in adjustedInsertions.OrderByDescending(insertion => insertion.Offset))
                {
                    editor.Insert(insertion.Offset, insertion.Text);
                }
            }, CancellationToken.None);

            var updatedText = FormerlySerializedAsBuilder.ApplyEdits(currentText, removals, insertions);

            if (editResponse.DocumentEditResults is not null
                && editResponse.DocumentEditResults.TryGetValue(textView.Document, out var documentEditResult)
                && documentEditResult.After is not null)
            {
                updatedText = documentEditResult.After.Text.CopyToString();
            }

            DocumentSnapshots[documentKey] = updatedText;
            LastAppliedEditTimes[documentKey] = DateTimeOffset.UtcNow;
            Interlocked.Increment(ref protectedRenameCount);
            await SaveDocumentAsync(textView, documentKey);
            lastChangeSummary = $"Applied {insertions.Count} insertion(s) and {removals.Count} cleanup removal(s) and saved the document.";
            WriteDiagnostic($"{documentKey}: applied {insertions.Count} insertion(s) and {removals.Count} cleanup removal(s) and saved the document.");
            _ = VerifySavedMigrationAsync(textView, documentKey, baselineText);
        }
        finally
        {
            DocumentsBeingUpdated.TryRemove(documentKey, out _);
        }
    }

    private static int GetRenameCommandApplyDelayMilliseconds(string renameSignature)
    {
        return ContainsPrefixRename(renameSignature)
            ? PrefixRenameApplyDelayMilliseconds
            : RenameCommandApplyDelayMilliseconds;
    }

    private static bool ContainsPrefixRename(string renameSignature)
    {
        foreach (var rename in renameSignature.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = rename.IndexOf("->", StringComparison.Ordinal);

            if (separatorIndex <= 0)
            {
                continue;
            }

            var previousName = rename[..separatorIndex];
            var currentName = rename[(separatorIndex + 2)..];

            if (currentName.Length < previousName.Length
                && previousName.StartsWith(currentName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task VerifySavedMigrationAsync(
        ITextViewSnapshot textView,
        string documentKey,
        string baselineText)
    {
        try
        {
            await Task.Delay(PostInsertVerificationDelayMilliseconds);

            var documentUri = GetDocumentUri(textView);

            if (documentUri is null)
            {
                WriteDiagnostic($"{documentKey}: skipped post-insert verification because document URI was unavailable.");
                return;
            }

            var latestDocument = await this.Extensibility.Documents().OpenTextDocumentAsync(documentUri, CancellationToken.None);
            var latestText = latestDocument.Text.CopyToString();
            var missingInsertions = FormerlySerializedAsBuilder.Build(baselineText, latestText);
            var selfRemovals = FormerlySerializedAsBuilder.BuildSelfAttributeRemovals(latestText);

            if (missingInsertions.Count == 0 && selfRemovals.Count == 0)
            {
                DocumentSnapshots[documentKey] = latestText;
                WriteDiagnostic($"{documentKey}: post-insert verification passed.");
                return;
            }

            DocumentsBeingUpdated[documentKey] = 0;

            try
            {
                var editResponse = await this.Extensibility.Editor().EditAsync(editBatch =>
                {
                    var editor = latestDocument.AsEditable(editBatch);

                    var adjustedInsertions = new List<TextInsertion>();
                    foreach (var insertion in missingInsertions)
                    {
                        var offset = insertion.Offset;
                        foreach (var removal in selfRemovals)
                        {
                            if (removal.Offset == offset)
                            {
                                offset = removal.Offset + removal.Length;
                                break;
                            }
                        }
                        adjustedInsertions.Add(new TextInsertion(offset, insertion.Text));
                    }

                    foreach (var removal in selfRemovals.OrderByDescending(removal => removal.Offset))
                    {
                        editor.Replace(new TextRange(latestDocument, removal.Offset, removal.Length), string.Empty);
                    }

                    foreach (var insertion in adjustedInsertions.OrderByDescending(insertion => insertion.Offset))
                    {
                        editor.Insert(insertion.Offset, insertion.Text);
                    }
                }, CancellationToken.None);

                var repairedText = FormerlySerializedAsBuilder.ApplyEdits(latestText, selfRemovals, missingInsertions);

                if (editResponse.DocumentEditResults is not null
                    && editResponse.DocumentEditResults.TryGetValue(latestDocument, out var documentEditResult)
                    && documentEditResult.After is not null)
                {
                    repairedText = documentEditResult.After.Text.CopyToString();
                }

                DocumentSnapshots[documentKey] = repairedText;
                LastAppliedEditTimes[documentKey] = DateTimeOffset.UtcNow;
                await SaveDocumentAsync(documentUri, documentKey);
                lastChangeSummary = $"Repaired {missingInsertions.Count} missing insertion(s) and {selfRemovals.Count} removal(s) after Visual Studio rename settled.";
                WriteDiagnostic($"{documentKey}: repaired {missingInsertions.Count} missing insertion(s) and {selfRemovals.Count} removal(s) after post-insert verification.");
            }
            finally
            {
                DocumentsBeingUpdated.TryRemove(documentKey, out _);
            }
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"{documentKey}: post-insert verification failed. {exception}");
        }
    }

    private async Task SaveDocumentAsync(ITextViewSnapshot textView, string documentKey)
    {
        var documentUri = GetDocumentUri(textView);

        if (documentUri is null)
        {
            WriteDiagnostic($"{documentKey}: skipped save because document URI was unavailable.");
            return;
        }

        await SaveDocumentAsync(documentUri, documentKey);
    }

    private async Task SaveDocumentAsync(Uri documentUri, string documentKey)
    {
        await this.Extensibility.Documents().SaveDocumentAsync(documentUri, CancellationToken.None);
        WriteDiagnostic($"{documentKey}: saved document {documentUri}.");
    }

    private static Uri? GetDocumentUri(ITextViewSnapshot textView)
    {
        if (textView.Uri is not null)
        {
            return textView.Uri;
        }

        return string.IsNullOrWhiteSpace(textView.FilePath)
            ? null
            : new Uri(textView.FilePath);
    }

    private sealed record PendingRenameOperation(
        string BaselineText,
        int Version,
        string RenameSignature,
        int SeenCount,
        bool IsBulkEdit,
        bool IsRenameCommandEdit);
}
