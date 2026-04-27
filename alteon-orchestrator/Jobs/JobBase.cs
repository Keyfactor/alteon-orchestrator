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

using System.Net.Http;
using System.Reflection;
using Keyfactor.Logging;
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

        public bool Overwrite { get; set; }

        public IPAMSecretResolver _resolver;

        internal protected ILogger logger { get; set; }

        internal protected AlteonLoadBalancerClient aClient { get; set; }

        // ── Production store initialization ───────────────────────────────────

        public void InitializeStore(InventoryJobConfiguration config)
        {
            logger.MethodEntry();
            LogPluginVersion();
            ServerUrl = config.CertificateStoreDetails.ClientMachine;
            Username = PAMUtilities.ResolvePAMField(_resolver, logger, "Server Username", config.ServerUsername);
            Password = PAMUtilities.ResolvePAMField(_resolver, logger, "Server Password", config.ServerPassword);
            aClient = new AlteonLoadBalancerClient(ServerUrl, Username, Password, logger);
            logger.LogTrace($"Configuration complete for inventory job.  Server Url = {ServerUrl}");
            logger.MethodExit();
        }

        public void InitializeStore(ManagementJobConfiguration config)
        {
            logger.MethodEntry();
            LogPluginVersion();
            ServerUrl = config.CertificateStoreDetails.ClientMachine;
            Username = PAMUtilities.ResolvePAMField(_resolver, logger, "Server Username", config.ServerUsername);
            Password = PAMUtilities.ResolvePAMField(_resolver, logger, "Server Password", config.ServerPassword);
            Overwrite = config.Overwrite;
            aClient = new AlteonLoadBalancerClient(ServerUrl, Username, Password, logger);
            logger.LogTrace($"Configuration complete for management job.  Server Url = {ServerUrl}, Overwrite = {Overwrite}, Certificate Alias = {config.JobCertificate.Alias}");
            logger.MethodExit();
        }

        // ── Testable store initialization (internal — used by unit tests) ─────
        // Accepts a pre-built HttpClient so MockHttp can intercept API calls.

        internal void InitializeStore(string serverUrl, string username,
                                      string password, HttpClient httpClient)
        {
            ServerUrl = serverUrl;
            Username = username;
            Password = password;
            aClient = new AlteonLoadBalancerClient(
                serverUrl, username, password, logger, httpClient);
        }

        protected void LogPluginVersion()
        {
            var targetAssembly = Assembly.GetExecutingAssembly();
            var assemblyName = targetAssembly?.GetName();
            var version = assemblyName?.Version;
            logger.LogTrace("\n");
            logger.LogTrace("----------------------------------");
            logger.LogTrace("Keyfactor Orchestrator Extension for Alteon Load Balancer");
            logger.LogTrace($"{assemblyName?.Name ?? "unknown"} v{version}");
            logger.LogTrace("----------------------------------\n");
        }
    }
}
