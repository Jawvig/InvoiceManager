using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;
using InvoiceManager.TestSupport;
using NodaMoney;

namespace InvoiceManager.Integrations.FreeAgent.Tests;

public sealed class FreeAgentGuessRemoverTests
{
    private const string BillUrl = "https://api.sandbox.freeagent.com/v2/bills/1";
    private const string ItemUrl = "https://api.sandbox.freeagent.com/v2/bill_items/1";
    private const string BankAccountUrl = "https://api.sandbox.freeagent.com/v2/bank_accounts/1";
    private const string ExplanationUrl = "https://api.sandbox.freeagent.com/v2/bank_transaction_explanations/1";
    private const string BankTransactionUrl = "https://api.sandbox.freeagent.com/v2/bank_transactions/1";

    [Fact]
    public async Task RemoveConfirmedGuessAsync_RevalidationFails_WhenExplanationNoLongerMatches_AndNothingIsDeleted()
    {
        var handler = new StubHttpMessageHandler((request, index) =>
            index switch
            {
                0 => JsonResponse(BankAccountsJson()),
                _ => JsonResponse(ExplanationsJson([])), // no matching explanation anymore
            });
        var client = TestClientFactory.Create(handler);
        var reconciler = new FreeAgentBillReconciler(client);
        var remover = new FreeAgentGuessRemover(client, reconciler);

        var intervention = BuildIntervention();
        var result = await remover.RemoveConfirmedGuessAsync(intervention);

        Assert.True(result is FreeAgentGuessRevalidationFailed, $"Expected FreeAgentGuessRevalidationFailed but got {result}.");
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task RemoveConfirmedGuessAsync_RevalidationFails_WhenExplanationIsNoLongerDeletable()
    {
        var handler = new StubHttpMessageHandler((request, index) =>
            index switch
            {
                0 => JsonResponse(BankAccountsJson(BankAccountUrl)),
                1 => JsonResponse(ExplanationsJson(isDeletable: false, isLocked: false)),
                _ => throw new InvalidOperationException("Nothing should have been deleted."),
            });
        var client = TestClientFactory.Create(handler);
        var reconciler = new FreeAgentBillReconciler(client);
        var remover = new FreeAgentGuessRemover(client, reconciler);

        var result = await remover.RemoveConfirmedGuessAsync(BuildIntervention());

        Assert.True(result is FreeAgentGuessRevalidationFailed, $"Expected FreeAgentGuessRevalidationFailed but got {result}.");
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task RemoveConfirmedGuessAsync_DeletesAndRetries_WhenAllPreconditionsHoldAndTransactionBecomesUnexplained()
    {
        var handler = new StubHttpMessageHandler((request, index) =>
            index switch
            {
                0 => JsonResponse(BankAccountsJson(BankAccountUrl)),
                1 => JsonResponse(ExplanationsJson(isDeletable: true, isLocked: false)),
                2 => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent), // DELETE explanation
                3 => JsonResponse(BankTransactionJson(explained: false)),
                4 => JsonResponse(BillJson()), // reconciler's GET for item-ownership check
                5 => JsonResponse(BillJson(totalValue: "100.00")), // successful PUT response
                _ => throw new InvalidOperationException("Unexpected request."),
            });
        var client = TestClientFactory.Create(handler);
        var reconciler = new FreeAgentBillReconciler(client);
        var remover = new FreeAgentGuessRemover(client, reconciler);

        var result = await remover.RemoveConfirmedGuessAsync(BuildIntervention());

        Assert.True(result is FreeAgentGuessRemoved, $"Expected FreeAgentGuessRemoved but got {result}.");
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task RemoveConfirmedGuessAsync_ReportsRetryFailed_WhenTransactionStillExplainedAfterDeletion()
    {
        var handler = new StubHttpMessageHandler((request, index) =>
            index switch
            {
                0 => JsonResponse(BankAccountsJson(BankAccountUrl)),
                1 => JsonResponse(ExplanationsJson(isDeletable: true, isLocked: false)),
                2 => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent),
                _ => JsonResponse(BankTransactionJson(explained: true)),
            });
        var client = TestClientFactory.Create(handler);
        var reconciler = new FreeAgentBillReconciler(client);
        var remover = new FreeAgentGuessRemover(client, reconciler);

        var result = await remover.RemoveConfirmedGuessAsync(BuildIntervention());

        Assert.True(result is FreeAgentGuessRemovalRetryFailed, $"Expected FreeAgentGuessRemovalRetryFailed but got {result}.");
    }

    private static FreeAgentGuessIntervention BuildIntervention() =>
        new(
            new FreeAgentInterventionId("freeagent-intervention-test"),
            new InvoiceRecordId("config-1_2026-08-01"),
            new FreeAgentBillIdentity(BillUrl),
            new FreeAgentBillItemIdentity(ItemUrl),
            BankTransactionUrl,
            ExplanationUrl,
            new Money(121.00m, "GBP"),
            new Money(100.00m, "GBP"),
            "Test intervention",
            DateTimeOffset.UtcNow,
            FreeAgentGuessInterventionStatus.Approved);

    private static string BankAccountsJson(params string[] urls) =>
        $$"""{"bank_accounts": [{{string.Join(",", urls.Select(u => $$"""{"url": "{{u}}"}"""))}}]}""";

    private static string ExplanationsJson(bool isDeletable, bool isLocked) =>
        $$"""
        {"bank_transaction_explanations": [{
          "url": "{{ExplanationUrl}}",
          "paid_bill": "{{BillUrl}}",
          "bank_transaction": "{{BankTransactionUrl}}",
          "marked_for_review": true,
          "is_locked": {{isLocked.ToString().ToLowerInvariant()}},
          "is_deletable": {{isDeletable.ToString().ToLowerInvariant()}}
        }]}
        """;

    private static string ExplanationsJson(IReadOnlyList<string> _) => """{"bank_transaction_explanations": []}""";

    private static string BankTransactionJson(bool explained)
    {
        var explanations = explained ? $"\"{ExplanationUrl}\"" : "";
        return $$"""
        {
          "bank_transaction": {
            "url": "{{BankTransactionUrl}}",
            "unexplained_amount": "0.00",
            "bank_transaction_explanations": [{{explanations}}]
          }
        }
        """;
    }

    private static string BillJson(string totalValue = "121.00")
    {
        var item = $$"""{"url": "{{ItemUrl}}", "bill": "{{BillUrl}}", "description": "Item", "total_value": "{{totalValue}}"}""";
        return $$"""
        {
          "bill": {
            "url": "{{BillUrl}}",
            "contact": "https://api.sandbox.freeagent.com/v2/contacts/1",
            "reference": "REF-1",
            "dated_on": "2026-08-01",
            "due_on": "2026-08-30",
            "currency": "GBP",
            "total_value": "{{totalValue}}",
            "paid_value": "0.00",
            "due_value": "{{totalValue}}",
            "status": "Open",
            "bill_items": [{{item}}]
          }
        }
        """;
    }

    private static HttpResponseMessage JsonResponse(string json) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };
}
