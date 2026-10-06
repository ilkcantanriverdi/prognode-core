namespace Prognode.Protocols.Abstractions;

public sealed record OpcUaCertificateInfo(string EndpointUrl, string Subject,
    string Sha256, string Sha1, DateTimeOffset ValidFrom, DateTimeOffset ValidUntil,
    bool Trusted, string SecurityPolicy);

public interface IOpcUaCertificateApproval
{
    Task<OpcUaCertificateInfo> InspectAsync(string endpointUrl, CancellationToken cancellationToken);
    Task<OpcUaCertificateInfo> TrustAsync(string endpointUrl, string expectedSha256,
        CancellationToken cancellationToken);
}
