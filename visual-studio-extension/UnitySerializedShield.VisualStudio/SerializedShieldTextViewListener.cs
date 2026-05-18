using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, string> DocumentSnapshots = new();
    private static readonly ConcurrentDictionary<string, byte> DocumentsBeingUpdated = new();
    private static int openedCount;
    private static int changedCount;
    private static int protectedRenameCount;
    private static string lastDocumentKey = "none";
    private static string lastChangeSummary = "No text view changes observed yet.";

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
        if (TryGetCSharpDocumentKey(textView, out var documentKey))
        {
            Interlocked.Increment(ref openedCount);
            lastDocumentKey = documentKey;
            DocumentSnapshots[documentKey] = textView.Document.Text.CopyToString();
        }

        return Task.CompletedTask;
    }

    public Task TextViewClosedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken)
    {
        if (TryGetCSharpDocumentKey(textView, out var documentKey))
        {
            DocumentSnapshots.TryRemove(documentKey, out _);
            DocumentsBeingUpdated.TryRemove(documentKey, out _);
        }

        return Task.CompletedTask;
    }

    public async Task TextViewChangedAsync(TextViewChangedArgs args, CancellationToken cancellationToken)
    {
        var textView = args.AfterTextView;

        if (!TryGetCSharpDocumentKey(textView, out var documentKey))
        {
            return;
        }

        Interlocked.Increment(ref changedCount);
        lastDocumentKey = documentKey;

        var currentText = textView.Document.Text.CopyToString();

        if (DocumentsBeingUpdated.ContainsKey(documentKey))
        {
            DocumentSnapshots[documentKey] = currentText;
            lastChangeSummary = "Ignored extension-authored edit.";
            return;
        }

        var previousText = args.BeforeTextView.Document.Text.CopyToString();
        DocumentSnapshots[documentKey] = currentText;

        if (previousText is null || previousText == currentText)
        {
            lastChangeSummary = "Change observed, but previous/current text were equivalent.";
            return;
        }

        var insertions = FormerlySerializedAsBuilder.Build(previousText, currentText);
        lastChangeSummary = $"Change observed. Insertions needed: {insertions.Count}.";

        if (insertions.Count == 0)
        {
            return;
        }

        DocumentsBeingUpdated[documentKey] = 0;

        try
        {
            var editResponse = await this.Extensibility.Editor().EditAsync(editBatch =>
            {
                var editor = textView.Document.AsEditable(editBatch);

                foreach (var insertion in insertions.OrderByDescending(insertion => insertion.Offset))
                {
                    editor.Insert(insertion.Offset, insertion.Text);
                }
            }, cancellationToken);

            var updatedText = FormerlySerializedAsBuilder.ApplyInsertions(currentText, insertions);

            if (editResponse.DocumentEditResults is not null
                && editResponse.DocumentEditResults.TryGetValue(textView.Document, out var documentEditResult)
                && documentEditResult.After is not null)
            {
                updatedText = documentEditResult.After.Text.CopyToString();
            }

            DocumentSnapshots[documentKey] = updatedText;
            Interlocked.Increment(ref protectedRenameCount);
            lastChangeSummary = $"Applied {insertions.Count} insertion(s).";
        }
        finally
        {
            DocumentsBeingUpdated.TryRemove(documentKey, out _);
        }
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
}
