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
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.PKI.X509;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Jobs
{
    public class Management : JobBase, IManagementJobExtension
    {
        // ── Production constructor ────────────────────────────────────────────

        public Management(IPAMSecretResolver resolver)
        {
            _resolver = resolver;
            logger = LogHandler.GetClassLogger<Management>();
        }

        // ── Testable constructor (internal — used by unit tests only) ─────────

        internal Management(IPAMSecretResolver resolver,
                            string serverUrl, string username, string password,
                            ILogger logger, HttpClient httpClient)
        {
            _resolver = resolver;
            this.logger = logger;
            InitializeStore(serverUrl, username, password, httpClient);
        }

        // ── ProcessJob ────────────────────────────────────────────────────────

        public JobResult ProcessJob(ManagementJobConfiguration config)
        {
            InitializeStore(config);

            JobResult complete = new JobResult()
            {
                Result = OrchestratorJobStatusJobResult.Failure,
                FailureMessage = "Invalid Management Operation"
            };

            switch (config.OperationType)
            {
                case CertStoreOperationType.Add:
                    logger.LogDebug($"Begin Management > Add...");
                    complete = PerformAddition(
                        config.JobCertificate.Alias,
                        config.JobCertificate.PrivateKeyPassword,
                        config.JobCertificate.Contents,
                        config.JobHistoryId,
                        config.JobProperties).Result;
                    break;

                case CertStoreOperationType.Remove:
                    logger.LogDebug($"Begin Management > Remove...");
                    complete = PerformRemoval(
                        config.JobCertificate.Alias,
                        config.JobHistoryId).Result;
                    break;
            }

            return complete;
        }

        // ── Add ───────────────────────────────────────────────────────────────

        protected virtual async Task<JobResult> PerformAddition(
            string alias, string pfxPassword, string entryContents,
            long jobHistoryId, IDictionary<string, object> jobProperties = null)
        {
            var complete = new JobResult
            {
                Result = OrchestratorJobStatusJobResult.Failure,
                JobHistoryId = jobHistoryId
            };

            byte[] bytes;
            X509Certificate2 x509;
            string pemCert, pemKey;

            try
            {
                bytes = Convert.FromBase64String(entryContents);
                x509 = new X509Certificate2(bytes, pfxPassword, X509KeyStorageFlags.Exportable);
                (pemCert, pemKey) = GetPemFromPfx(bytes, pfxPassword);
            }
            catch (Exception ex)
            {
                logger.LogError("an error occurred when attempting to decode the certificate");
                logger.LogError($"certificate contents: \n{entryContents}");
                logger.LogError($"error: {ex.Message}");
                throw;
            }

            var certType = AlteonCertTypes.INTERMEDIATE_CA;

            if (x509.HasPrivateKey)
            {
                logger.LogTrace($"Private key is present, setting cert type to {AlteonCertTypes.CERTIFICATE_AND_KEY}");
                certType = AlteonCertTypes.CERTIFICATE_AND_KEY;
            }
            else
            {
                if (x509.Subject == x509.Issuer)
                {
                    logger.LogTrace($"Subject = {x509.Issuer}, importing as a trusted CA certificate");
                    certType = AlteonCertTypes.TRUSTED_CA;
                }
            }

            logger.LogTrace($"determined type to be {certType}");

            if (!string.IsNullOrWhiteSpace(pfxPassword))
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    complete.FailureMessage = "You must supply an alias for the certificate.";
                    return complete;
                }

                try
                {
                    if (certType == AlteonCertTypes.CERTIFICATE_AND_KEY)
                    {
                        logger.LogTrace($"adding key and then certificate for certificate with alias {alias}");
                        await aClient.AddCertificate(alias, pfxPassword, pemKey,
                            AlteonCertTypes.KEY_ONLY, Overwrite);
                        await aClient.AddCertificate(alias, pfxPassword, pemCert,
                            AlteonCertTypes.CERT_ONLY, Overwrite);
                        await aClient.ApplyAndSave();
                    }
                    else
                    {
                        logger.LogTrace($"Adding certificate only for certificate with alias {alias}");
                        await aClient.AddCertificate(alias, pfxPassword, pemCert,
                            certType, Overwrite);
                    }

                    // ── Bind to virtual services ─────────────────────────────
                    var bindingsResult = await PerformBindingsForAddAsync(
                        alias,
                        jobProperties?.TryGetValue("VirtualServiceBindings", out var b) == true
                            ? b?.ToString() : null,
                        jobHistoryId,
                        Overwrite);

                    if (bindingsResult.Result == OrchestratorJobStatusJobResult.Failure)
                        return bindingsResult;

                    complete.Result = OrchestratorJobStatusJobResult.Success;
                }
                catch (Exception ex)
                {
                    complete.FailureMessage = $"An error occurred while adding {alias} to {ExtensionName}: " + ex.Message;
                    if (ex.InnerException != null)
                        complete.FailureMessage += " - " + ex.InnerException.Message;
                    logger.LogError($"an error occurred when attempting to add certificate: {ex.Message}");
                }
            }
            else
            {
                complete.FailureMessage = "Certificate to add must be in a .PFX file format.";
            }

            return complete;
        }

        // ── Remove ────────────────────────────────────────────────────────────

        protected virtual async Task<JobResult> PerformRemoval(string alias, long jobHistoryId)
        {
            var complete = new JobResult
            {
                Result = OrchestratorJobStatusJobResult.Failure,
                JobHistoryId = jobHistoryId
            };

            if (string.IsNullOrWhiteSpace(alias))
            {
                complete.FailureMessage = "You must supply an alias for the certificate.";
                return complete;
            }

            try
            {
                // ── Clear bindings first ─────────────────────────────────────
                var bindingsResult = await PerformBindingsForRemoveAsync(alias, jobHistoryId);
                if (bindingsResult.Result == OrchestratorJobStatusJobResult.Failure)
                    return bindingsResult;

                // ── Then remove the cert from the repository ─────────────────
                await aClient.RemoveCertificate(alias);
                complete.Result = OrchestratorJobStatusJobResult.Success;
            }
            catch (Exception ex)
            {
                logger.LogError($"An error occurred when attempting to remove the certificate with alias {alias}: {ex.Message}");
                complete.FailureMessage = $"An error occurred while removing {alias} from {ExtensionName}: " + ex.Message;
            }

            return complete;
        }

        // ── NEW: Binding operations ───────────────────────────────────────────

        /// <summary>
        /// Binds a certificate to the virtual services specified in the
        /// VirtualServiceBindings entry parameter.
        ///
        /// For each virtual service:
        ///   - Fetches the current service state from the device
        ///   - If not found → fails with clear message
        ///   - If CertGroup is set → SNI path (add to group or create group)
        ///   - Otherwise → non-SNI path (set SrvCert + ensure policy exists)
        ///   - If a DIFFERENT cert is already bound → fails with clear message
        ///   - If the SAME cert is already bound → succeeds (idempotent / renewal)
        ///
        /// Apply+Save is called once after all bindings succeed.
        /// </summary>
        internal async Task<JobResult> PerformBindingsForAddAsync(
            string certId, string bindingsParam, long jobHistoryId,
            bool overwrite = false)
        {
            var complete = new JobResult
            {
                Result = OrchestratorJobStatusJobResult.Failure,
                JobHistoryId = jobHistoryId
            };

            // ── Validate entry parameter ─────────────────────────────────────
            if (string.IsNullOrWhiteSpace(bindingsParam))
            {
                complete.FailureMessage =
                    "Entry parameter 'VirtualServiceBindings' is required. " +
                    "Provide one or more 'virtId:port' pairs, comma-separated. " +
                    "Example: '1:443' or '1:443,2:443,my-virt:8443'.";
                return complete;
            }

            List<VirtualServiceBinding> bindings;
            try
            {
                bindings = VirtualServiceBinding.ParseList(bindingsParam);
            }
            catch (ArgumentException ex)
            {
                complete.FailureMessage = ex.Message;
                return complete;
            }

            // ── Process each binding ─────────────────────────────────────────
            var errors = new List<string>();

            foreach (var binding in bindings)
            {
                try
                {
                    var svc = await aClient.GetVirtualServiceAsync(
                        binding.VirtId, binding.ServicePort);

                    if (svc == null)
                    {
                        errors.Add($"Virtual service '{binding}' not found on device.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(svc.CertGroup))
                    {
                        // ── SNI path ─────────────────────────────────────────
                        logger.LogDebug($"Virtual service {binding} uses cert group '{svc.CertGroup}' — SNI path");
                        await HandleSniBindingAsync(svc.CertGroup, certId);
                    }
                    else
                    {
                        // ── Non-SNI path ──────────────────────────────────────
                        // Conflict check: a DIFFERENT cert is already bound
                        if (!string.IsNullOrWhiteSpace(svc.SrvCert)
                            && !string.Equals(svc.SrvCert, certId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            if (!overwrite)
                            {
                                errors.Add(
                                    $"Virtual service '{binding}' already has cert " +
                                    $"'{svc.SrvCert}' bound. " +
                                    $"Enable Overwrite to replace the existing binding.");
                                continue;
                            }

                            logger.LogDebug(
                                $"Overwrite enabled — replacing cert '{svc.SrvCert}' " +
                                $"with '{certId}' on {binding}");
                        }

                        var policyId = $"KF-{binding.VirtId}-{binding.ServicePort}";
                        await aClient.EnsureSslPolicyAsync(policyId);
                        await aClient.BindCertificateDirectAsync(binding, certId, policyId);
                        logger.LogDebug($"Bound cert '{certId}' to {binding} (non-SNI)");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to bind to {binding}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                complete.FailureMessage = string.Join("; ", errors);
                return complete;
            }

            // ── Apply + Save once after all bindings ─────────────────────────
            await aClient.ApplyAndSave();

            complete.Result = OrchestratorJobStatusJobResult.Success;
            return complete;
        }

        /// <summary>
        /// Clears all virtual service bindings for the given certificate.
        ///
        /// For non-SNI bindings: clears SrvCert, leaves SslPolName untouched.
        /// For SNI bindings:
        ///   - If cert is the DEFAULT cert for a group → fails with clear message
        ///   - If cert is a non-default member → removes it from the group
        ///
        /// Apply+Save is called once if any changes were made.
        /// Succeeds silently if the certificate has no bindings at all.
        /// </summary>
        internal async Task<JobResult> PerformBindingsForRemoveAsync(
            string certId, long jobHistoryId)
        {
            var complete = new JobResult
            {
                Result = OrchestratorJobStatusJobResult.Failure,
                JobHistoryId = jobHistoryId
            };

            try
            {
                var allServices = await aClient.GetAllVirtualServicesAsync();
                var allGroups = await aClient.GetAllCertGroupsAsync();

                var errors = new List<string>();
                var changed = false;

                // ── Non-SNI: clear SrvCert where it matches ───────────────────
                foreach (var svc in allServices)
                {
                    if (!string.Equals(svc.SrvCert, certId,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    var binding = new VirtualServiceBinding(svc.VirtIndex, svc.ServicePort);
                    try
                    {
                        // Pass empty string to clear — leave SslPolName alone
                        await aClient.BindCertificateDirectAsync(binding, string.Empty, null);
                        changed = true;
                        logger.LogDebug($"Cleared cert binding on {binding} (non-SNI)");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to clear binding on {binding}: {ex.Message}");
                    }
                }

                // ── SNI: handle cert group membership ─────────────────────────
                // Build lookup: groupId → virtual services using that group
                var groupToServices = allServices
                    .Where(s => !string.IsNullOrWhiteSpace(s.CertGroup))
                    .GroupBy(s => s.CertGroup)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var group in allGroups)
                {
                    if (!group.Certs.Any(c => string.Equals(c, certId,
                            StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Safety check: refuse if this cert is the group's default
                    if (string.Equals(group.DefaultCert, certId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        var affectedServices = groupToServices.TryGetValue(
                            group.ID, out var svcs)
                            ? string.Join(", ", svcs.Select(s =>
                                $"{s.VirtIndex}:{s.ServicePort}"))
                            : "unknown";

                        errors.Add(
                            $"Cannot remove cert '{certId}' — it is the default cert " +
                            $"for group '{group.ID}' (used by virtual service(s): " +
                            $"{affectedServices}). " +
                            $"Reassign the default cert on group '{group.ID}' before removing.");
                        continue;
                    }

                    try
                    {
                        await aClient.RemoveCertFromGroupAsync(group.ID, certId);
                        changed = true;
                        logger.LogDebug($"Removed cert '{certId}' from SNI group '{group.ID}'");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to remove from group '{group.ID}': {ex.Message}");
                    }
                }

                if (errors.Count > 0)
                {
                    complete.FailureMessage = string.Join("; ", errors);
                    return complete;
                }

                // Apply + Save only if something changed
                if (changed)
                    await aClient.ApplyAndSave();

                complete.Result = OrchestratorJobStatusJobResult.Success;
                return complete;
            }
            catch (Exception ex)
            {
                logger.LogError($"PerformBindingsForRemoveAsync({certId}) failed: {ex.Message}", ex);
                complete.FailureMessage = ex.Message;
                return complete;
            }
        }

        // ── SNI helper ────────────────────────────────────────────────────────

        private async Task HandleSniBindingAsync(string groupId, string certId)
        {
            var group = await aClient.GetCertGroupAsync(groupId);

            if (group == null)
            {
                // Group referenced by virtual service doesn't exist yet — create it
                logger.LogDebug($"Cert group '{groupId}' not found — creating");
                await aClient.CreateCertGroupAsync(groupId, certId);
            }
            else
            {
                // Group exists — add cert to it (idempotent)
                await aClient.AddCertToGroupAsync(groupId, certId);
            }
        }

        // ── PFX extraction helpers (unchanged) ────────────────────────────────

        private (string, string) GetPemFromPfx(byte[] pfxBytes, string pfxPassword)
        {
            try
            {
                logger.MethodEntry();

                CertificateCollectionConverter converter =
                    CertificateCollectionConverterFactory.FromDER(pfxBytes, pfxPassword);
                string pfxPem = converter.ToPEM(pfxPassword);
                List<X509Certificate2> clist = converter.ToX509Certificate2List(pfxPassword);
                StringBuilder certPemBuilder = new StringBuilder();

                if (clist.Count > 1)
                    clist = ReorderPEMLIst(clist);

                logger.LogTrace("Building certificate PEM");
                foreach (X509Certificate2 cert in clist)
                {
                    certPemBuilder.AppendLine("-----BEGIN CERTIFICATE-----");
                    certPemBuilder.AppendLine(
                        Convert.ToBase64String(cert.RawData, Base64FormattingOptions.InsertLineBreaks));
                    certPemBuilder.AppendLine("-----END CERTIFICATE-----");
                }

                logger.LogTrace("Building the key PEM");
                byte[] pkBytes = PKI.PrivateKeys.PrivateKeyConverterFactory
                    .FromPKCS12(pfxBytes, pfxPassword.ToString())
                    .ToPkcs8BlobUnencrypted();
                StringBuilder keyPemBuilder = new StringBuilder();
                keyPemBuilder.AppendLine("-----BEGIN PRIVATE KEY-----");
                keyPemBuilder.AppendLine(
                    Convert.ToBase64String(pkBytes, Base64FormattingOptions.InsertLineBreaks));
                keyPemBuilder.AppendLine("-----END PRIVATE KEY-----");

                logger.LogTrace($"certPem: {certPemBuilder}");
                logger.MethodExit();
                return (certPemBuilder.ToString(), keyPemBuilder.ToString());
            }
            catch (Exception e)
            {
                logger.LogError(
                    $"Error Occurred in GetPemFromPfx(byte[] pfxBytes, string pfxPassword): {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        private List<X509Certificate2> ReorderPEMLIst(List<X509Certificate2> certList)
        {
            List<X509Certificate2> rtnList = new List<X509Certificate2>();
            X509Certificate2 root = certList.FirstOrDefault(
                p => p.IssuerName.RawData.SequenceEqual(p.SubjectName.RawData));

            if (root == null || string.IsNullOrEmpty(root.SerialNumber))
                throw new Exception("Invalid certificate chain.  No root CA certificate found.");

            rtnList.Add(root);

            X509Certificate2 parentCert = root;
            for (int i = 1; i < certList.Count; i++)
            {
                X509Certificate2 childCert = certList.FirstOrDefault(
                    p => p.IssuerName.RawData.SequenceEqual(parentCert.SubjectName.RawData)
                      && !p.IssuerName.RawData.SequenceEqual(p.SubjectName.RawData));

                if (childCert == null || string.IsNullOrEmpty(childCert.SerialNumber))
                    throw new Exception("Invalid certificate chain.  End entity or issuing CA certificate not found.");

                rtnList.Insert(0, childCert);
                parentCert = childCert;
            }

            return rtnList;
        }
    }
}