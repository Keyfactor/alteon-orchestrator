namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer
{
    public static class Endpoints
    {
        public const string CertificateRepository = "config/SlbNewSslCfgCertsTable"; // HTTP DELETE to remove.
        public const string CertificateContent = "config/getcert";
        public const string AddCertificate = "config/sslcertimport";        
    }
}
