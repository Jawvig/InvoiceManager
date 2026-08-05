using InvoiceManager.Core.Integrations.FreeAgent;

namespace InvoiceManager.Integrations.FreeAgent;

/// <summary>Translates an <see cref="AttachmentWire"/> into the Core-owned <see cref="FreeAgentAttachmentMetadata"/>.</summary>
internal static class AttachmentWireExtensions
{
    extension(AttachmentWire wire)
    {
        public FreeAgentAttachmentMetadata ToAttachmentMetadata() =>
            new(
                wire.FileName ?? string.Empty,
                wire.FileSize,
                wire.ContentType ?? "application/octet-stream",
                DateTimeOffset.UtcNow);
    }
}
