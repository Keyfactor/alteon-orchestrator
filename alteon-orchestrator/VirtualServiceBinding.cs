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
    /// server ID and service port to which a certificate is (or should be) bound.
    ///
    /// String format: "virtId:servicePort"  e.g. "1:443" or "my-virt:8443"
    /// List format (entry parameter): comma-separated e.g. "1:443,2:443,my-virt:8443"
    /// </summary>
    public record VirtualServiceBinding(string VirtId, string ServicePort)
    {
        public override string ToString() => $"{VirtId}:{ServicePort}";

        /// <summary>
        /// Parse a single "virtId:servicePort" binding string.
        /// Trims whitespace from each part.
        /// </summary>
        public static VirtualServiceBinding Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException(
                    $"Invalid virtual service binding '{raw}'. " +
                    "Expected format: 'virtId:servicePort' e.g. '1:443'.");

            var parts = raw.Trim().Split(':', 2);

            if (parts.Length != 2
                || string.IsNullOrWhiteSpace(parts[0])
                || string.IsNullOrWhiteSpace(parts[1]))
                throw new ArgumentException(
                    $"Invalid virtual service binding '{raw}'. " +
                    "Expected format: 'virtId:servicePort' e.g. '1:443'. " +
                    "Note: virtual server IDs containing ':' or ',' are not supported.");

            // Port must be a valid TCP port number (1–65535).
            // This also catches the case where a ':' in the virtId has caused
            // the port segment to contain non-numeric characters.
            if (!int.TryParse(parts[1].Trim(), out var port)
                || port < 1 || port > 65535)
                throw new ArgumentException(
                    $"Invalid virtual service binding '{raw}'. " +
                    $"Service port must be a number between 1 and 65535 — " +
                    $"got '{parts[1].Trim()}'. " +
                    $"Note: virtual server IDs containing ':' or ',' are not supported.");

            return new VirtualServiceBinding(parts[0].Trim(), parts[1].Trim());
        }

        /// <summary>
        /// Parse a comma-separated list of bindings from an entry parameter value.
        /// Empty entries (e.g. trailing commas) are silently ignored.
        /// </summary>
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

    // ── Virtual service API DTOs ──────────────────────────────────────────────

    /// <summary>
    /// A single row from SlbNewCfgVirtServicesTable.
    /// SrvCert populated  → non-SNI direct binding.
    /// CertGroup populated → SNI binding via a certificate group.
    /// Neither populated  → service has no SSL certificate bound.
    /// </summary>
    public record VirtServiceEntry
    {
        [JsonPropertyName("Index")]
        public string VirtIndex { get; init; }

        [JsonPropertyName("ServIndex")]
        public string ServicePort { get; init; }

        [JsonPropertyName("SrvCert")]
        public string SrvCert { get; init; }

        [JsonPropertyName("CertGroup")]
        public string CertGroup { get; init; }

        [JsonPropertyName("SslPolName")]
        public string SslPolName { get; init; }
    }

    public record VirtServiceTableResponse
    {
        [JsonPropertyName("SlbNewCfgVirtServicesTable")]
        public List<VirtServiceEntry> Entries { get; init; } = new();
    }

    // ── Cert group API DTOs ───────────────────────────────────────────────────

    /// <summary>
    /// A single row from SlbNewCfgSslCertGroupTable.
    /// Used for SNI — multiple certificates served on the same VIP:port.
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
        [JsonPropertyName("SlbNewCfgSslCertGroupTable")]
        public List<CertGroupEntry> Entries { get; init; } = new();
    }
}