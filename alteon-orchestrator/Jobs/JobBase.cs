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

using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Jobs
{
    public abstract class JobBase
    {
        public string ExtensionName => "";

        public string Username { get; set; }

        public string Password { get; set; }

        public string ServerUrl { get; set; }

        public IPAMSecretResolver _resolver;

        internal protected AlteonLoadBalancerClient aClient { get; set; }


        public void InitializeStore(InventoryJobConfiguration config, ILogger logger)
        {            
            ServerUrl = config.CertificateStoreDetails.ClientMachine;
            Username = PAMUtilities.ResolvePAMField(_resolver, logger, "Server User Name", config.ServerUsername);
            Password = PAMUtilities.ResolvePAMField(_resolver, logger, "Server Password", config.ServerPassword);
            aClient = new AlteonLoadBalancerClient(ServerUrl, Username, Password);
        }

        public void InitializeStore(ManagementJobConfiguration config, ILogger logger) {
            ServerUrl = config.CertificateStoreDetails.ClientMachine;
            Username = PAMUtilities.ResolvePAMField(_resolver, logger, "Server User Name", config.ServerUsername); 
            Password = PAMUtilities.ResolvePAMField(_resolver, logger, "Server Password", config.ServerPassword);
            aClient = new AlteonLoadBalancerClient(ServerUrl, Username, Password);
        }
    }
}
