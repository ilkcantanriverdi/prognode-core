using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Prognode.Protocols.Abstractions;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Prognode.Protocols.OpcUa;

public sealed class OpcUaConnectionFactory(string dataRoot) : IOpcUaCertificateApproval
{
    private readonly SemaphoreSlim _initialize = new(1, 1);
    private readonly ITelemetryContext _telemetry = DefaultTelemetry.Create(_ => { });
    private ApplicationConfiguration? _configuration;

    public async Task<OpcUaCertificateInfo> InspectAsync(string endpointUrl,
        CancellationToken cancellationToken)
    {
        var description = await SelectSupportedEndpointAsync(endpointUrl, cancellationToken);
        using var certificate = X509CertificateLoader.LoadCertificate(description.ServerCertificate);
        return CertificateInfo(endpointUrl, description, certificate);
    }

    public async Task<OpcUaCertificateInfo> TrustAsync(string endpointUrl,
        string expectedSha256, CancellationToken cancellationToken)
    {
        var description = await SelectSupportedEndpointAsync(endpointUrl, cancellationToken);
        using var certificate = X509CertificateLoader.LoadCertificate(description.ServerCertificate);
        var actual = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        if (!string.Equals(actual, expectedSha256?.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OPC UA server certificate changed. Inspect it again before approval.");
        var now = DateTimeOffset.UtcNow;
        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
            throw new InvalidOperationException("OPC UA server certificate is outside its validity period.");
        var trusted = Path.Combine(dataRoot, "opc-ua", "pki", "trusted", "certs");
        Directory.CreateDirectory(trusted);
        var path = Path.Combine(trusted, $"Approved OPC UA server [{actual}].der");
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, certificate.Export(X509ContentType.Cert), cancellationToken);
        var configuration = await GetConfigurationAsync(cancellationToken);
        await configuration.CertificateValidator.UpdateAsync(configuration, cancellationToken);
        return CertificateInfo(endpointUrl, description, certificate);
    }

    private OpcUaCertificateInfo CertificateInfo(string endpointUrl,
        EndpointDescription description, X509Certificate2 certificate)
    {
        var sha256 = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var trusted = Path.Combine(dataRoot, "opc-ua", "pki", "trusted", "certs");
        var isTrusted = Directory.Exists(trusted) && Directory.EnumerateFiles(trusted, "*.der")
            .Any(path => HasFingerprint(path, sha256));
        return new(OpcUaEndpoint.Parse(endpointUrl), certificate.Subject, sha256,
            certificate.Thumbprint, certificate.NotBefore, certificate.NotAfter,
            isTrusted, description.SecurityPolicyUri);
    }

    private static bool HasFingerprint(string path, string expected)
    {
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
            return string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256),
                expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (CryptographicException) { return false; }
    }

    public async Task<ISession> OpenAsync(string? endpointUrl, CancellationToken cancellationToken)
    {
        var url = OpcUaEndpoint.Parse(endpointUrl);
        var configuration = await GetConfigurationAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        var description = await SelectSupportedEndpointAsync(url, timeout.Token);
        var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(configuration));
        var factory = new DefaultSessionFactory(_telemetry);
        return await factory.CreateAsync(configuration, endpoint, false, true,
            "ProgNode Core", 60_000, new UserIdentity(), default, timeout.Token);
    }

    private async Task<EndpointDescription> SelectSupportedEndpointAsync(string endpointUrl,
        CancellationToken cancellationToken)
    {
        var url = OpcUaEndpoint.Parse(endpointUrl);
        var configuration = await GetConfigurationAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        var description = await CoreClientUtils.SelectEndpointAsync(
            configuration, url, true, 60_000, _telemetry, timeout.Token);
        if (description is null || description.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
            description.SecurityPolicyUri is not (SecurityPolicies.Basic256Sha256 or
                SecurityPolicies.Aes128_Sha256_RsaOaep or SecurityPolicies.Aes256_Sha256_RsaPss))
            throw new InvalidOperationException("OPC UA server has no supported SignAndEncrypt endpoint.");
        return description;
    }

    private async Task<ApplicationConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        if (_configuration is not null) return _configuration;
        await _initialize.WaitAsync(cancellationToken);
        try
        {
            if (_configuration is not null) return _configuration;
            var pki = Path.Combine(dataRoot, "opc-ua", "pki");
            var configuration = new ApplicationConfiguration
            {
                ApplicationName = "ProgNode Core",
                ApplicationUri = $"urn:{System.Net.Dns.GetHostName()}:ProgNode:Core",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = "Directory",
                        StorePath = Path.Combine(pki, "own"),
                        SubjectName = "CN=ProgNode Core"
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory", StorePath = Path.Combine(pki, "trusted")
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory", StorePath = Path.Combine(pki, "issuers")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = "Directory", StorePath = Path.Combine(pki, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = false,
                    RejectSHA1SignedCertificates = true,
                    MinimumCertificateKeySize = 2048,
                    AddAppCertToTrustedStore = false
                },
                TransportQuotas = new TransportQuotas { OperationTimeout = 60_000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60_000 }
            };
            await configuration.ValidateAsync(ApplicationType.Client, cancellationToken);
            var application = new ApplicationInstance(configuration, _telemetry);
            if (!await application.CheckApplicationInstanceCertificatesAsync(silent: true, ct: cancellationToken))
                throw new InvalidOperationException("OPC UA application certificate could not be created or validated.");
            _configuration = configuration;
            return configuration;
        }
        finally { _initialize.Release(); }
    }
}
