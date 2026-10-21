using System;
using UnityEngine;

namespace Rehear.Evc.Config
{
    public enum EvcEnvironment
    {
        Development,
        Staging,
        Production
    }

    [CreateAssetMenu(fileName = "EvcEnvironmentConfig", menuName = "Rehear/EVC Environment Config")]
    public sealed class EvcEnvironmentConfig : ScriptableObject
    {
        [SerializeField] private EvcEnvironment environment = EvcEnvironment.Development;
        [SerializeField] private string serverBaseUrl = string.Empty;
        [SerializeField, Min(1)] private int timeoutSeconds = 30;
        [SerializeField, Range(0, 5)] private int transientRetryCount = 2;
        [SerializeField] private bool enableDemoFallback;

        public EvcEnvironment Environment => environment;
#if UNITY_EDITOR
        // Session-only opt-in. Never serialized into a scene or included in APKs.
        public static bool UseLocalRecordingServer { get; set; }
#endif
        public string ServerBaseUrl
        {
            get
            {
#if UNITY_EDITOR
                if (UseLocalRecordingServer) return "http://127.0.0.1:8093";
#endif
                return (serverBaseUrl ?? string.Empty).TrimEnd('/');
            }
        }
        public int TimeoutSeconds => Mathf.Max(1, timeoutSeconds);
        public int TransientRetryCount => Mathf.Clamp(transientRetryCount, 0, 5);
        public bool EnableDemoFallback => enableDemoFallback &&
                                          environment == EvcEnvironment.Development &&
                                          Debug.isDebugBuild;

        public bool TryValidate(out string error)
        {
            error = string.Empty;
#if UNITY_EDITOR
            if (UseLocalRecordingServer) return true;
#endif
            if (!Uri.TryCreate(ServerBaseUrl, UriKind.Absolute, out var uri))
            {
                error = "EVC 서버 URL이 설정되지 않았습니다.";
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                error = "EVC 서버 URL은 HTTP(S) 주소여야 합니다.";
                return false;
            }

            if (environment != EvcEnvironment.Development && uri.Scheme != Uri.UriSchemeHttps)
            {
                error = "스테이징/운영 EVC 서버는 HTTPS가 필요합니다.";
                return false;
            }

            return true;
        }

        public string BuildApiUrl(string relativePath)
        {
            if (!TryValidate(out var error))
                throw new InvalidOperationException(error);

            var path = relativePath.StartsWith("/", StringComparison.Ordinal)
                ? relativePath
                : "/" + relativePath;
            return ServerBaseUrl + "/odi/xreal_rehear/evc" + path;
        }
    }
}
