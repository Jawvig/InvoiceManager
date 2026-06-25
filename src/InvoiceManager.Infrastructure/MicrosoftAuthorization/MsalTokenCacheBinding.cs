using Microsoft.Identity.Client;

namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

public static class MsalTokenCacheBinding
{
    public static void Bind(ITokenCache tokenCache, IMicrosoftAuthorizationStore authorizationStore)
    {
        tokenCache.SetBeforeAccessAsync(async args =>
        {
            var tokenCacheBytes = await authorizationStore.ReadTokenCacheAsync();
            if (tokenCacheBytes is { Length: > 0 })
            {
                args.TokenCache.DeserializeMsalV3(tokenCacheBytes);
            }
        });

        tokenCache.SetAfterAccessAsync(async args =>
        {
            if (!args.HasStateChanged)
            {
                return;
            }

            var tokenCacheBytes = args.TokenCache.SerializeMsalV3();
            if (tokenCacheBytes.Length > 0)
            {
                await authorizationStore.SaveTokenCacheAsync(tokenCacheBytes);
            }
        });
    }
}
