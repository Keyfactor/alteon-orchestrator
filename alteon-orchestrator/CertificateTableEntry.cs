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

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer
{
    /// <summary>
    /// A single row from SlbNewSslCfgCertsTable.
    /// Type values:     1=key  2=pair  3=cert  4=trustedCA  5=intermediateCA
    /// Generate values: 5=generated (cert content exists)  6=notGenerated (entry only)
    /// </summary>
    public record CertificateTableEntry
    {
        [JsonPropertyName("ID")]
        public string ID { get; init; }

        [JsonPropertyName("Type")]
        public int? Type { get; init; }                 // 1=key 2=pair 3=cert 4=trustedCA 5=intermediateCA

        [JsonPropertyName("Name")]
        public string Name { get; init; }

        [JsonPropertyName("KeySize")]
        public int? KeySize { get; init; }              // 1=ks512 2=ks1024 3=ks2048 4=ks4096 6=unknown

        [JsonPropertyName("Expirty")]                   // note: typo is in the Alteon API itself
        public string Expirty { get; init; }

        [JsonPropertyName("CommonName")]
        public string CommonName { get; init; }

        [JsonPropertyName("HashAlgo")]
        public int? HashAlgo { get; init; }             // 1=md5 2=sha1 3=sha256 4=sha384 5=sha512 6=unknown

        [JsonPropertyName("CountryName")]
        public string CountryName { get; init; }

        [JsonPropertyName("PrpvinceName")]              // note: typo is in the Alteon API itself
        public string PrpvinceName { get; init; }

        [JsonPropertyName("LocalityName")]
        public string LocalityName { get; init; }

        [JsonPropertyName("OrganizationName")]
        public string OrganizationName { get; init; }

        [JsonPropertyName("OrganizationUnitName")]
        public string OrganizationUnitName { get; init; }

        [JsonPropertyName("EMail")]
        public string EMail { get; init; }

        [JsonPropertyName("ValidityPeriod")]
        public int? ValidityPeriod { get; init; }

        [JsonPropertyName("DeleteStatus")]
        public int? DeleteStatus { get; init; }         // 1=other 2=delete

        [JsonPropertyName("Generate")]
        public int? Generate { get; init; }             // 1=other 2=generate 3=idle 4=inprogress 5=generated 6=notGenerated

        [JsonPropertyName("Status")]
        public int? Status { get; init; }               // 1=generated 2=notGenerated 3=inProgress

        [JsonPropertyName("KeyType")]
        public int? KeyType { get; init; }              // 1=rsa 2=ec 6=unknown

        [JsonPropertyName("KeySizeEc")]
        public int? KeySizeEc { get; init; }            // 0=ks0 1=ks192 2=ks224 3=ks256 4=ks384 5=ks521 6=unknown

        [JsonPropertyName("CurveNameEc")]
        public int? CurveNameEc { get; init; }          // see Alteon docs for full enum

        [JsonPropertyName("KeySizeCommon")]
        public int? KeySizeCommon { get; init; }
    }

    public record CertificateTableEntryCollection
    {
        [JsonPropertyName("SlbNewSslCfgCertsTable")]
        public List<CertificateTableEntry> SlbNewSslCfgCertsTable { get; init; } = new();
    }
}
