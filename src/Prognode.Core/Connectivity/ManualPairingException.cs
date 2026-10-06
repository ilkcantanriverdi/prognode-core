namespace Prognode.Core.Connectivity;
public sealed class ManualPairingException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
