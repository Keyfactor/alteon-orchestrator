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
    public class InventoryBindingTests
    {
        private const string BaseUrl   = "https://192.168.1.168";
        private const string MainUrl   = $"{BaseUrl}/config/SlbNewCfgEnhVirtServicesTable";
        private const string SecondUrl = $"{BaseUrl}/config/SlbNewCfgEnhVirtServicesSecondPartTable";
        private const string FifthUrl  = $"{BaseUrl}/config/SlbNewCfgEnhVirtServicesFifthPartTable";
        private const string GroupUrl  = $"{BaseUrl}/config/SlbNewSslCfgGroupsTable";

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
            SetupThreePartTables(mock,
                main: new[]
                {
                    AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443),
                    AlteonResponseFactory.VirtServiceEntry("web",    1, 80)
                },
                second: new[]
                {
                    AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: "my-cert"),
                    AlteonResponseFactory.VirtServiceSecondPartEntry("web",    1, servCert: "my-cert")
                },
                fifth: new[]
                {
                    AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, certGrpMark: 1),
                    AlteonResponseFactory.VirtServiceFifthPartEntry("web",    1, certGrpMark: 1)
                });
            mock.When(HttpMethod.Get, GroupUrl)
                .Respond("application/json", AlteonResponseFactory.EmptyCertGroupTableResponse());
            SetupCertContent(mock, "my-cert");

            var job = BuildInventoryJob(mock);
            var result = job.ProcessJob(BuildInventoryConfig(),
                items => { submitted = items.ToList(); return true; });

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            submitted.Should().HaveCount(1);

            var bindings = submitted![0].Parameters["VirtualServiceBindings"]
                               .ToString()!.Split(',');
            bindings.Should().Contain("webssl:443");
            bindings.Should().Contain("web:80");
        }

        [Fact]
        public void Inventory_Sni_ReportsGroupBindingsAsEntryParameter()
        {
            List<CurrentInventoryItem>? submitted = null;

            var mock = new MockHttpMessageHandler();
            SetupCertTable(mock, new[] { AlteonResponseFactory.CertEntry("my-cert") });
            SetupThreePartTables(mock,
                main: new[] { AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443) },
                second: new[]
                {
                    // ServCert holds the group name when CertGrpMark == 2
                    AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: "KF-GRP-webssl-443")
                },
                fifth: new[]
                {
                    AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, certGrpMark: 2)
                });
            mock.When(HttpMethod.Get, GroupUrl)
                .Respond("application/json",
                    AlteonResponseFactory.CertGroupTableResponse(new[]
                    {
                        AlteonResponseFactory.CertGroupEntry(
                            "KF-GRP-webssl-443", "my-cert", "my-cert")
                    }));
            SetupCertContent(mock, "my-cert");

            var job = BuildInventoryJob(mock);
            var result = job.ProcessJob(BuildInventoryConfig(),
                items => { submitted = items.ToList(); return true; });

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            submitted![0].Parameters["VirtualServiceBindings"]
                .ToString().Should().Contain("webssl:443");
        }

        [Fact]
        public void Inventory_CertWithNoBindings_StillInventoried()
        {
            List<CurrentInventoryItem>? submitted = null;

            var mock = new MockHttpMessageHandler();
            SetupCertTable(mock, new[] { AlteonResponseFactory.CertEntry("unbound-cert") });
            SetupThreePartTables(mock,
                main:   new List<object>(),
                second: new List<object>(),
                fifth:  new List<object>());
            mock.When(HttpMethod.Get, GroupUrl)
                .Respond("application/json", AlteonResponseFactory.EmptyCertGroupTableResponse());
            SetupCertContent(mock, "unbound-cert");

            var job = BuildInventoryJob(mock);
            var result = job.ProcessJob(BuildInventoryConfig(),
                items => { submitted = items.ToList(); return true; });

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            submitted.Should().HaveCount(1);
            submitted![0].Parameters["VirtualServiceBindings"].ToString()
                .Should().BeNullOrEmpty();
        }

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
            SetupThreePartTables(mock,
                main: new[]
                {
                    AlteonResponseFactory.VirtServiceEntry("virt1", 1, 443),
                    AlteonResponseFactory.VirtServiceEntry("virt2", 1, 443),
                    AlteonResponseFactory.VirtServiceEntry("virt3", 1, 443)
                },
                second: new[]
                {
                    AlteonResponseFactory.VirtServiceSecondPartEntry("virt1", 1, servCert: "cert-a"),
                    AlteonResponseFactory.VirtServiceSecondPartEntry("virt2", 1, servCert: "cert-b"),
                    AlteonResponseFactory.VirtServiceSecondPartEntry("virt3", 1, servCert: "cert-a")
                },
                fifth: new[]
                {
                    AlteonResponseFactory.VirtServiceFifthPartEntry("virt1", 1, 1),
                    AlteonResponseFactory.VirtServiceFifthPartEntry("virt2", 1, 1),
                    AlteonResponseFactory.VirtServiceFifthPartEntry("virt3", 1, 1)
                });
            mock.When(HttpMethod.Get, GroupUrl)
                .Respond("application/json", AlteonResponseFactory.EmptyCertGroupTableResponse());
            SetupCertContent(mock, "cert-a");
            SetupCertContent(mock, "cert-b");

            var job = BuildInventoryJob(mock);
            var result = job.ProcessJob(BuildInventoryConfig(),
                items => { submitted = items.ToList(); return true; });

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            submitted.Should().HaveCount(2);

            var certA = submitted!.Single(i => i.Alias == "cert-a");
            var aBindings = certA.Parameters["VirtualServiceBindings"].ToString()!.Split(',');
            aBindings.Should().Contain("virt1:443").And.Contain("virt3:443");
            aBindings.Should().NotContain("virt2:443");

            var certB = submitted.Single(i => i.Alias == "cert-b");
            certB.Parameters["VirtualServiceBindings"].ToString()
                 .Should().Contain("virt2:443");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SetupCertTable(MockHttpMessageHandler mock,
                                           IEnumerable<object> entries)
        {
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewSslCfgCertsTable")
                .Respond("application/json", AlteonResponseFactory.CertTableResponse(entries));
        }

        private static void SetupCertContent(MockHttpMessageHandler mock, string certId)
        {
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/getcert*")
                .Respond("text/plain", AlteonResponseFactory.FakePemCertContent(certId));
        }

        private static void SetupThreePartTables(MockHttpMessageHandler mock,
                                                  IEnumerable<object> main,
                                                  IEnumerable<object> second,
                                                  IEnumerable<object> fifth)
        {
            mock.When(HttpMethod.Get, MainUrl)
                .Respond("application/json", AlteonResponseFactory.VirtServiceTableResponse(main));
            mock.When(HttpMethod.Get, SecondUrl)
                .Respond("application/json", AlteonResponseFactory.VirtServiceSecondPartTableResponse(second));
            mock.When(HttpMethod.Get, FifthUrl)
                .Respond("application/json", AlteonResponseFactory.VirtServiceFifthPartTableResponse(fifth));
        }

        private static Inventory BuildInventoryJob(MockHttpMessageHandler mock)
        {
            var resolverMock = new Mock<Keyfactor.Orchestrators.Extensions.Interfaces.IPAMSecretResolver>();
            resolverMock.Setup(r => r.Resolve(It.IsAny<string>())).Returns<string>(s => s);
            return new Inventory(resolverMock.Object, BaseUrl, "admin", "admin",
                NullLogger<Inventory>.Instance, mock.ToHttpClient());
        }

        private static InventoryJobConfiguration BuildInventoryConfig() =>
            new InventoryJobConfiguration
            {
                JobHistoryId = 1,
                CertificateStoreDetails = new CertificateStore
                {
                    ClientMachine = BaseUrl, StorePath = "NA"
                },
                ServerUsername = "admin",
                ServerPassword = "admin"
            };
    }
}
