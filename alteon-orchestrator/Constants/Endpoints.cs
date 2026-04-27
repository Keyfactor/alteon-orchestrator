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

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer
{
    public static class Endpoints
    {
        // ── https://<alteon IP>:<port>/**Endpoint** ───────────────────────────
        public const string CertificateRepository  = "config/SlbNewSslCfgCertsTable";
        public const string CertificateContent     = "config/getcert";
        public const string AddCertificate         = "config/sslcertimport";
        public const string Config                 = "config";
        public const string ApplyTable             = "config/AgApplyTable";

        // ── Virtual server / service tables ──────────────────────────────────
        // Virtual servers (keyed by VirtServerIndex = name string)
        public const string VirtualServers         = "config/SlbNewCfgEnhVirtServerTable";

        // Virtual services — main table (keyed by ServIndex/Index, contains VirtPort)
        public const string VirtualServices        = "config/SlbNewCfgEnhVirtServicesTable";

        // Virtual services — second part (same key, contains ServCert + SSLpol)
        public const string VirtualServicesSecond  = "config/SlbNewCfgEnhVirtServicesSecondPartTable";

        // Virtual services — fifth part (same key, contains ServCertGrpMark)
        public const string VirtualServicesFifth   = "config/SlbNewCfgEnhVirtServicesFifthPartTable";

        // ── SSL ───────────────────────────────────────────────────────────────
        // SSL policies (keyed by NameIdIndex = policy name string)
        public const string SslPolicies            = "config/SlbNewSslCfgSSLPolTable";

        // SSL cert groups for SNI
        public const string CertGroups             = "config/SlbNewSslCfgGroupsTable";
    }
}
