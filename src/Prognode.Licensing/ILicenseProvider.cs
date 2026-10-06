using Prognode.Contracts.Licensing;

namespace Prognode.Licensing;

public interface ILicenseProvider
{
    LicenseSnapshot GetCurrent();
}
