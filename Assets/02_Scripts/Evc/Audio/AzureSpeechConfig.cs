using System;
using UnityEngine;

namespace Rehear.Evc.Audio
{
    [CreateAssetMenu(menuName = "Rehear/Azure Speech", fileName = "AzureSpeechConfig")]
    public sealed class AzureSpeechConfig : ScriptableObject
    {
        [Tooltip("Deployed speech bridge HTTPS base URL. Never put an Azure key here.")]
        public string bridgeBaseUrl = "";
#if UNITY_EDITOR || REHEAR_SPEECH_SMOKE
        // In-memory loopback override for explicit diagnostics only; absent from ordinary APKs.
        public static string EditorLoopbackBaseUrl { get; set; }
#endif

        public string BaseUrl
        {
            get
            {
#if UNITY_EDITOR || REHEAR_SPEECH_SMOKE
                if (!string.IsNullOrEmpty(EditorLoopbackBaseUrl))
                {
                    if (!Uri.TryCreate(EditorLoopbackBaseUrl, UriKind.Absolute, out var local) ||
                        local.Scheme != Uri.UriSchemeHttp || local.Host != "127.0.0.1" ||
                        !string.IsNullOrEmpty(local.UserInfo) || !string.IsNullOrEmpty(local.Query) || !string.IsNullOrEmpty(local.Fragment))
                        throw new InvalidOperationException("Editor speech test must use an explicit loopback URL.");
                    return EditorLoopbackBaseUrl.TrimEnd('/');
                }
#endif
                var value = (bridgeBaseUrl ?? "").Trim().TrimEnd('/');
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
                    !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                    throw new InvalidOperationException("Azure 음성 서버 HTTPS 주소를 설정해주세요.");
                return value;
            }
        }
    }
}
