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
    /// All field names match the actual Alteon API contract documented in
    /// Alteon_32_1_0_0_REST_API_User_Guide and confirmed via live device testing.
    /// </summary>
    public static class AlteonResponseFactory
    {
        // ── Certificate table ────────────────────────────────────────────────

        public static string CertTableResponse(IEnumerable<object> entries) =>
            JsonConvert.SerializeObject(new { SlbNewSslCfgCertsTable = entries });

        /// <summary>Type=3 Generate=5 = cert exists</summary>
        public static object CertEntry(string id, int type = 3, int generate = 5,
                                        string name = "") =>
            new { ID = id, Type = type, Generate = generate, Name = name };

        public static object KeyEntry(string id) =>
            new { ID = id, Type = 1, Generate = 5, Name = "" };

        public static string EmptyCertTableResponse() =>
            CertTableResponse(new List<object>());

        // ── Virtual server table (SlbNewCfgEnhVirtServerTable) ───────────────

        /// <summary>
        /// Response for GET /config/SlbNewCfgEnhVirtServerTable
        /// Alteon appends "E*!" to enhanced table names in GET-all responses.
        /// </summary>
        public static string VirtServerTableResponse(IEnumerable<object> entries) =>
            JsonConvert.SerializeObject(new
            {
                // Note the E*! suffix — this is how Alteon actually returns it
                SlbNewCfgEnhVirtServerTableEBang = entries
            }).Replace("SlbNewCfgEnhVirtServerTableEBang",
                       "SlbNewCfgEnhVirtServerTableE*!");

        public static object VirtServerEntry(string virtServerIndex) =>
            new { VirtServerIndex = virtServerIndex };

        public static string EmptyVirtServerTableResponse() =>
            VirtServerTableResponse(new List<object>());

        // ── Virtual services main table (SlbNewCfgEnhVirtServicesTable) ──────

        /// <summary>
        /// Response for GET /config/SlbNewCfgEnhVirtServicesTable
        /// Keyed by ServIndex (virtual server name) + Index (numeric service index).
        /// VirtPort is the TCP port.
        /// </summary>
        public static string VirtServiceTableResponse(IEnumerable<object> entries) =>
            JsonConvert.SerializeObject(new { SlbNewCfgEnhVirtServicesTable = entries });

        public static object VirtServiceEntry(string servIndex, int index, int virtPort) =>
            new { ServIndex = servIndex, Index = index, VirtPort = virtPort };

        public static string EmptyVirtServiceTableResponse() =>
            VirtServiceTableResponse(new List<object>());

        // ── Virtual services second-part table ───────────────────────────────

        /// <summary>
        /// Response for GET /config/SlbNewCfgEnhVirtServicesSecondPartTable
        /// Same key as main table. Contains ServCert and SSLpol.
        /// </summary>
        public static string VirtServiceSecondPartTableResponse(IEnumerable<object> entries) =>
            JsonConvert.SerializeObject(new
            {
                SlbNewCfgEnhVirtServicesSecondPartTable = entries
            });

        public static object VirtServiceSecondPartEntry(string servIndex, int index,
                                                         string servCert = "",
                                                         string sslPol = "") =>
            new
            {
                ServSecondPartIndex = servIndex,
                SecondPartIndex     = index,
                ServCert            = servCert,
                SSLpol              = sslPol
            };

        public static string EmptyVirtServiceSecondPartTableResponse() =>
            VirtServiceSecondPartTableResponse(new List<object>());

        // ── Virtual services fifth-part table ────────────────────────────────

        /// <summary>
        /// Response for GET /config/SlbNewCfgEnhVirtServicesFifthPartTable
        /// Contains ServCertGrpMark: 1=cert (non-SNI), 2=group (SNI).
        /// </summary>
        public static string VirtServiceFifthPartTableResponse(IEnumerable<object> entries) =>
            JsonConvert.SerializeObject(new
            {
                SlbNewCfgEnhVirtServicesFifthPartTable = entries
            });

        public static object VirtServiceFifthPartEntry(string servIndex, int index,
                                                        int certGrpMark = 1) =>
            new
            {
                ServFifthPartIndex = servIndex,
                FifthPartIndex     = index,
                ServCertGrpMark    = certGrpMark
            };

        public static string EmptyVirtServiceFifthPartTableResponse() =>
            VirtServiceFifthPartTableResponse(new List<object>());

        // ── Convenience: all three tables for a simple non-SNI setup ─────────

        /// <summary>
        /// Builds a consistent set of all three part-table responses for a single
        /// virtual service with a direct (non-SNI) cert binding.
        /// </summary>
        public static (string main, string second, string fifth) ResolvedServiceResponses(
            string servIndex, int index, int virtPort,
            string servCert = "", string sslPol = "", int certGrpMark = 1)
        {
            var main   = VirtServiceTableResponse(new[]
            {
                VirtServiceEntry(servIndex, index, virtPort)
            });
            var second = VirtServiceSecondPartTableResponse(new[]
            {
                VirtServiceSecondPartEntry(servIndex, index, servCert, sslPol)
            });
            var fifth  = VirtServiceFifthPartTableResponse(new[]
            {
                VirtServiceFifthPartEntry(servIndex, index, certGrpMark)
            });
            return (main, second, fifth);
        }

        // ── SSL policy table ─────────────────────────────────────────────────

        public static string SslPolicyTableResponse(IEnumerable<object> policies) =>
            JsonConvert.SerializeObject(new { SlbNewSslCfgSSLPolTable = policies });

        public static object SslPolicyEntry(string nameIdIndex, string name = "") =>
            new { NameIdIndex = nameIdIndex, Name = string.IsNullOrEmpty(name) ? nameIdIndex : name };

        // ── Cert group table ─────────────────────────────────────────────────

        public static string CertGroupTableResponse(IEnumerable<object> groups) =>
            JsonConvert.SerializeObject(new { SlbNewSslCfgGroupsTable = groups });

        public static object CertGroupEntry(string groupId, string defaultCert,
                                            params string[] memberCerts) =>
            new
            {
                ID          = groupId,
                Name        = groupId,
                DefaultCert = defaultCert,
                Certs       = memberCerts
            };

        public static string EmptyCertGroupTableResponse() =>
            CertGroupTableResponse(new List<object>());

        // ── Generic responses ────────────────────────────────────────────────

        public static string OkResponse() =>
            JsonConvert.SerializeObject(new { status = "ok", message = "" });

        public static string ErrorResponse(string message) =>
            JsonConvert.SerializeObject(new { status = "err", message });

        // ── Certificate content ──────────────────────────────────────────────

        public static string FakePemCertContent(string cn = "test.example.com") =>
            $"-----BEGIN CERTIFICATE-----\nMIIFakeCertFor{cn.Replace(".", "")}\n-----END CERTIFICATE-----\n";
    }
}
