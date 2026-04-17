// Copyright 2026 Keyfactor
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Tests.Helpers
{
    /// <summary>
    /// Builds JSON response bodies that match what the real Alteon REST API returns.
    /// All field names match the actual Alteon API contract observed during testing.
    /// </summary>
    public static class AlteonResponseFactory
    {
        // ── Certificate table ────────────────────────────────────────────────

        /// <summary>
        /// Builds the response body for GET /config/SlbNewSslCfgCertsTable
        /// Type values:  1=key, 2=pair, 3=cert, 4=trustedCA, 5=intermediateCA
        /// Generate values: 5=generated (cert exists), 6=notGenerated (entry only)
        /// </summary>
        public static string CertTableResponse(IEnumerable<object> entries) =>
            JsonConvert.SerializeObject(new
            {
                SlbNewSslCfgCertsTable = entries
            });

        /// <summary>Minimal cert-only entry (Type=3, Generate=5 means cert exists).</summary>
        public static object CertEntry(string id, int type = 3, int generate = 5,
                                        string name = "") =>
            new { ID = id, Type = type, Generate = generate, Name = name };

        /// <summary>Key-only entry (Type=1).</summary>
        public static object KeyEntry(string id) =>
            new { ID = id, Type = 1, Generate = 5, Name = "" };

        /// <summary>Empty cert table — device has no certificates.</summary>
        public static string EmptyCertTableResponse() =>
            CertTableResponse(new List<object>());

        // ── Virtual service table ────────────────────────────────────────────

        /// <summary>
        /// Builds the response body for GET /config/SlbNewCfgVirtServicesTable
        /// SrvCert populated  → non-SNI binding
        /// CertGroup populated → SNI binding
        /// Neither populated  → unbound service
        /// </summary>
        public static string VirtServiceTableResponse(IEnumerable<object> entries) =>
            JsonConvert.SerializeObject(new
            {
                SlbNewCfgVirtServicesTable = entries
            });

        /// <summary>Non-SNI virtual service entry with a direct cert binding.</summary>
        public static object VirtServiceEntry(string virtIndex, string servIndex,
                                              string srvCert = "",
                                              string certGroup = "",
                                              string sslPolName = "") =>
            new
            {
                Index     = virtIndex,
                ServIndex = servIndex,
                SrvCert   = srvCert,
                CertGroup = certGroup,
                SslPolName = sslPolName
            };

        // ── Individual virtual service ────────────────────────────────────────

        /// <summary>
        /// Response for GET /config/SlbNewCfgVirtServicesTable/{virtId}/{port}
        /// </summary>
        public static string SingleVirtServiceResponse(string virtIndex, string servIndex,
                                                       string srvCert = "",
                                                       string certGroup = "",
                                                       string sslPolName = "") =>
            JsonConvert.SerializeObject(new
            {
                SlbNewCfgVirtServicesTable = new[]
                {
                    VirtServiceEntry(virtIndex, servIndex, srvCert, certGroup, sslPolName)
                }
            });

        // ── Cert group table ─────────────────────────────────────────────────

        /// <summary>
        /// Builds the response body for GET /config/SlbNewCfgSslCertGroupTable
        /// </summary>
        public static string CertGroupTableResponse(IEnumerable<object> groups) =>
            JsonConvert.SerializeObject(new
            {
                SlbNewCfgSslCertGroupTable = groups
            });

        /// <summary>A cert group with its member cert IDs and a default cert.</summary>
        public static object CertGroupEntry(string groupId, string defaultCert,
                                            params string[] memberCerts) =>
            new
            {
                ID          = groupId,
                Name        = groupId,
                DefaultCert = defaultCert,
                Certs       = memberCerts
            };

        /// <summary>Empty cert group table.</summary>
        public static string EmptyCertGroupTableResponse() =>
            CertGroupTableResponse(new List<object>());

        // ── SSL policy table ─────────────────────────────────────────────────

        public static string SslPolicyTableResponse(IEnumerable<object> policies) =>
            JsonConvert.SerializeObject(new
            {
                SlbNewCfgSslPolicyTable = policies
            });

        public static object SslPolicyEntry(string id, string name = "",
                                             bool enabled = true) =>
            new { ID = id, Name = string.IsNullOrEmpty(name) ? id : name,
                  AdminStatus = enabled ? 2 : 1 };

        // ── Generic API responses ────────────────────────────────────────────

        /// <summary>Standard Alteon success response body.</summary>
        public static string OkResponse() =>
            JsonConvert.SerializeObject(new { status = "ok", message = "" });

        /// <summary>Standard Alteon error response body.</summary>
        public static string ErrorResponse(string message) =>
            JsonConvert.SerializeObject(new { status = "err", message });

        // ── Certificate content ──────────────────────────────────────────────

        /// <summary>Fake PEM certificate content returned by /config/getcert</summary>
        public static string FakePemCertContent(string cn = "test.example.com") =>
            $"-----BEGIN CERTIFICATE-----\nMIIFakeCertFor{cn.Replace(".", "")}\n-----END CERTIFICATE-----\n";
    }
}
