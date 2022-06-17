using System;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using RestSharp;
using RestSharp.Authenticators;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer
{
    public class AlteonLoadBalancerClient
    {
        private RestClient _restClient { get; set; }
        ILogger logger = LogHandler.GetClassLogger<AlteonLoadBalancerClient>();

        public AlteonLoadBalancerClient(string baseUrl, string username, string password)
        {
            _restClient = new RestClient(baseUrl);
            _restClient.Authenticator = new HttpBasicAuthenticator(username, password);
        }

        public async Task<CertificateTableEntryCollection> GetCertificates()
        {
            var request = new RestRequest("SlbNewSslCfgCertsTable", Method.Get);
            try
            {
                var response = await _restClient.ExecuteAsync<CertificateTableEntryCollection>(request);
                return response.Data;
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
        }        
    }
}
