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
        // ── https://<alteon IP>:<port>/**Endpoint** ──────────────────────────────
        public const string CertificateRepository = "config/SlbNewSslCfgCertsTable";
        public const string CertificateContent = "config/getcert";
        public const string AddCertificate = "config/sslcertimport";
        public const string Config = "config";
        public const string ApplyTable = "config/AgApplyTable";        
        public const string VirtualServices = "config/SlbNewCfgVirtServicesTable";        
        public const string SslPolicies = "config/SlbNewCfgSslPolicyTable";        
        public const string CertGroups = "config/SlbNewCfgSslCertGroupTable";
    }
}
