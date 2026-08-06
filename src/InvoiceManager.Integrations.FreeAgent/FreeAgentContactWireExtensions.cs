using InvoiceManager.Core.Integrations.FreeAgent;

namespace InvoiceManager.Integrations.FreeAgent;

internal static class ContactWireExtensions
{
    extension(ContactWire wire)
    {
        public FreeAgentContact ToContact()
        {
            var url = wire.Url ?? throw new InvalidOperationException("FreeAgent contact is missing its url.");
            return new FreeAgentContact(new FreeAgentContactIdentity(url), wire.DisplayName());
        }

        private string DisplayName()
        {
            if (wire.OrganisationName is { Length: > 0 } organisationName)
                return organisationName;

            var personName = $"{wire.FirstName} {wire.LastName}".Trim();
            if (personName.Length > 0)
                return personName;

            return wire.Url?.Segments[^1] ?? "Unknown contact";
        }
    }
}
