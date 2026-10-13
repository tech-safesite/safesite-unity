using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Modules.SafeSiteApiService.Internal
{
    internal static class UnityWebRequestAsync
    {
        /// <summary>
        /// Sends and waits for completion by polling <c>isDone</c> once per tick of the current
        /// SynchronizationContext. Polling (rather than the <c>completed</c> event) also works in
        /// batch mode, where the CLI pumps its own context and the player loop does not run.
        /// </summary>
        public static async Task SendAsync(this UnityWebRequest request, CancellationToken ct, Action<UnityWebRequest> onTick = null)
        {
            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                if (ct.IsCancellationRequested)
                {
                    request.Abort();
                    throw new OperationCanceledException(ct);
                }

                onTick?.Invoke(request);
                await Task.Yield();
            }

            onTick?.Invoke(request);
        }
    }
}
