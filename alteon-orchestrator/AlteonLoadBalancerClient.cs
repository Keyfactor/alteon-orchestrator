// Copyright 2026 Keyfactor
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
        protected ILogger logger { get; set; }

        public AlteonLoadBalancerClient(string baseUrl, string username, string password, ILogger logger)
        {
            this.logger = logger;

            var options = new RestClientOptions(baseUrl)
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true, // this is to allow self-signed appliance certs
                Authenticator = new HttpBasicAuthenticator(username, password)
            };
            _restClient = new RestClient(options);
        }

        public async Task<CertificateTableEntryCollection> GetCertificates()
        {
            logger.MethodEntry();

            var request = new RestRequest(Endpoints.CertificateRepository);

            logger.LogTrace($"making request to retrieve certificates from endpoint: {request.Resource}");
            try
            {
                var response = await _restClient.GetAsync<CertificateTableEntryCollection>(request);
                return response;
            }
            catch (Exception ex)
            {
                logger.LogError($"An error occurred when attempting to retrieve the certificates from {_restClient.BuildUri(request)?.ToString()}");
                logger.LogError(ex.Message, ex);
                throw;
            }
            finally
            {
                logger.MethodExit();
            }
        }

        public async Task<CertificateTableEntryCollection> GetCertificatesById(string id, string type)
        {
            logger.MethodEntry();
            var typeInt = AlteonCertTypes.AlteonCertTypeValue(type);

            var url = $"{Endpoints.CertificateRepository}?filter=ID:{id},Type:{typeInt}&filtertype=exact&props=ID,Name,Type";
            var request = new RestRequest(url);

            // the filter above _should_ return only the certs and keys with that alias.  
            // ...but it doesn't.  It returns any certs containing that string in the alias, so we have to filter the results.

            try
            {
                logger.LogTrace($"retreiving certs from API endpoint: {url}");
                var collection = await _restClient.GetAsync<CertificateTableEntryCollection>(request);
                collection.SlbNewSslCfgCertsTable = collection.SlbNewSslCfgCertsTable.FindAll(c => c.ID == id && c.Type == typeInt);
                return collection;
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        public string GetCertificateContent(string certId)
        {
            logger.MethodEntry();
            var request = new RestRequest(Endpoints.CertificateContent);
            request.AddQueryParameter("id", certId);
            request.AddQueryParameter("type", "srvcrt");
            var fullUri = _restClient.BuildUri(request);

            logger.LogTrace($"making request to get certificate from the endpoint: {fullUri}");

            try
            {
                var response = _restClient.DownloadData(request);
                var sr = new StreamReader(new MemoryStream(response), Encoding.UTF8);
                var content = sr.ReadToEnd();
                return content;
            }
            catch (Exception ex)
            {
                logger.LogError($"An error occurred when attempting to retrieve the certificate with id '{certId}' from {fullUri}");
                logger.LogError(ex.Message, ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        public async Task AddCertificate(string alias, string pfxPassword, string certContents, string type, bool overwrite)
        {
            logger.MethodEntry();
            // first, see if a certificate with this alias/id already exists
            logger.LogTrace($"checking to see if an entry with alias '{alias}' and type '{type}' exists..");
            var existing = await GetCertificatesById(alias, type);
            var replace = false;
            if (existing.SlbNewSslCfgCertsTable?.Count > 0)
            {
                logger.LogTrace("it does..");
                // the cert already exists; if overwrite == true, we should overwrite; else exist here.
                if (!overwrite) throw new Exception($"The certificate with id {alias} already exists and Overwrite == false.");
                replace = true; // if it exists and overwrite is true, we replace it.
                logger.LogTrace(".. and overwrite is true, passing renew=1 in the query.");
            }
            else logger.LogTrace("..it does not.  Adding as new");

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
                // apply and save changes
                await ApplyAndSave();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
            finally
            {
                logger.MethodExit();
            }
        }

        internal async Task RemoveCertificate(string alias)
        {
            logger.MethodEntry();
            var url = string.Empty;

            var existing = (await GetCertificatesById(alias, AlteonCertTypes.CERT_ONLY)).SlbNewSslCfgCertsTable;
            if (existing.Count == 0)
            {
                throw new Exception($"Certificate with alias {alias} not found.");
            }
            try
            {
                foreach (var c in existing)
                {
                    url = $"{Endpoints.CertificateRepository}/{c.ID}/{c.Type}";
                    var request = new RestRequest(url, Method.Delete);
                    var fullUri = _restClient.BuildUri(request);
                    logger.LogTrace($"making request to remove certificate to uri {fullUri}");
                    var response = await _restClient.DeleteAsync(request);

                    if (!response.IsSuccessful)
                    {
                        throw new Exception($"Failed to remove certificate: {alias}", response.ErrorException);
                    }
                }
                // apply and save changes
                await ApplyAndSave();
            }
            catch (Exception ex)
            {
                logger.LogError($"An error occurred when attempting to remove the certificate with alias {alias} via endpoint: '{url}'");
                logger.LogError(ex.Message, ex);
                throw;
            }
            finally
            {
                logger.MethodExit();
            }
        }

        /// <summary>
        /// This method is intended to be called after making changes to the certs/keys on the Alteon.  It will apply the changes and save the config, so that the changes persist through a reboot.  If this isn't called after making changes, the changes will be lost on reboot.
        /// </summary>
        /// <returns></returns>
        internal async Task ApplyAndSave()
        {
            logger.MethodEntry();
            logger.LogTrace($"making requests to apply and save changes");
            try
            {
                await ApplyChanges();
                await LogApplyTable();
                await SaveChanges();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
            finally
            {
                logger.MethodExit();
            }
        }

        internal async Task ApplyChanges()
        {
            logger.MethodEntry();
            var applyRequest = new RestRequest(Endpoints.ApplyChanges, Method.Post);
            var fullUri = _restClient.BuildUri(applyRequest);
            logger.LogTrace($"making request to apply and save changes to uri {fullUri}");
            try
            {
                var response = await _restClient.PostAsync(applyRequest);
                if (!response.IsSuccessful)
                {
                    logger.LogError($"request to apply changes failed: {response.ErrorMessage}, {response.Content}");
                    throw new Exception($"Failed to apply changes.", response.ErrorException);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
            finally
            {
                logger.MethodExit();
            }
        }

        internal async Task SaveChanges()
        {
            logger.MethodEntry();
            var saveRequest = new RestRequest(Endpoints.SaveChanges, Method.Post);
            var fullUri = _restClient.BuildUri(saveRequest);
            logger.LogTrace($"making request to save changes to uri {fullUri}");
            try
            {
                var response = await _restClient.PostAsync(saveRequest);
                if (!response.IsSuccessful)
                {
                    throw new Exception($"Failed to save changes.", response.ErrorException);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
            finally
            {
                logger.MethodExit();
            }
        }

        internal async Task LogApplyTable()
        {
            logger.MethodEntry();
            var request = new RestRequest(Endpoints.ApplyTable);
            var fullUri = _restClient.BuildUri(request);
            logger.LogTrace($"making request to get apply table to uri {fullUri}");
            try
            {
                var response = await _restClient.GetAsync(request);
                logger.LogTrace($"Apply table response: {response.Content}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
                throw;
            }
            finally
            {
                logger.MethodExit();
            }
        }
    }
}
