// Copyright 2022 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Jobs
{
    public class Inventory : JobBase, IInventoryJobExtension
    {
        ILogger logger = LogHandler.GetClassLogger<Inventory>();
              

        public JobResult ProcessJob(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventoryUpdate)
        {
            InitializeStore(config);

            List<CurrentInventoryItem> certs = new List<CurrentInventoryItem>();
            try
            {
                var tableCerts = aClient.GetCertificates().Result;
                //"Generate" indicates whether a cert actually exists, or just the entry.  5 means it exists.
                var certsOnly = tableCerts.SlbNewSslCfgCertsTable.Where(c => c.Type == 3 && c.Generate == 5).ToList();
                var keysOnly = tableCerts.SlbNewSslCfgCertsTable.Where(c => c.Type == 1);

                certsOnly.ForEach(certEntry => {
                    var certStrings = new List<string>();
                    var certContent = aClient.GetCertificateContent(certEntry.ID);

                    certs.Add(new CurrentInventoryItem()
                    {
                        Alias = certEntry.ID,
                        Certificates = new List<string>() { certContent },
                        PrivateKeyEntry = keysOnly.Any(k => k.ID == certEntry.ID)
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
            var success = submitInventoryUpdate.Invoke(certs.ToList());

            return new JobResult
            {
                Result = success ? OrchestratorJobStatusJobResult.Success : OrchestratorJobStatusJobResult.Failure,
                JobHistoryId = config.JobHistoryId,
                FailureMessage = success ? string.Empty : "Error executing SubmitInventoryUpdate"
            };
        }
    }
}
