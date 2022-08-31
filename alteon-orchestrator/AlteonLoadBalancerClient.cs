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

        public async Task<CertificateTableEntryCollection> GetCertificatesById(string id)
        {
            var url = $"{Endpoints.CertificateRepository}?filter=ID:{id}&filtertype=exact&props=ID,Type";
            var request = new RestRequest(url);

            // var request = new RestRequest(Endpoints.CertificateRepository);
            // request.AddQueryParameter("filter", "ID");
            // request.AddQueryParameter("filtertype", "exact");
            // request.AddQueryParameter("props", "ID,Type");
            // the filter above _should_ return only the certs and keys with that alias.  
            // ...but it doesn't.  It returns any certs containing that string in the alias, so we have to filter the results.

            try
            {
                var collection = await _restClient.GetAsync<CertificateTableEntryCollection>(request);
                collection.SlbNewSslCfgCertsTable = collection.SlbNewSslCfgCertsTable.FindAll(c => c.ID == id);
                return collection;
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
                if (!response.IsSuccessful)
                {
                    throw new Exception($"Failed to add certificate: {alias}", response.ErrorException);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
        }

        internal async Task RemoveCertificate(string alias)
        {
            var existing = (await GetCertificatesById(alias)).SlbNewSslCfgCertsTable;
            if (existing.Count == 0)
            {
                throw new Exception($"Certificate with alias {alias} not found.");
            }
            try
            {
                existing.ForEach(c =>
                {
                    var url = $"{Endpoints.CertificateRepository}/{c.ID}/{c.Type}";
                    var request = new RestRequest(url, Method.Delete);

                    var response = _restClient.DeleteAsync(request).Result;

                    if (!response.IsSuccessful)
                    {
                        throw new Exception($"Failed to remove certificate: {alias}", response.ErrorException);
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
        }
    }
}
