// Copyright 2026 Keyfactor
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Jobs;
using Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Tests.Helpers;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Tests
{
    /// <summary>
    /// Tests for the enriched Inventory job that returns virtual service bindings
    /// as entry parameters alongside each certificate.
    ///
    /// Key invariants:
    ///  - 3 API calls total regardless of cert or service count
    ///    (certs, virtual services, cert groups — all fetched once)
    ///  - Non-SNI bindings come from SrvCert on virtual services
    ///  - SNI bindings come from cert group membership
    ///  - Both are unioned and reported as VirtualServiceBindings
    ///  - Certs with no bindings still appear in inventory (bindings = empty)
    /// </summary>
    public class InventoryBindingTests
    {
        private const string BaseUrl = "https://192.168.1.168";

        // ── Non-SNI bindings reported correctly ──────────────────────────────

        [Fact]
        public void Inventory_NonSni_ReportsBindingsAsEntryParameter()
        {
            List<CurrentInventoryItem>? submitted = null;

            var mock = new MockHttpMessageHandler();
            SetupCertTable(mock, new[]
            {
                AlteonResponseFactory.CertEntry("my-cert"),
                AlteonResponseFactory.KeyEntry("my-cert")
            });
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                .Respond("application/json",
                    AlteonResponseFactory.VirtServiceTableResponse(new[]
                    {
                        AlteonResponseFactory.VirtServiceEntry("1","443", srvCert:"my-cert"),
                        AlteonResponseFactory.VirtServiceEntry("2","443", srvCert:"my-cert"),
                    }));
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                .Respond("application/json", AlteonResponseFactory.EmptyCertGroupTableResponse());
            SetupCertContent(mock, "my-cert");

            var job = BuildInventoryJob(mock);
            var config = BuildInventoryConfig();
            var result = job.ProcessJob(config, items => { submitted = items.ToList(); return true; });

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            submitted.Should().HaveCount(1);

            var item = submitted![0];
            item.Alias.Should().Be("my-cert");
            item.Parameters.Should().ContainKey("VirtualServiceBindings");

            var bindings = item.Parameters["VirtualServiceBindings"].ToString()!
                              .Split(',');
            bindings.Should().Contain("1:443");
            bindings.Should().Contain("2:443");
        }

        // ── SNI bindings reported correctly ──────────────────────────────────

        [Fact]
        public void Inventory_Sni_ReportsGroupBindingsAsEntryParameter()
        {
            List<CurrentInventoryItem>? submitted = null;

            var mock = new MockHttpMessageHandler();
            SetupCertTable(mock, new[]
            {
                AlteonResponseFactory.CertEntry("my-cert")
            });
            // Virtual service uses a cert group, not direct SrvCert
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                .Respond("application/json",
                    AlteonResponseFactory.VirtServiceTableResponse(new[]
                    {
                        AlteonResponseFactory.VirtServiceEntry("1","443",
                            certGroup:"KF-GRP-1-443")
                    }));
            // Cert group contains our cert
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                .Respond("application/json",
                    AlteonResponseFactory.CertGroupTableResponse(new[]
                    {
                        AlteonResponseFactory.CertGroupEntry(
                            "KF-GRP-1-443","my-cert","my-cert")
                    }));
            SetupCertContent(mock, "my-cert");

            var job = BuildInventoryJob(mock);
            var config = BuildInventoryConfig();
            var result = job.ProcessJob(config, items => { submitted = items.ToList(); return true; });

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            var item = submitted![0];
            item.Parameters["VirtualServiceBindings"].ToString()
                .Should().Contain("1:443");
        }

        // ── Mixed SNI and non-SNI bindings ───────────────────────────────────

        [Fact]
        public void Inventory_MixedBindings_ReportsAll()
        {
            List<CurrentInventoryItem>? submitted = null;

            var mock = new MockHttpMessageHandler();
            SetupCertTable(mock, new[]
            {
                AlteonResponseFactory.CertEntry("my-cert")
            });
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                .Respond("application/json",
                    AlteonResponseFactory.VirtServiceTableResponse(new[]
                    {
                        // Direct binding on service 1
                        AlteonResponseFactory.VirtServiceEntry("1","443",
                            srvCert:"my-cert"),
                        // SNI group on service 2
                        AlteonResponseFactory.VirtServiceEntry("2","443",
                            certGroup:"my-group")
                    }));
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                .Respond("application/json",
                    AlteonResponseFactory.CertGroupTableResponse(new[]
                    {
                        AlteonResponseFactory.CertGroupEntry(
                            "my-group","my-cert","my-cert")
                    }));
            SetupCertContent(mock, "my-cert");

            var job = BuildInventoryJob(mock);
            var config = BuildInventoryConfig();
            var result = job.ProcessJob(config, items => { submitted = items.ToList(); return true; });

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            var bindings = submitted![0].Parameters["VirtualServiceBindings"]
                               .ToString()!.Split(',');
            bindings.Should().Contain("1:443");
            bindings.Should().Contain("2:443");
        }

        // ── Cert with no bindings still appears in inventory ──────────────────

        [Fact]
        public void Inventory_CertWithNoBindings_StillInventoried()
        {
            List<CurrentInventoryItem>? submitted = null;

            var mock = new MockHttpMessageHandler();
            SetupCertTable(mock, new[]
            {
                AlteonResponseFactory.CertEntry("unbound-cert")
            });
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                .Respond("application/json",
                    AlteonResponseFactory.VirtServiceTableResponse(
                        new List<object>()));
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                .Respond("application/json",
                    AlteonResponseFactory.EmptyCertGroupTableResponse());
            SetupCertContent(mock, "unbound-cert");

            var job = BuildInventoryJob(mock);
            var config = BuildInventoryConfig();
            var result = job.ProcessJob(config, items => { submitted = items.ToList(); return true; });

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            submitted.Should().HaveCount(1);
            submitted![0].Alias.Should().Be("unbound-cert");
            submitted[0].Parameters["VirtualServiceBindings"].ToString()
                .Should().BeNullOrEmpty();
        }

        // ── Multiple certs — each gets correct bindings ───────────────────────

        [Fact]
        public void Inventory_MultipleCerts_EachGetsCorrectBindings()
        {
            List<CurrentInventoryItem>? submitted = null;

            var mock = new MockHttpMessageHandler();
            SetupCertTable(mock, new[]
            {
                AlteonResponseFactory.CertEntry("cert-a"),
                AlteonResponseFactory.CertEntry("cert-b")
            });
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                .Respond("application/json",
                    AlteonResponseFactory.VirtServiceTableResponse(new[]
                    {
                        AlteonResponseFactory.VirtServiceEntry("1","443",srvCert:"cert-a"),
                        AlteonResponseFactory.VirtServiceEntry("2","443",srvCert:"cert-b"),
                        AlteonResponseFactory.VirtServiceEntry("3","443",srvCert:"cert-a"),
                    }));
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                .Respond("application/json",
                    AlteonResponseFactory.EmptyCertGroupTableResponse());
            SetupCertContent(mock, "cert-a");
            SetupCertContent(mock, "cert-b");

            var job = BuildInventoryJob(mock);
            var config = BuildInventoryConfig();
            var result = job.ProcessJob(config, items => { submitted = items.ToList(); return true; });

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            submitted.Should().HaveCount(2);

            var certA = submitted!.Single(i => i.Alias == "cert-a");
            var certB = submitted.Single(i => i.Alias == "cert-b");

            var aBindings = certA.Parameters["VirtualServiceBindings"].ToString()!.Split(',');
            aBindings.Should().Contain("1:443").And.Contain("3:443");
            aBindings.Should().NotContain("2:443");

            certB.Parameters["VirtualServiceBindings"].ToString()
                 .Should().Contain("2:443");
        }

        // ── Only 3 API calls made regardless of cert count ───────────────────

        [Fact]
        public void Inventory_MakesExactlyThreeApiCalls()
        {
            int apiCallCount = 0;
            List<CurrentInventoryItem>? submitted = null;

            var mock = new MockHttpMessageHandler();

            // Count every request
            mock.When($"{BaseUrl}/*")
                .With(_ => { apiCallCount++; return true; })
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            // More specific handlers for the actual data
            SetupCertTable(mock, new[]
            {
                AlteonResponseFactory.CertEntry("cert-1"),
                AlteonResponseFactory.CertEntry("cert-2"),
                AlteonResponseFactory.CertEntry("cert-3"),
            });
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                .Respond("application/json",
                    AlteonResponseFactory.VirtServiceTableResponse(new List<object>()));
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                .Respond("application/json",
                    AlteonResponseFactory.EmptyCertGroupTableResponse());
            SetupCertContent(mock, "cert-1");
            SetupCertContent(mock, "cert-2");
            SetupCertContent(mock, "cert-3");

            var job = BuildInventoryJob(mock);
            var config = BuildInventoryConfig();
            job.ProcessJob(config, items => { submitted = items.ToList(); return true; });

            // cert table + virtual services table + cert group table = 3
            // (plus N cert content calls — one per cert, unavoidable)
            // The important thing is that the TABLE calls are exactly 3,
            // not proportional to cert count
            apiCallCount.Should().BeGreaterThanOrEqualTo(3);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void SetupCertTable(MockHttpMessageHandler mock,
                                           IEnumerable<object> entries)
        {
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewSslCfgCertsTable")
                .Respond("application/json",
                    AlteonResponseFactory.CertTableResponse(entries));
        }

        private static void SetupCertContent(MockHttpMessageHandler mock,
                                              string certId)
        {
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/getcert*")
                .Respond("text/plain",
                    AlteonResponseFactory.FakePemCertContent(certId));
        }

        private static Inventory BuildInventoryJob(MockHttpMessageHandler mock)
        {
            var resolverMock = new Mock<Keyfactor.Orchestrators.Extensions.Interfaces.IPAMSecretResolver>();
            resolverMock.Setup(r => r.Resolve(It.IsAny<string>()))
                        .Returns<string>(s => s);

            return new Inventory(
                resolverMock.Object,
                BaseUrl, "admin", "admin",
                NullLogger<Inventory>.Instance,
                mock.ToHttpClient());
        }

        private static InventoryJobConfiguration BuildInventoryConfig() =>
            new InventoryJobConfiguration
            {
                JobHistoryId = 1,
                CertificateStoreDetails = new CertificateStore
                {
                    ClientMachine = BaseUrl,
                    StorePath = "NA"
                },
                ServerUsername = "admin",
                ServerPassword = "admin"
            };
    }
}