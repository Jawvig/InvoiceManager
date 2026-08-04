using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;

namespace InvoiceManager.TestSupport;

public sealed class FakeFreeAgentAttachmentUploader : IFreeAgentAttachmentUploader
{
    public Func<FreeAgentBillIdentity, byte[], string, FreeAgentAttachmentResult>? Upload { get; set; }

    public Task<FreeAgentAttachmentResult> UploadAsync(
        FreeAgentBillIdentity bill,
        byte[] pdfContent,
        string fileName,
        Option<FreeAgentAttachmentMetadata> expectedExisting,
        CancellationToken cancellationToken = default)
    {
        var result = Upload?.Invoke(bill, pdfContent, fileName)
            ?? throw new InvalidOperationException("FakeFreeAgentAttachmentUploader.Upload was not configured.");
        return Task.FromResult(result);
    }
}
