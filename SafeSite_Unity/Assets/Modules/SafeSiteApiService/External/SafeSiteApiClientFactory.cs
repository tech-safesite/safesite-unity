using System;

namespace Modules.SafeSiteApiService.External
{
    /// <summary>
    /// Creates <see cref="ISafeSiteApiClient"/>s without a scene object (editor tools, CLI).
    /// The Internal assembly registers the implementation on load, so callers only ever compile
    /// against External.
    /// </summary>
    public static class SafeSiteApiClientFactory
    {
        private static Func<SafeSiteEnvironment, ISecretStore, ISafeSiteApiClient> _create;

        public static bool IsRegistered => _create != null;

        /// <summary>Called by Modules.SafeSiteApiService.Internal. Not for feature code.</summary>
        public static void Register(Func<SafeSiteEnvironment, ISecretStore, ISafeSiteApiClient> create) => _create = create;

        /// <param name="secrets">Where the admin refresh token is kept; null keeps it in memory only.</param>
        public static ISafeSiteApiClient Create(SafeSiteEnvironment environment, ISecretStore secrets = null)
        {
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (_create == null)
                throw new InvalidOperationException("[SafeSiteApiService] No client implementation registered. Is Modules.SafeSiteApiService.Internal compiled into this build?");
            return _create(environment, secrets);
        }
    }
}
