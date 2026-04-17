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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using RestSharp;
using RestSharp.Authenticators;
using RestSharp.Serializers.Json;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer
{
    public class AlteonLoadBalancerClient
    {
        private RestClient _restClient { get; set; }
        protected ILogger logger { get; set; }

        // Shared STJ options — case-insensitive to handle any casing variations
        // the Alteon API may return across firmware versions.
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // ── Production constructor ────────────────────────────────────────────

        public AlteonLoadBalancerClient(string baseUrl, string username,
                                        string password, ILogger logger)
        {
            this.logger = logger;

            var options = new RestClientOptions(baseUrl)
            {
                RemoteCertificateValidationCallback =
                    (sender, certificate, chain, sslPolicyErrors) => true,
                Authenticator = new HttpBasicAuthenticator(username, password)
            };
            _restClient = new RestClient(
                options,
                configureSerialization: s => s.UseSystemTextJson(_jsonOptions));
        }

        // ── Testable constructor (internal — used by unit tests only) ─────────
        // Accepts a pre-configured HttpClient so MockHttp can intercept calls.

        internal AlteonLoadBalancerClient(string baseUrl, string username,
                                          string password, ILogger logger,
                                          HttpClient httpClient)
        {
            this.logger = logger;

            var options = new RestClientOptions(baseUrl)
            {
                RemoteCertificateValidationCallback =
                    (sender, certificate, chain, sslPolicyErrors) => true,
                Authenticator = new HttpBasicAuthenticator(username, password)
            };
            _restClient = new RestClient(
                httpClient,
                options,
                configureSerialization: s => s.UseSystemTextJson(_jsonOptions));
        }

        // ── Existing certificate repository methods (unchanged) ───────────────

        public async Task<CertificateTableEntryCollection> GetCertificates()
        {
            logger.MethodEntry();

            var request = new RestRequest(Endpoints.CertificateRepository, Method.Get);

            logger.LogTrace($"making request to retrieve certificates from endpoint: {request.Resource}");
            try
            {
                var response = await _restClient.ExecuteAsync(request);
                if (!response.IsSuccessful)
                {
                    logger.LogTrace($"the request failed with status code {response.StatusCode} and error message:");
                    logger.LogTrace($"{response.ErrorMessage}");
                    logger.LogTrace($"{response.Content}");
                }
                var certs = JsonSerializer.Deserialize<CertificateTableEntryCollection>(response.Content, _jsonOptions);
                return certs;
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

            try
            {
                logger.LogTrace($"retreiving certs from API endpoint: {url}");
                var response = await _restClient.GetAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogTrace($"the request failed with status code {response.StatusCode} and error message:");
                    logger.LogTrace($"{response.ErrorMessage}");
                    logger.LogTrace($"{response.Content}");
                    throw response.ErrorException;
                }

                var collection = JsonSerializer.Deserialize<CertificateTableEntryCollection>(response.Content, _jsonOptions);
                // Return a new record instance with the list filtered to exact matches.
                // (The API filter parameter does substring matching, not exact matching,
                // so we filter client-side to enforce ID and Type equality.)
                return collection with
                {
                    SlbNewSslCfgCertsTable = collection.SlbNewSslCfgCertsTable
                        .FindAll(c => c.ID == id && c.Type == typeInt)
                };
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

        public async Task AddCertificate(string alias, string pfxPassword,
                                          string certContents, string type, bool overwrite)
        {
            logger.MethodEntry();
            logger.LogTrace($"checking to see if an entry with alias '{alias}' and type '{type}' exists..");
            var existing = await GetCertificatesById(alias, type);
            var replace = false;
            if (existing.SlbNewSslCfgCertsTable?.Count > 0)
            {
                logger.LogTrace("it does..");
                if (!overwrite) throw new Exception($"The certificate with id {alias} already exists and Overwrite == false.");
                replace = true;
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

        // ── Apply / Save (unchanged) ──────────────────────────────────────────

        internal async Task ApplyAndSave()
        {
            logger.MethodEntry();
            logger.LogTrace($"making requests to apply and save changes");
            try
            {
                await ApplyChanges();
                await Task.Delay(3000);
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

            var applyRequest = new RestRequest(Endpoints.Config, Method.Post);
            applyRequest.AddQueryParameter("action", "apply");
            applyRequest.AddHeader("Accept", "application/json");

            var fullUri = _restClient.BuildUri(applyRequest);
            logger.LogTrace($"making request to apply and save changes to {fullUri.ToString()}");
            try
            {
                var response = await _restClient.ExecuteAsync(applyRequest);
                if (!response.IsSuccessful)
                {
                    logger.LogError($"request to apply changes failed with error message: {response.ErrorMessage}\ncontent:{response.Content}");
                    throw new Exception($"Failed to apply changes.", response.ErrorException);
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"request to apply changest failed: {ex.Message}", ex);
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
            var saveRequest = new RestRequest(Endpoints.Config, Method.Post);
            saveRequest.AddQueryParameter("action", "save");
            saveRequest.AddHeader("Accept", "application/json");

            var fullUri = _restClient.BuildUri(saveRequest);
            logger.LogTrace($"making request to save changes to {fullUri.ToString()}");
            try
            {
                var response = await _restClient.ExecuteAsync(saveRequest);
                if (!response.IsSuccessful)
                {
                    logger.LogError($"request to save changes failed: {response.ErrorMessage}, {response.Content}");
                    throw new Exception($"Failed to save changes, status code : {response.StatusCode}\nerror message : {response.ErrorMessage}\ncontent:{response.Content}");
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
            var request = new RestRequest(Endpoints.ApplyTable, Method.Get);
            request.AddHeader("Accept", "application/json");
            var fullUri = _restClient.BuildUri(request);
            logger.LogTrace($"making request to get apply table to uri {fullUri}");
            try
            {
                var response = await _restClient.ExecuteAsync(request);
                if (!response.IsSuccessful)
                {
                    logger.LogError($"There was an error getting the pending changes: {response.StatusCode}\t{response.ErrorMessage}\t{response.Content}");
                    throw response.ErrorException;
                }
                logger.LogTrace($"Apply table response: {response.Content}");
            }
            catch (Exception ex)
            {
                logger.LogError($"LogApplyTable failed (not critical) - {ex.Message}", ex);
                // continuing.. non-critical
            }
            finally
            {
                logger.MethodExit();
            }
        }

        // ── NEW: Virtual service methods ──────────────────────────────────────

        /// <summary>
        /// Returns all virtual service entries across all virtual servers.
        /// Single API call — callers filter in memory to avoid N+1 requests.
        /// </summary>
        public async Task<List<VirtServiceEntry>> GetAllVirtualServicesAsync()
        {
            logger.MethodEntry();
            var request = new RestRequest(Endpoints.VirtualServices, Method.Get);
            try
            {
                var response = await _restClient.ExecuteAsync(request);
                if (!response.IsSuccessful)
                {
                    logger.LogError($"Failed to retrieve virtual services: {response.StatusCode} {response.Content}");
                    return new List<VirtServiceEntry>();
                }
                var table = JsonSerializer.Deserialize<VirtServiceTableResponse>(response.Content, _jsonOptions);
                return table?.Entries ?? new List<VirtServiceEntry>();
            }
            catch (Exception ex)
            {
                logger.LogError($"GetAllVirtualServicesAsync failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        /// <summary>
        /// Returns a single virtual service entry, or null if not found.
        /// Used during Add to determine SNI vs non-SNI mode for that service.
        /// </summary>
        public async Task<VirtServiceEntry> GetVirtualServiceAsync(string virtId, string servicePort)
        {
            logger.MethodEntry();
            var url = $"{Endpoints.VirtualServices}/{Uri.EscapeDataString(virtId)}/{Uri.EscapeDataString(servicePort)}";
            var request = new RestRequest(url, Method.Get);
            try
            {
                var response = await _restClient.ExecuteAsync(request);

                // Alteon returns 405 for non-existent resources on v30.5
                if (response.StatusCode == HttpStatusCode.MethodNotAllowed
                    || response.StatusCode == HttpStatusCode.NotFound
                    || !response.IsSuccessful)
                {
                    logger.LogTrace($"Virtual service {virtId}:{servicePort} not found ({response.StatusCode})");
                    return null;
                }

                var table = JsonSerializer.Deserialize<VirtServiceTableResponse>(response.Content, _jsonOptions);
                return table?.Entries?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                logger.LogError($"GetVirtualServiceAsync({virtId},{servicePort}) failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        /// <summary>
        /// Returns all virtual service bindings for a given certificate,
        /// covering both non-SNI (SrvCert) and SNI (CertGroup membership).
        /// Fetches all virtual services and all cert groups in two API calls,
        /// then filters in memory — O(services + groups), not O(certs).
        /// </summary>
        public async Task<List<VirtualServiceBinding>> GetBindingsForCertificateAsync(string certId)
        {
            logger.MethodEntry();
            var bindings = new List<VirtualServiceBinding>();

            try
            {
                var allServices = await GetAllVirtualServicesAsync();
                var allGroups = await GetAllCertGroupsAsync();

                // Non-SNI: direct SrvCert binding
                foreach (var svc in allServices)
                {
                    if (string.Equals(svc.SrvCert, certId,
                            StringComparison.OrdinalIgnoreCase))
                        bindings.Add(new VirtualServiceBinding(svc.VirtIndex, svc.ServicePort));
                }

                // SNI: cert group membership
                // Build a lookup: groupId → virtual services that reference it
                var groupToServices = allServices
                    .Where(s => !string.IsNullOrWhiteSpace(s.CertGroup))
                    .GroupBy(s => s.CertGroup)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var group in allGroups)
                {
                    if (!group.Certs.Any(c => string.Equals(c, certId,
                            StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (!groupToServices.TryGetValue(group.ID, out var services))
                        continue;

                    foreach (var svc in services)
                        bindings.Add(new VirtualServiceBinding(svc.VirtIndex, svc.ServicePort));
                }

                return bindings;
            }
            catch (Exception ex)
            {
                logger.LogError($"GetBindingsForCertificateAsync({certId}) failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        /// <summary>
        /// Binds a certificate directly to a virtual service (non-SNI path).
        /// Also sets the SSL policy. Does not apply or save.
        /// Pass empty strings for certId and sslPolicyId to clear the binding.
        /// </summary>
        public async Task BindCertificateDirectAsync(VirtualServiceBinding binding,
                                                      string certId, string sslPolicyId)
        {
            logger.MethodEntry();
            var url = $"{Endpoints.VirtualServices}/" +
                      $"{Uri.EscapeDataString(binding.VirtId)}/" +
                      $"{Uri.EscapeDataString(binding.ServicePort)}";

            var payload = new Dictionary<string, string>
            {
                ["SrvCert"] = certId ?? string.Empty
            };

            // Only include SslPolName when setting (not when clearing)
            if (!string.IsNullOrEmpty(sslPolicyId))
                payload["SslPolName"] = sslPolicyId;

            var request = new RestRequest(url, Method.Put);
            request.AddJsonBody(payload);

            try
            {
                var response = await _restClient.ExecuteAsync(request);
                if (!response.IsSuccessful)
                    throw new Exception(
                        $"Failed to bind cert '{certId}' to {binding}: " +
                        $"{response.StatusCode} {response.Content}");

                logger.LogTrace($"Bound cert '{certId}' to {binding} with policy '{sslPolicyId}'");
            }
            catch (Exception ex)
            {
                logger.LogError($"BindCertificateDirectAsync({binding}) failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        // ── SSL Policy methods ───────────────────────────────────────────

        /// <summary>
        /// Ensures an SSL policy with the given ID exists on the device.
        /// If it already exists, leaves it untouched (preserves custom settings).
        /// If it does not exist, creates it with sensible security defaults:
        ///   - TLS 1.2 and 1.3 enabled; TLS 1.0/1.1 disabled
        ///   - Cipher suite: HIGH
        ///   - Frontend SSL enabled, backend SSL disabled
        /// Policy naming convention: KF-{virtId}-{servicePort}
        /// </summary>
        public async Task EnsureSslPolicyAsync(string policyId)
        {
            logger.MethodEntry();
            try
            {
                var exists = await SslPolicyExistsAsync(policyId);
                if (exists)
                {
                    logger.LogTrace($"SSL policy '{policyId}' already exists — leaving unchanged");
                    return;
                }

                logger.LogTrace($"SSL policy '{policyId}' not found — creating with defaults");

                var url = $"{Endpoints.SslPolicies}/{Uri.EscapeDataString(policyId)}";
                var request = new RestRequest(url, Method.Post);
                request.AddJsonBody(new
                {
                    SlbSslPolName = policyId,
                    SlbSslPolAdminStatus = 2,      // enabled
                    // TLS version restrictions are applied via separate
                    // /frver sub-resource after creation on most firmware versions
                });

                var response = await _restClient.ExecuteAsync(request);
                if (!response.IsSuccessful)
                    throw new Exception(
                        $"Failed to create SSL policy '{policyId}': " +
                        $"{response.StatusCode} {response.Content}");

                logger.LogTrace($"Created SSL policy '{policyId}'");
            }
            catch (Exception ex)
            {
                logger.LogError($"EnsureSslPolicyAsync({policyId}) failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        private async Task<bool> SslPolicyExistsAsync(string policyId)
        {
            var url = $"{Endpoints.SslPolicies}/{Uri.EscapeDataString(policyId)}";
            var request = new RestRequest(url, Method.Get);
            var response = await _restClient.ExecuteAsync(request);

            return response.IsSuccessful
                && response.StatusCode != HttpStatusCode.MethodNotAllowed
                && response.StatusCode != HttpStatusCode.NotFound;
        }

        // ── NEW: Cert group methods (SNI) ─────────────────────────────────────

        /// <summary>
        /// Returns all cert groups from the device. Single API call.
        /// </summary>
        public async Task<List<CertGroupEntry>> GetAllCertGroupsAsync()
        {
            logger.MethodEntry();
            var request = new RestRequest(Endpoints.CertGroups, Method.Get);
            try
            {
                var response = await _restClient.ExecuteAsync(request);
                if (!response.IsSuccessful)
                {
                    logger.LogTrace($"GetAllCertGroupsAsync: {response.StatusCode} — returning empty list");
                    return new List<CertGroupEntry>();
                }
                var table = JsonSerializer.Deserialize<CertGroupTableResponse>(response.Content, _jsonOptions);
                return table?.Entries ?? new List<CertGroupEntry>();
            }
            catch (Exception ex)
            {
                logger.LogError($"GetAllCertGroupsAsync failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        /// <summary>
        /// Returns a single cert group by ID, or null if not found.
        /// </summary>
        public async Task<CertGroupEntry> GetCertGroupAsync(string groupId)
        {
            logger.MethodEntry();
            var url = $"{Endpoints.CertGroups}/{Uri.EscapeDataString(groupId)}";
            var request = new RestRequest(url, Method.Get);
            try
            {
                var response = await _restClient.ExecuteAsync(request);
                if (response.StatusCode == HttpStatusCode.MethodNotAllowed
                    || response.StatusCode == HttpStatusCode.NotFound
                    || !response.IsSuccessful)
                    return null;

                var table = JsonSerializer.Deserialize<CertGroupTableResponse>(response.Content, _jsonOptions);
                return table?.Entries?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                logger.LogError($"GetCertGroupAsync({groupId}) failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        /// <summary>
        /// Adds a certificate to an existing cert group.
        /// Idempotent — if the cert is already a member, does nothing.
        /// </summary>
        public async Task AddCertToGroupAsync(string groupId, string certId)
        {
            logger.MethodEntry();
            try
            {
                var group = await GetCertGroupAsync(groupId);
                if (group == null)
                    throw new Exception($"Cert group '{groupId}' not found.");

                // Idempotent: already a member
                if (group.Certs.Any(c => string.Equals(c, certId,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    logger.LogTrace($"Cert '{certId}' is already a member of group '{groupId}' — skipping");
                    return;
                }

                var updatedCerts = new List<string>(group.Certs) { certId };
                var url = $"{Endpoints.CertGroups}/{Uri.EscapeDataString(groupId)}";
                var request = new RestRequest(url, Method.Put);
                request.AddJsonBody(new
                {
                    DefaultCert = group.DefaultCert,
                    Certs = updatedCerts
                });

                var response = await _restClient.ExecuteAsync(request);
                if (!response.IsSuccessful)
                    throw new Exception(
                        $"Failed to add cert '{certId}' to group '{groupId}': " +
                        $"{response.StatusCode} {response.Content}");

                logger.LogTrace($"Added cert '{certId}' to group '{groupId}'");
            }
            catch (Exception ex)
            {
                logger.LogError($"AddCertToGroupAsync({groupId},{certId}) failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        /// <summary>
        /// Removes a certificate from a cert group.
        /// Throws InvalidOperationException if the cert is the group's default cert —
        /// removing the default would leave non-SNI clients without a certificate.
        /// The operator must reassign the default cert manually before removal.
        /// </summary>
        public async Task RemoveCertFromGroupAsync(string groupId, string certId)
        {
            logger.MethodEntry();
            try
            {
                var group = await GetCertGroupAsync(groupId);
                if (group == null)
                {
                    logger.LogTrace($"Cert group '{groupId}' not found — nothing to remove");
                    return;
                }

                // Safety check: refuse to remove the default cert
                if (string.Equals(group.DefaultCert, certId,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Cannot remove cert '{certId}' from group '{groupId}' — " +
                        $"it is the default cert for that group. " +
                        $"Reassign the default cert on group '{groupId}' before removing.");

                var updatedCerts = group.Certs
                    .Where(c => !string.Equals(c, certId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var url = $"{Endpoints.CertGroups}/{Uri.EscapeDataString(groupId)}";
                var request = new RestRequest(url, Method.Put);
                request.AddJsonBody(new
                {
                    DefaultCert = group.DefaultCert,
                    Certs = updatedCerts
                });

                var response = await _restClient.ExecuteAsync(request);
                if (!response.IsSuccessful)
                    throw new Exception(
                        $"Failed to remove cert '{certId}' from group '{groupId}': " +
                        $"{response.StatusCode} {response.Content}");

                logger.LogTrace($"Removed cert '{certId}' from group '{groupId}'");
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                logger.LogError($"RemoveCertFromGroupAsync({groupId},{certId}) failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        /// <summary>
        /// Creates a new cert group with the given cert as both the sole member
        /// and the default cert. Used when a virtual service references a group
        /// that doesn't exist yet.
        /// </summary>
        public async Task CreateCertGroupAsync(string groupId, string defaultCertId)
        {
            logger.MethodEntry();
            try
            {
                var url = $"{Endpoints.CertGroups}/{Uri.EscapeDataString(groupId)}";
                var request = new RestRequest(url, Method.Post);
                request.AddJsonBody(new
                {
                    ID = groupId,
                    Name = groupId,
                    DefaultCert = defaultCertId,
                    Certs = new[] { defaultCertId }
                });

                var response = await _restClient.ExecuteAsync(request);
                if (!response.IsSuccessful)
                    throw new Exception(
                        $"Failed to create cert group '{groupId}': " +
                        $"{response.StatusCode} {response.Content}");

                logger.LogTrace($"Created cert group '{groupId}' with default cert '{defaultCertId}'");
            }
            catch (Exception ex)
            {
                logger.LogError($"CreateCertGroupAsync({groupId},{defaultCertId}) failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }
    }
}
