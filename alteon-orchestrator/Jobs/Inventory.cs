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
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Jobs
{
    public class Inventory : JobBase, IInventoryJobExtension
    {
        // ── Production constructor ────────────────────────────────────────────

        public Inventory(IPAMSecretResolver resolver)
        {
            _resolver = resolver;
            logger = LogHandler.GetClassLogger<Inventory>();
        }

        // ── Testable constructor (internal — used by unit tests only) ─────────

        internal Inventory(IPAMSecretResolver resolver,
                           string serverUrl, string username, string password,
                           ILogger logger, HttpClient httpClient)
        {
            _resolver = resolver;
            this.logger = logger;
            InitializeStore(serverUrl, username, password, httpClient);
        }

        // ── ProcessJob ────────────────────────────────────────────────────────

        public JobResult ProcessJob(InventoryJobConfiguration config,
                                    SubmitInventoryUpdate submitInventoryUpdate)
        {
            // Production path: initialize from job config
            if (aClient == null)
                InitializeStore(config);

            var certs = new List<CurrentInventoryItem>();

            try
            {
                // ── Fetch all data in 3 API calls ────────────────────────────
                // 1. Certificate repository
                var tableCerts = aClient.GetCertificates().GetAwaiter().GetResult();
                var certsOnly = tableCerts.SlbNewSslCfgCertsTable
                                           .Where(c => c.Type == 3 && c.Generate == 5)
                                           .ToList();
                var keysOnly = tableCerts.SlbNewSslCfgCertsTable
                                           .Where(c => c.Type == 1);

                // 2. All virtual services (one call, filter in memory)
                var allServices = aClient.GetAllVirtualServicesAsync()
                                         .GetAwaiter().GetResult();

                // 3. All cert groups for SNI bindings (one call)
                var allGroups = aClient.GetAllCertGroupsAsync()
                                       .GetAwaiter().GetResult();

                // Build lookup: groupId → virtual services referencing that group
                var groupToServices = allServices
                    .Where(s => !string.IsNullOrWhiteSpace(s.CertGroup))
                    .GroupBy(s => s.CertGroup)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // ── Build inventory items ────────────────────────────────────
                certsOnly.ForEach(certEntry =>
                {
                    var certContent = aClient.GetCertificateContent(certEntry.ID);
                    var bindings = new List<string>();

                    // Non-SNI bindings: virtual services with SrvCert = this cert
                    foreach (var svc in allServices)
                    {
                        if (string.Equals(svc.SrvCert, certEntry.ID,
                                StringComparison.OrdinalIgnoreCase))
                            bindings.Add(new VirtualServiceBinding(
                                svc.VirtIndex, svc.ServicePort).ToString());
                    }

                    // SNI bindings: cert group membership
                    foreach (var group in allGroups)
                    {
                        if (!group.Certs.Any(c => string.Equals(c, certEntry.ID,
                                StringComparison.OrdinalIgnoreCase)))
                            continue;

                        if (!groupToServices.TryGetValue(group.ID, out var services))
                            continue;

                        foreach (var svc in services)
                            bindings.Add(new VirtualServiceBinding(
                                svc.VirtIndex, svc.ServicePort).ToString());
                    }

                    certs.Add(new CurrentInventoryItem
                    {
                        Alias = certEntry.ID,
                        Certificates = new List<string> { certContent },
                        PrivateKeyEntry = keysOnly.Any(k => k.ID == certEntry.ID),
                        Parameters = new Dictionary<string, object>
                        {
                            // Empty string when unbound — never null,
                            // so Keyfactor can round-trip the value safely
                            ["VirtualServiceBindings"] = string.Join(",", bindings)
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ex.Message
                };
            }

            var success = submitInventoryUpdate.Invoke(certs);

            return new JobResult
            {
                Result = success
                                     ? OrchestratorJobStatusJobResult.Success
                                     : OrchestratorJobStatusJobResult.Failure,
                JobHistoryId = config.JobHistoryId,
                FailureMessage = success ? string.Empty : "Error executing SubmitInventoryUpdate"
            };
        }
    }
}
