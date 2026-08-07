using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;

namespace InvoiceManager.TestSupport;

public sealed class FakeFreeAgentAttachmentUploader : IFreeAgentAttachmentUploader
{
    public Func<FreeAgentBillIdentity, byte[], string, FreeAgentAttachmentResult>? Upload { get; set; }

    /// <summary>Every <see cref="Option{T}"/> of expected-existing metadata passed to <see cref="UploadAsync"/>, in call order.</summary>
    public List<Option<FreeAgentAttachmentMetadata>> ExpectedExistingRequests { get; } = [];

    public Task<FreeAgentAttachmentResult> UploadAsync(
        FreeAgentBillIdentity bill,
        byte[] pdfContent,
        string fileName,
        Option<FreeAgentAttachmentMetadata> expectedExisting,
        CancellationToken cancellationToken = default)
    {
        ExpectedExistingRequests.Add(expectedExisting);
        var result = Upload?.Invoke(bill, pdfContent, fileName)
            ?? throw new InvalidOperationException("FakeFreeAgentAttachmentUploader.Upload was not configured.");
        return Task.FromResult(result);
    }
}
