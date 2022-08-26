using System;
using System.IO;
using System.Net;
using System.Text;
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
            var options = new RestClientOptions(baseUrl)
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            };
            _restClient = new RestClient(options);

            _restClient.Authenticator = new HttpBasicAuthenticator(username, password);
        }

        public async Task<CertificateTableEntryCollection> GetCertificates()
        {
            var request = new RestRequest(Endpoints.CertificateRepository);
            try
            {
                var response = await _restClient.GetAsync<CertificateTableEntryCollection>(request);
                return response;
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
        }

        public string GetCertificateContent(string certId)
        {
            var request = new RestRequest(Endpoints.CertificateContent);
            request.AddQueryParameter("id", certId);
            request.AddQueryParameter("type", "srvcrt");
            try
            {
                var response = _restClient.DownloadData(request);
                var sr = new StreamReader(new MemoryStream(response), Encoding.UTF8);
                var content = sr.ReadToEnd();
                return content;
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
        }

        public async Task AddCertificate(string alias, string pfxPassword, string certContents, string type)
        {
            var request = new RestRequest(Endpoints.AddCertificate, Method.Post);
            request.AddQueryParameter("id", alias);
            request.AddQueryParameter("type", type);
            request.AddQueryParameter("passphrase", pfxPassword);
            request.AddQueryParameter("src", "txt");

            request.AddBody(certContents);

            try
            {
                var response = await _restClient.PostAsync(request);
                if (!response.IsSuccessful) {
                    throw new Exception($"Failed to add certificate: {alias}", response.ErrorException);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
        }
    }
}
