// Copyright 2022 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
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
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
                Authenticator = new HttpBasicAuthenticator(username, password)
            };
            _restClient = new RestClient(options);
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
            logger.MethodEntry();
            var request = new RestRequest(Endpoints.CertificateContent);
            request.AddQueryParameter("id", certId);
            request.AddQueryParameter("type", "srvcrt");
            var fullUri = _restClient.BuildUri(request);

            logger.LogTrace($"making request to get certificate to uri: {fullUri}");

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

        public async Task AddCertificate(string alias, string pfxPassword, string certContents, string type, bool overwrite)
        {
            logger.MethodEntry();
            // first, see if a certificate with this alias/id already exists
            var existing = await GetCertificatesById(alias);
            var replace = false;
            if (existing.SlbNewSslCfgCertsTable?.Count > 0)
            {
                // the cert already exists; if overwrite == true, we should overwrite; else exist here.
                if (!overwrite) throw new Exception($"The certificate with id {alias} already exists and Overwrite == false.");
                replace = true; // if it exists and overwrite is true, we replace it.
            }


            var request = new RestRequest(Endpoints.AddCertificate, Method.Post);
            request.AddQueryParameter("id", alias);
            request.AddQueryParameter("type", type);
            request.AddQueryParameter("passphrase", pfxPassword);
            request.AddQueryParameter("src", "txt");
            if (replace) request.AddQueryParameter("renew", 1);

            request.AddBody(certContents);
            var fullUri = _restClient.BuildUri(request);
            logger.LogTrace($"posting certificate to the uri {fullUri}");

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
            logger.MethodExit();
        }

        internal async Task RemoveCertificate(string alias)
        {
            logger.MethodEntry();

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
                    var fullUri = _restClient.BuildUri(request);
                    logger.LogTrace($"making request to remove certificate to uri {fullUri}");
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
            logger.MethodExit();
        }
    }
}
