using Rehear.Evc.Contracts;

namespace Rehear.Evc.Transport
{
    public static class EvcErrorMapper
    {
        public static EvcErrorKind Map(long status, bool connectionError, bool timedOut)
        {
            if (timedOut)
                return EvcErrorKind.Timeout;
            if (connectionError)
                return EvcErrorKind.Network;

            switch (status)
            {
                case 401: return EvcErrorKind.Unauthorized;
                case 404: return EvcErrorKind.NotFound;
                case 409: return EvcErrorKind.Conflict;
                case 413: return EvcErrorKind.PayloadTooLarge;
                case 415: return EvcErrorKind.UnsupportedMediaType;
                case 422: return EvcErrorKind.Validation;
                case 429: return EvcErrorKind.RateLimited;
                case 502: return EvcErrorKind.ProviderUnavailable;
                default:
                    return status >= 500 && status <= 599
                        ? EvcErrorKind.ServerError
                        : EvcErrorKind.Unknown;
            }
        }
    }
}
