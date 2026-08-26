using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Rehear.Evc.Data
{
    public interface IRemoteSlideLoader
    {
        Task<Texture2D> LoadAsync(string url, int timeoutSeconds, CancellationToken cancellationToken);
    }

    public sealed class RemoteSlideLoader : IRemoteSlideLoader
    {
        private static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        public async Task<Texture2D> LoadAsync(
            string url,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new ArgumentException("Slide URL must be an absolute HTTP(S) URL.", nameof(url));
            }

            if (Cache.TryGetValue(url, out var cached) && cached != null)
                return cached;

            using (var request = UnityWebRequestTexture.GetTexture(url, true))
            using (cancellationToken.Register(request.Abort))
            {
                request.timeout = Math.Max(1, timeoutSeconds);
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Remote slide download failed.");

                var texture = DownloadHandlerTexture.GetContent(request);
                if (texture == null)
                    throw new InvalidOperationException("Remote slide response was not a supported image.");
                texture.name = "RemoteSlide";
                Cache[url] = texture;
                return texture;
            }
        }
    }
}
