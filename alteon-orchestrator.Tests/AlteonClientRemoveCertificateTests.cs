// Copyright 2026 Keyfactor
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Tests
{
    /// <summary>
    /// Unit tests for AlteonLoadBalancerClient.RemoveCertificate.
    ///
    /// The bug: only the CERT_ONLY entry was deleted; the KEY_ONLY entry was left
    /// behind as an orphaned private key on the device.
    ///
    /// The fix: RemoveCertificate now fetches both CERT_ONLY and KEY_ONLY entries
    /// and issues a DELETE for each one found.
    /// </summary>
    public class AlteonClientRemoveCertificateTests
    {
        private const string BaseUrl  = "https://192.168.1.168";
        private const string Username = "admin";
        private const string Password = "admin";
        private const string Alias    = "my-cert";

        private const string CertRepoUrl = $"{BaseUrl}/config/SlbNewSslCfgCertsTable";
        private const string ConfigUrl   = $"{BaseUrl}/config";

        // Type integers as returned by AlteonCertTypes.AlteonCertTypeValue
        // KEY_ONLY  = 1
        // CERT_ONLY = 3
        private const int TypeKey  = 1;
        private const int TypeCert = 3;

        // MockHttp glob matching does not apply to query strings, so we match the
        // full URL exactly as GetCertificatesById constructs it.
        private static string CertLookupUrl(int type) =>
            $"{CertRepoUrl}?filter=ID:{Alias},Type:{type}&filtertype=exact&props=ID,Name,Type";

        // ── Happy path: cert + key both present ───────────────────────────────

        [Fact]
        public async Task RemoveCertificate_CertAndKeyExist_DeletesBoth()
        {
            var mock = new MockHttpMessageHandler();

            // Lookup: CERT_ONLY (type=3) — found
            mock.When(HttpMethod.Get, CertLookupUrl(TypeCert))
                .Respond("application/json",
                    AlteonResponseFactory.CertTableResponse(new[]
                    {
                        AlteonResponseFactory.CertEntry(Alias, type: TypeCert)
                    }));

            // Lookup: KEY_ONLY (type=1) — found
            mock.When(HttpMethod.Get, CertLookupUrl(TypeKey))
                .Respond("application/json",
                    AlteonResponseFactory.CertTableResponse(new[]
                    {
                        AlteonResponseFactory.KeyEntry(Alias)
                    }));

            // DELETE cert entry
            mock.When(HttpMethod.Delete, $"{CertRepoUrl}/{Alias}/{TypeCert}")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            // DELETE key entry  <- the one that was previously missing
            mock.When(HttpMethod.Delete, $"{CertRepoUrl}/{Alias}/{TypeKey}")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            SetupApplyAndSave(mock);

            var client = BuildClient(mock);
            await client.Invoking(c => c.RemoveCertificate(Alias))
                        .Should().NotThrowAsync();
        }

        // ── Cert-only (no private key, e.g. CA / intermediate) ────────────────

        [Fact]
        public async Task RemoveCertificate_CertOnlyNoKey_DeletesCertOnly()
        {
            var mock = new MockHttpMessageHandler();

            // CERT_ONLY found
            mock.When(HttpMethod.Get, CertLookupUrl(TypeCert))
                .Respond("application/json",
                    AlteonResponseFactory.CertTableResponse(new[]
                    {
                        AlteonResponseFactory.CertEntry(Alias, type: TypeCert)
                    }));

            // KEY_ONLY not found — empty table (e.g. CA / intermediate cert)
            mock.When(HttpMethod.Get, CertLookupUrl(TypeKey))
                .Respond("application/json", AlteonResponseFactory.EmptyCertTableResponse());

            // Only the cert DELETE should fire — no key entry to remove
            mock.When(HttpMethod.Delete, $"{CertRepoUrl}/{Alias}/{TypeCert}")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            SetupApplyAndSave(mock);

            var client = BuildClient(mock);
            await client.Invoking(c => c.RemoveCertificate(Alias))
                        .Should().NotThrowAsync();
        }

        // ── Cert not found — throws ────────────────────────────────────────────

        [Fact]
        public async Task RemoveCertificate_CertNotFound_Throws()
        {
            var mock = new MockHttpMessageHandler();

            mock.When(HttpMethod.Get, CertLookupUrl(TypeCert))
                .Respond("application/json", AlteonResponseFactory.EmptyCertTableResponse());

            // KEY lookup should never be reached — guard clause exits first
            var client = BuildClient(mock);
            await client.Invoking(c => c.RemoveCertificate(Alias))
                        .Should().ThrowAsync<Exception>()
                        .WithMessage($"*{Alias}*not found*");
        }

        // ── DELETE of key entry fails — throws with type info ─────────────────

        [Fact]
        public async Task RemoveCertificate_KeyDeleteFails_ThrowsWithTypeContext()
        {
            var mock = new MockHttpMessageHandler();

            mock.When(HttpMethod.Get, CertLookupUrl(TypeCert))
                .Respond("application/json",
                    AlteonResponseFactory.CertTableResponse(new[]
                    {
                        AlteonResponseFactory.CertEntry(Alias, type: TypeCert)
                    }));

            mock.When(HttpMethod.Get, CertLookupUrl(TypeKey))
                .Respond("application/json",
                    AlteonResponseFactory.CertTableResponse(new[]
                    {
                        AlteonResponseFactory.KeyEntry(Alias)
                    }));

            // Cert DELETE succeeds
            mock.When(HttpMethod.Delete, $"{CertRepoUrl}/{Alias}/{TypeCert}")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            // Key DELETE fails — returns 500
            mock.When(HttpMethod.Delete, $"{CertRepoUrl}/{Alias}/{TypeKey}")
                .Respond(HttpStatusCode.InternalServerError, "application/json",
                    AlteonResponseFactory.ErrorResponse("device error"));

            var client = BuildClient(mock);
            await client.Invoking(c => c.RemoveCertificate(Alias))
                        .Should().ThrowAsync<Exception>()
                        .WithMessage($"*alias={Alias}*type={TypeKey}*");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SetupApplyAndSave(MockHttpMessageHandler mock)
        {
            // WaitForApplyIdleAsync — state 2 = idle, safe to proceed
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/agApplyConfig")
                .Respond("application/json", "{\"agApplyConfig\":2}");

            // ApplyChanges + SaveChanges
            mock.When(HttpMethod.Post, ConfigUrl)
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            // LogApplyTable
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/agApplyTable")
                .Respond("application/json", AlteonResponseFactory.OkResponse());
        }

        private static AlteonLoadBalancerClient BuildClient(MockHttpMessageHandler mock) =>
            new AlteonLoadBalancerClient(
                BaseUrl, Username, Password,
                NullLogger.Instance,
                mock.ToHttpClient());
    }
}
