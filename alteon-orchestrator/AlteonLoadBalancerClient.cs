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
            request.AddHeader("Accept", "*/*");
            request.AddStringBody(certContents, "text/plain");
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

            // The Add job uploads cert and key as two separate entries on the device
            // (CERT_ONLY + KEY_ONLY). We must delete both here to avoid orphaned private keys.
            var existingCerts = (await GetCertificatesById(alias, AlteonCertTypes.CERT_ONLY)).SlbNewSslCfgCertsTable;
            if (existingCerts.Count == 0)
            {
                throw new Exception($"Certificate with alias {alias} not found.");
            }

            // KEY_ONLY entry may not exist (e.g. CA / intermediate certs have no key), so
            // a missing key is not an error — Concat handles the empty list gracefully.
            var existingKeys = (await GetCertificatesById(alias, AlteonCertTypes.KEY_ONLY)).SlbNewSslCfgCertsTable;

            var allEntries = existingCerts.Concat(existingKeys).ToList();

            try
            {
                foreach (var c in allEntries)
                {
                    url = $"{Endpoints.CertificateRepository}/{c.ID}/{c.Type}";
                    var request = new RestRequest(url, Method.Delete);
                    var fullUri = _restClient.BuildUri(request);
                    logger.LogTrace($"making request to remove certificate/key entry to uri {fullUri}");
                    var response = await _restClient.ExecuteAsync(request);

                    if (!response.IsSuccessful)
                    {
                        throw new Exception($"Failed to remove entry (alias={alias}, type={c.Type})", response.ErrorException);
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
                await WaitForApplyIdleAsync();
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

        /// <summary>
        /// Polls agApplyConfig until the apply state is idle (2) or complete (4).
        /// States: 1=apply, 2=idle, 3=inprogress, 4=complete, 5=failed
        /// Times out after 60 seconds and proceeds anyway.
        /// </summary>
        private async Task WaitForApplyIdleAsync(
            int pollIntervalMs = 500, int timeoutMs = 60000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var request  = new RestRequest("config/agApplyConfig", Method.Get);
                var response = await _restClient.ExecuteAsync(request);

                if (!response.IsSuccessful)
                {
                    logger.LogTrace($"WaitForApplyIdleAsync: could not read agApplyConfig ({response.StatusCode}) — proceeding");
                    return;
                }

                // Parse the numeric state from the response
                // Response looks like: { "agApplyConfig": 3 }
                var doc = System.Text.Json.JsonDocument.Parse(response.Content);
                if (doc.RootElement.TryGetProperty("agApplyConfig", out var stateProp)
                    && stateProp.TryGetInt32(out var state))
                {
                    // 2=idle, 4=complete, 5=failed — all safe to proceed
                    if (state != 3)
                    {
                        logger.LogTrace($"WaitForApplyIdleAsync: agApplyConfig state={state} — proceeding");
                        return;
                    }
                    logger.LogTrace($"WaitForApplyIdleAsync: apply in progress (state=3) — waiting {pollIntervalMs}ms");
                }
                else
                {
                    // Unexpected response shape — just proceed
                    logger.LogTrace("WaitForApplyIdleAsync: unexpected response shape — proceeding");
                    return;
                }

                await Task.Delay(pollIntervalMs);
            }

            logger.LogTrace("WaitForApplyIdleAsync: timed out waiting for idle — proceeding anyway");
        }

        internal async Task ApplyChanges()
        {
            logger.MethodEntry();

            var applyRequest = new RestRequest(Endpoints.Config, Method.Post);
            applyRequest.AddQueryParameter("action", "apply");
            applyRequest.AddHeader("Accept", "*/*");

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
            saveRequest.AddHeader("Accept", "*/*");

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

        // ── Virtual service methods ───────────────────────────────────────────

        /// <summary>
        /// Validates that a virtual server name exists on the device.
        /// In Alteon's enhanced virtual server table, VirtServerIndex IS the name —
        /// there is no separate numeric index. Returns the confirmed name, or the
        /// input as-is if the table can't be fetched (graceful degradation).
        /// </summary>
        private async Task<string> ResolveVirtIndexAsync(string virtIdOrName)
        {
            var request = new RestRequest(Endpoints.VirtualServers, Method.Get);
            var response = await _restClient.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                logger.LogTrace($"ResolveVirtIndexAsync: could not fetch virtual server table ({response.StatusCode}) — using '{virtIdOrName}' as-is");
                return virtIdOrName;
            }

            var table = JsonSerializer.Deserialize<VirtServerTableResponse>(response.Content, _jsonOptions);
            var match = table?.Entries?.FirstOrDefault(e =>
                string.Equals(e.VirtServerIndex, virtIdOrName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                logger.LogTrace($"Validated virtual server '{virtIdOrName}' exists on device");
                return match.VirtServerIndex;
            }

            logger.LogTrace($"No virtual server named '{virtIdOrName}' found in enhanced table — using as-is");
            return virtIdOrName;
        }

        /// <summary>
        /// Fetches all virtual services from SlbNewCfgEnhVirtServicesTable and
        /// resolves them into ResolvedVirtService objects by joining with the
        /// second-part and fifth-part tables (which hold ServCert, SSLpol, and
        /// ServCertGrpMark). Three GET calls total; all filtering is in-memory.
        /// </summary>
        public async Task<List<ResolvedVirtService>> GetAllResolvedServicesAsync()
        {
            logger.MethodEntry();
            try
            {
                // Fetch all three part tables in parallel
                var mainTask   = _restClient.ExecuteAsync(new RestRequest(Endpoints.VirtualServices,       Method.Get));
                var secondTask = _restClient.ExecuteAsync(new RestRequest(Endpoints.VirtualServicesSecond, Method.Get));
                var fifthTask  = _restClient.ExecuteAsync(new RestRequest(Endpoints.VirtualServicesFifth,  Method.Get));
                await Task.WhenAll(mainTask, secondTask, fifthTask);

                var mainEntries = mainTask.Result.IsSuccessful
                    ? JsonSerializer.Deserialize<VirtServiceTableResponse>(mainTask.Result.Content, _jsonOptions)?.Entries
                      ?? new List<VirtServiceEntry>()
                    : new List<VirtServiceEntry>();

                var secondEntries = secondTask.Result.IsSuccessful
                    ? JsonSerializer.Deserialize<VirtServiceSecondPartTableResponse>(secondTask.Result.Content, _jsonOptions)?.Entries
                      ?? new List<VirtServiceSecondPartEntry>()
                    : new List<VirtServiceSecondPartEntry>();

                var fifthEntries = fifthTask.Result.IsSuccessful
                    ? JsonSerializer.Deserialize<VirtServiceFifthPartTableResponse>(fifthTask.Result.Content, _jsonOptions)?.Entries
                      ?? new List<VirtServiceFifthPartEntry>()
                    : new List<VirtServiceFifthPartEntry>();

                // Build lookups for second and fifth part tables
                var secondLookup = secondEntries.ToDictionary(
                    e => (e.ServIndex, e.Index), e => e);
                var fifthLookup = fifthEntries.ToDictionary(
                    e => (e.ServIndex, e.Index), e => e);

                return mainEntries.Select(svc =>
                {
                    secondLookup.TryGetValue((svc.ServIndex, svc.Index), out var second);
                    fifthLookup.TryGetValue((svc.ServIndex,  svc.Index), out var fifth);
                    return new ResolvedVirtService
                    {
                        ServIndex    = svc.ServIndex,
                        Index        = svc.Index,
                        VirtPort     = svc.VirtPort,
                        ServCert     = second?.ServCert  ?? string.Empty,
                        SSLpol       = second?.SSLpol    ?? string.Empty,
                        CertGrpMark  = fifth?.ServCertGrpMark ?? 1  // default: cert (non-SNI)
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError($"GetAllResolvedServicesAsync failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        /// <summary>
        /// Resolves a single virtual service by name and port.
        /// Fetches all services and filters in-memory to avoid N+1 requests.
        /// Returns null if the virtual server name or port is not found.
        /// </summary>
        public async Task<ResolvedVirtService> GetVirtualServiceAsync(string virtId, string servicePort)
        {
            logger.MethodEntry();
            try
            {
                if (!int.TryParse(servicePort, out var port))
                {
                    logger.LogTrace($"GetVirtualServiceAsync: invalid port '{servicePort}'");
                    return null;
                }

                var confirmedVirtId = await ResolveVirtIndexAsync(virtId);
                var all = await GetAllResolvedServicesAsync();

                var match = all.FirstOrDefault(s =>
                    string.Equals(s.ServIndex, confirmedVirtId, StringComparison.OrdinalIgnoreCase)
                    && s.VirtPort == port);

                if (match == null)
                    logger.LogTrace($"Virtual service {virtId}:{servicePort} not found on device");

                return match;
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
        /// covering both non-SNI (ServCert direct) and SNI (CertGroup membership).
        /// </summary>
        public async Task<List<VirtualServiceBinding>> GetBindingsForCertificateAsync(string certId)
        {
            logger.MethodEntry();
            var bindings = new List<VirtualServiceBinding>();
            try
            {
                var allServices = await GetAllResolvedServicesAsync();
                var allGroups   = await GetAllCertGroupsAsync();

                // Non-SNI: direct ServCert binding
                foreach (var svc in allServices)
                {
                    if (svc.CertGrpMark == 1 &&
                        string.Equals(svc.ServCert, certId, StringComparison.OrdinalIgnoreCase))
                        bindings.Add(new VirtualServiceBinding(svc.ServIndex, svc.VirtPort.ToString()));
                }

                // SNI: cert group membership — find groups containing this cert,
                // then find services that reference those groups
                var groupsWithCert = allGroups
                    .Where(g => g.Certs.Any(c => string.Equals(c, certId, StringComparison.OrdinalIgnoreCase)))
                    .Select(g => g.ID)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var svc in allServices)
                {
                    if (svc.CertGrpMark == 2 &&
                        groupsWithCert.Contains(svc.ServCert))
                        bindings.Add(new VirtualServiceBinding(svc.ServIndex, svc.VirtPort.ToString()));
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
        /// Binds a certificate to a virtual service by writing to the second-part
        /// and fifth-part tables. Does not apply or save.
        /// Pass empty string for certId to clear the binding.
        /// certGrpMark: 1 = single cert (non-SNI), 2 = cert group (SNI).
        /// </summary>
        public async Task BindCertificateAsync(ResolvedVirtService svc,
                                               string certId, string sslPolicyId,
                                               int certGrpMark = 1)
        {
            logger.MethodEntry();
            try
            {
                // Write ServCert to the second-part table.
                // We only set SSLpol if the service has no policy configured —
                // preserving any manually configured policy on the device.
                // Sending SSLpol when one already exists can trigger Alteon
                // validation errors on some firmware versions.
                var secondUrl = $"{Endpoints.VirtualServicesSecond}/" +
                                $"{Uri.EscapeDataString(svc.ServIndex)}/{svc.Index}";
                var secondRequest = new RestRequest(secondUrl, Method.Put);
                var secondPayload = new Dictionary<string, object>
                {
                    ["ServCert"] = certId ?? string.Empty
                };
                if (!string.IsNullOrEmpty(sslPolicyId) && string.IsNullOrEmpty(svc.SSLpol))
                    secondPayload["SSLpol"] = sslPolicyId;

                secondRequest.AddJsonBody(secondPayload);
                var secondResponse = await _restClient.ExecuteAsync(secondRequest);
                if (!secondResponse.IsSuccessful)
                    throw new Exception(
                        $"Failed to set ServCert on {svc.ServIndex}:{svc.VirtPort}: " +
                        $"{secondResponse.StatusCode} {secondResponse.Content}");

                // Write ServCertGrpMark to the fifth-part table
                var fifthUrl = $"{Endpoints.VirtualServicesFifth}/" +
                               $"{Uri.EscapeDataString(svc.ServIndex)}/{svc.Index}";
                var fifthRequest = new RestRequest(fifthUrl, Method.Put);
                fifthRequest.AddJsonBody(new { ServCertGrpMark = certGrpMark });
                var fifthResponse = await _restClient.ExecuteAsync(fifthRequest);
                if (!fifthResponse.IsSuccessful)
                    throw new Exception(
                        $"Failed to set ServCertGrpMark on {svc.ServIndex}:{svc.VirtPort}: " +
                        $"{fifthResponse.StatusCode} {fifthResponse.Content}");

                logger.LogTrace($"Bound cert '{certId}' (grpMark={certGrpMark}) to {svc.ServIndex}:{svc.VirtPort} with policy '{sslPolicyId}'");
            }
            catch (Exception ex)
            {
                logger.LogError($"BindCertificateAsync({svc.ServIndex}:{svc.VirtPort}) failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        // ── SSL Policy methods ────────────────────────────────────────────────

        /// <summary>
        /// Ensures an SSL policy with the given ID exists on the device.
        /// If it already exists, leaves it untouched (preserves custom settings).
        /// If it does not exist, creates it with secure defaults:
        ///   TLS 1.2 + 1.3 enabled, TLS 1.0/1.1 disabled, frontend SSL enabled.
        /// Policy naming convention: KF-{virtId}-{servicePort}
        /// </summary>
        public async Task EnsureSslPolicyAsync(string policyId)
        {
            logger.MethodEntry();
            try
            {
                var checkUrl = $"{Endpoints.SslPolicies}/{Uri.EscapeDataString(policyId)}";
                var checkResp = await _restClient.ExecuteAsync(new RestRequest(checkUrl, Method.Get));
                if (checkResp.IsSuccessful
                    && checkResp.StatusCode != HttpStatusCode.NotFound
                    && checkResp.StatusCode != HttpStatusCode.MethodNotAllowed)
                {
                    logger.LogTrace($"SSL policy '{policyId}' already exists — leaving unchanged");
                    return;
                }

                logger.LogTrace($"SSL policy '{policyId}' not found — creating with defaults");
                var createUrl = $"{Endpoints.SslPolicies}/{Uri.EscapeDataString(policyId)}";
                var createReq = new RestRequest(createUrl, Method.Post);
                createReq.AddJsonBody(new
                {
                    Name          = policyId,
                    AdminStatus   = 1,   // enabled
                    Fessl         = 1,   // frontend SSL enabled
                    Bessl         = 2,   // backend SSL disabled
                    CipherName    = 9,   // HIGH
                    FETls12Version = 1,  // enabled
                    FETls13Version = 1,  // enabled
                    FETls10Version = 2,  // disabled
                    FETls11Version = 2,  // disabled
                    FESslv3Version = 2,  // disabled
                });

                var createResp = await _restClient.ExecuteAsync(createReq);
                if (!createResp.IsSuccessful)
                    throw new Exception(
                        $"Failed to create SSL policy '{policyId}': " +
                        $"{createResp.StatusCode} {createResp.Content}");

                logger.LogTrace($"Created SSL policy '{policyId}'");
            }
            catch (Exception ex)
            {
                logger.LogError($"EnsureSslPolicyAsync({policyId}) failed: {ex.Message}", ex);
                throw;
            }
            finally { logger.MethodExit(); }
        }

        // ── Cert group methods (SNI) ──────────────────────────────────────────

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
