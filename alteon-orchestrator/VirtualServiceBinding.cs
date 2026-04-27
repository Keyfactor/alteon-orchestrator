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
using System.Text.Json.Serialization;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer
{
    // ── Entry parameter model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a single virtual service binding: the combination of a virtual
    /// server name and service port to which a certificate is (or should be) bound.
    ///
    /// String format: "virtName:servicePort"  e.g. "webssl:443"
    /// List format (entry parameter): comma-separated e.g. "webssl:443,web:8443"
    /// </summary>
    public record VirtualServiceBinding(string VirtId, string ServicePort)
    {
        public override string ToString() => $"{VirtId}:{ServicePort}";

        public static VirtualServiceBinding Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException(
                    $"Invalid virtual service binding '{raw}'. " +
                    "Expected format: 'virtName:servicePort' e.g. 'webssl:443'.");

            var parts = raw.Trim().Split(':', 2);

            if (parts.Length != 2
                || string.IsNullOrWhiteSpace(parts[0])
                || string.IsNullOrWhiteSpace(parts[1]))
                throw new ArgumentException(
                    $"Invalid virtual service binding '{raw}'. " +
                    "Expected format: 'virtName:servicePort' e.g. 'webssl:443'.");

            if (!int.TryParse(parts[1].Trim(), out var port) || port < 1 || port > 65535)
                throw new ArgumentException(
                    $"Invalid virtual service binding '{raw}'. " +
                    $"Service port must be a number between 1 and 65535 — got '{parts[1].Trim()}'.");

            return new VirtualServiceBinding(parts[0].Trim(), parts[1].Trim());
        }

        public static List<VirtualServiceBinding> ParseList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new List<VirtualServiceBinding>();

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(Parse)
                .ToList();
        }
    }

    // ── Virtual server DTOs (SlbNewCfgEnhVirtServerTable) ─────────────────────

    /// <summary>
    /// A row from SlbNewCfgEnhVirtServerTable.
    /// VirtServerIndex is the virtual server name (e.g. "webssl") and is also
    /// the ServIndex key used in all virtual service part tables.
    /// </summary>
    public record VirtServerEntry
    {
        [JsonPropertyName("VirtServerIndex")]
        public string VirtServerIndex { get; init; }
    }

    public record VirtServerTableResponse
    {
        // Alteon appends "E*!" to enhanced table names in GET-all responses.
        [JsonPropertyName("SlbNewCfgEnhVirtServerTableE*!")]
        public List<VirtServerEntry> Entries { get; init; } = new();
    }

    // ── Virtual service DTOs (SlbNewCfgEnhVirtServicesTable) ──────────────────

    /// <summary>
    /// A row from SlbNewCfgEnhVirtServicesTable.
    /// Keyed by ServIndex (virtual server name) + Index (service index integer).
    /// VirtPort is the TCP port — used to find which Index corresponds to port 443.
    /// </summary>
    public record VirtServiceEntry
    {
        [JsonPropertyName("ServIndex")]
        public string ServIndex { get; init; }   // virtual server name e.g. "webssl"

        [JsonPropertyName("Index")]
        public int Index { get; init; }           // numeric service index e.g. 1

        [JsonPropertyName("VirtPort")]
        public int VirtPort { get; init; }        // TCP port e.g. 443
    }

    public record VirtServiceTableResponse
    {
        [JsonPropertyName("SlbNewCfgEnhVirtServicesTable")]
        public List<VirtServiceEntry> Entries { get; init; } = new();
    }

    // ── Virtual service second-part DTOs (SlbNewCfgEnhVirtServicesSecondPartTable)

    /// <summary>
    /// A row from SlbNewCfgEnhVirtServicesSecondPartTable.
    /// Same key as VirtServiceEntry (ServIndex + Index).
    /// Contains ServCert (certificate name) and SSLpol (SSL policy name) —
    /// the two fields needed for certificate binding.
    /// </summary>
    public record VirtServiceSecondPartEntry
    {
        [JsonPropertyName("ServSecondPartIndex")]
        public string ServIndex { get; init; }

        [JsonPropertyName("SecondPartIndex")]
        public int Index { get; init; }

        [JsonPropertyName("ServCert")]
        public string ServCert { get; init; }

        [JsonPropertyName("SSLpol")]
        public string SSLpol { get; init; }
    }

    public record VirtServiceSecondPartTableResponse
    {
        [JsonPropertyName("SlbNewCfgEnhVirtServicesSecondPartTable")]
        public List<VirtServiceSecondPartEntry> Entries { get; init; } = new();
    }

    // ── Virtual service fifth-part DTOs (SlbNewCfgEnhVirtServicesFifthPartTable)

    /// <summary>
    /// A row from SlbNewCfgEnhVirtServicesFifthPartTable.
    /// Contains ServCertGrpMark which indicates whether ServCert refers to
    /// a single certificate (1=cert) or a certificate group for SNI (2=group).
    /// </summary>
    public record VirtServiceFifthPartEntry
    {
        [JsonPropertyName("ServFifthPartIndex")]
        public string ServIndex { get; init; }

        [JsonPropertyName("FifthPartIndex")]
        public int Index { get; init; }

        /// <summary>1 = single cert (non-SNI), 2 = cert group (SNI)</summary>
        [JsonPropertyName("ServCertGrpMark")]
        public int ServCertGrpMark { get; init; }
    }

    public record VirtServiceFifthPartTableResponse
    {
        [JsonPropertyName("SlbNewCfgEnhVirtServicesFifthPartTable")]
        public List<VirtServiceFifthPartEntry> Entries { get; init; } = new();
    }

    // ── Resolved virtual service (assembled from multiple part tables) ─────────

    /// <summary>
    /// A fully resolved virtual service, combining fields from the main table
    /// and the second/fifth part tables. Used internally by the client to make
    /// binding decisions without callers needing to know about the part tables.
    /// </summary>
    public record ResolvedVirtService
    {
        public string ServIndex { get; init; }   // virtual server name
        public int    Index     { get; init; }   // numeric service index
        public int    VirtPort  { get; init; }   // TCP port

        public string ServCert     { get; init; }  // current cert name (or empty)
        public string SSLpol       { get; init; }  // current SSL policy (or empty)
        public int    CertGrpMark  { get; init; }  // 1=cert, 2=group
    }

    // ── SSL policy DTOs (SlbNewSslCfgSSLPolTable) ─────────────────────────────

    /// <summary>
    /// A row from SlbNewSslCfgSSLPolTable.
    /// Keyed by NameIdIndex (the policy name string).
    /// </summary>
    public record SslPolicyEntry
    {
        [JsonPropertyName("NameIdIndex")]
        public string NameIdIndex { get; init; }

        [JsonPropertyName("Name")]
        public string Name { get; init; }
    }

    public record SslPolicyTableResponse
    {
        [JsonPropertyName("SlbNewSslCfgSSLPolTable")]
        public List<SslPolicyEntry> Entries { get; init; } = new();
    }

    // ── Cert group DTOs (SlbNewSslCfgGroupsTable) ─────────────────────────────

    /// <summary>
    /// A row from SlbNewSslCfgGroupsTable.
    /// Used for SNI — multiple certificates served on the same VIP:port via a group.
    /// </summary>
    public record CertGroupEntry
    {
        [JsonPropertyName("ID")]
        public string ID { get; init; }

        [JsonPropertyName("Name")]
        public string Name { get; init; }

        [JsonPropertyName("DefaultCert")]
        public string DefaultCert { get; init; }

        [JsonPropertyName("Certs")]
        public List<string> Certs { get; init; } = new();
    }

    public record CertGroupTableResponse
    {
        [JsonPropertyName("SlbNewSslCfgGroupsTable")]
        public List<CertGroupEntry> Entries { get; init; } = new();
    }
}
