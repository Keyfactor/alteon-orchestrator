// Copyright 2026 Keyfactor
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Tests
{
    /// <summary>
    /// Unit tests for the virtual-service and binding methods on AlteonLoadBalancerClient.
    ///
    /// The Alteon API splits virtual service configuration across multiple "part" tables:
    ///   - SlbNewCfgEnhVirtServicesTable       — main table (ServIndex, Index, VirtPort)
    ///   - SlbNewCfgEnhVirtServicesSecondPartTable — ServCert, SSLpol
    ///   - SlbNewCfgEnhVirtServicesFifthPartTable  — ServCertGrpMark (1=cert, 2=group)
    ///
    /// GetAllResolvedServicesAsync fetches all three in parallel and joins them.
    /// GetVirtualServiceAsync uses that to find a service by name + port.
    /// BindCertificateAsync writes to the second and fifth part tables.
    /// </summary>
    public class AlteonClientVirtualServiceTests
    {
        private const string BaseUrl  = "https://192.168.1.168";
        private const string Username = "admin";
        private const string Password = "admin";

        private const string MainUrl   = $"{BaseUrl}/config/SlbNewCfgEnhVirtServicesTable";
        private const string SecondUrl = $"{BaseUrl}/config/SlbNewCfgEnhVirtServicesSecondPartTable";
        private const string FifthUrl  = $"{BaseUrl}/config/SlbNewCfgEnhVirtServicesFifthPartTable";
        private const string GroupUrl  = $"{BaseUrl}/config/SlbNewSslCfgGroupsTable";
        private const string PolicyUrl = $"{BaseUrl}/config/SlbNewSslCfgSSLPolTable";

        // ── GetAllResolvedServicesAsync ───────────────────────────────────────

        [Fact]
        public async Task GetAllResolvedServices_JoinsAllThreeTables()
        {
            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Get, MainUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443),
                           AlteonResponseFactory.VirtServiceEntry("web",    1, 80)
                       }));

            handler.When(HttpMethod.Get, SecondUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceSecondPartTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServiceSecondPartEntry(
                               "webssl", 1, servCert: "my-cert", sslPol: "KF-webssl-443")
                       }));

            handler.When(HttpMethod.Get, FifthUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceFifthPartTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, certGrpMark: 1),
                           AlteonResponseFactory.VirtServiceFifthPartEntry("web",    1, certGrpMark: 1)
                       }));

            var client = BuildClient(handler);
            var result = await client.GetAllResolvedServicesAsync();

            result.Should().HaveCount(2);

            var webssl = result[0];
            webssl.ServIndex.Should().Be("webssl");
            webssl.VirtPort.Should().Be(443);
            webssl.ServCert.Should().Be("my-cert");
            webssl.SSLpol.Should().Be("KF-webssl-443");
            webssl.CertGrpMark.Should().Be(1);

            var web = result[1];
            web.ServIndex.Should().Be("web");
            web.ServCert.Should().BeEmpty();  // no entry in second-part table
        }

        [Fact]
        public async Task GetAllResolvedServices_EmptyTables_ReturnsEmptyList()
        {
            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Get, MainUrl)
                   .Respond("application/json", AlteonResponseFactory.EmptyVirtServiceTableResponse());
            handler.When(HttpMethod.Get, SecondUrl)
                   .Respond("application/json", AlteonResponseFactory.EmptyVirtServiceSecondPartTableResponse());
            handler.When(HttpMethod.Get, FifthUrl)
                   .Respond("application/json", AlteonResponseFactory.EmptyVirtServiceFifthPartTableResponse());

            var client = BuildClient(handler);
            var result = await client.GetAllResolvedServicesAsync();

            result.Should().BeEmpty();
        }

        // ── GetVirtualServiceAsync ────────────────────────────────────────────

        [Fact]
        public async Task GetVirtualService_MatchesByServIndexAndPort()
        {
            var handler = new MockHttpMessageHandler();

            // Virtual server validation — webssl exists
            handler.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgEnhVirtServerTable")
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServerTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServerEntry("webssl")
                       }));

            handler.When(HttpMethod.Get, MainUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443),
                           AlteonResponseFactory.VirtServiceEntry("webssl", 2, 80)
                       }));
            handler.When(HttpMethod.Get, SecondUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceSecondPartTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServiceSecondPartEntry(
                               "webssl", 1, servCert: "my-cert")
                       }));
            handler.When(HttpMethod.Get, FifthUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.EmptyVirtServiceFifthPartTableResponse());

            var client = BuildClient(handler);
            var result = await client.GetVirtualServiceAsync("webssl", "443");

            result.Should().NotBeNull();
            result!.ServIndex.Should().Be("webssl");
            result.VirtPort.Should().Be(443);
            result.Index.Should().Be(1);
            result.ServCert.Should().Be("my-cert");
        }

        [Fact]
        public async Task GetVirtualService_PortNotFound_ReturnsNull()
        {
            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgEnhVirtServerTable")
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServerTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServerEntry("webssl")
                       }));

            handler.When(HttpMethod.Get, MainUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServiceEntry("webssl", 1, 80) // only port 80
                       }));
            handler.When(HttpMethod.Get, SecondUrl)
                   .Respond("application/json", AlteonResponseFactory.EmptyVirtServiceSecondPartTableResponse());
            handler.When(HttpMethod.Get, FifthUrl)
                   .Respond("application/json", AlteonResponseFactory.EmptyVirtServiceFifthPartTableResponse());

            var client = BuildClient(handler);
            var result = await client.GetVirtualServiceAsync("webssl", "443"); // 443 not found

            result.Should().BeNull();
        }

        // ── GetBindingsForCertificateAsync ────────────────────────────────────

        [Fact]
        public async Task GetBindingsForCertificate_NonSni_ReturnsMatchingServices()
        {
            var handler = SetupThreePartTables(handler: new MockHttpMessageHandler(),
                mainEntries: new[]
                {
                    AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443),
                    AlteonResponseFactory.VirtServiceEntry("web",    1, 80)
                },
                secondEntries: new[]
                {
                    AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: "target-cert"),
                    AlteonResponseFactory.VirtServiceSecondPartEntry("web",    1, servCert: "other-cert")
                },
                fifthEntries: new[]
                {
                    AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, certGrpMark: 1),
                    AlteonResponseFactory.VirtServiceFifthPartEntry("web",    1, certGrpMark: 1)
                });

            handler.When(HttpMethod.Get, GroupUrl)
                   .Respond("application/json", AlteonResponseFactory.EmptyCertGroupTableResponse());

            var client = BuildClient(handler);
            var bindings = await client.GetBindingsForCertificateAsync("target-cert");

            bindings.Should().HaveCount(1);
            bindings[0].VirtId.Should().Be("webssl");
            bindings[0].ServicePort.Should().Be("443");
        }

        [Fact]
        public async Task GetBindingsForCertificate_SniGroup_ReturnsGroupBindings()
        {
            var handler = SetupThreePartTables(new MockHttpMessageHandler(),
                mainEntries: new[]
                {
                    AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443)
                },
                secondEntries: new[]
                {
                    // ServCert holds the group name when CertGrpMark == 2
                    AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: "KF-GRP-webssl-443")
                },
                fifthEntries: new[]
                {
                    AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, certGrpMark: 2)
                });

            handler.When(HttpMethod.Get, GroupUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.CertGroupTableResponse(new[]
                       {
                           AlteonResponseFactory.CertGroupEntry(
                               "KF-GRP-webssl-443", "target-cert", "target-cert", "other-cert")
                       }));

            var client = BuildClient(handler);
            var bindings = await client.GetBindingsForCertificateAsync("target-cert");

            bindings.Should().HaveCount(1);
            bindings[0].VirtId.Should().Be("webssl");
            bindings[0].ServicePort.Should().Be("443");
        }

        // ── BindCertificateAsync ──────────────────────────────────────────────

        [Fact]
        public async Task BindCertificate_WritesToSecondAndFifthPartTables()
        {
            string? secondBody = null;
            string? fifthBody  = null;

            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Put, $"{SecondUrl}/webssl/1")
                   .With(req => { secondBody = req.Content?.ReadAsStringAsync().Result; return true; })
                   .Respond("application/json", AlteonResponseFactory.OkResponse());

            handler.When(HttpMethod.Put, $"{FifthUrl}/webssl/1")
                   .With(req => { fifthBody = req.Content?.ReadAsStringAsync().Result; return true; })
                   .Respond("application/json", AlteonResponseFactory.OkResponse());

            var client = BuildClient(handler);
            var svc = new ResolvedVirtService
            {
                ServIndex = "webssl", Index = 1, VirtPort = 443,
                ServCert = "", SSLpol = "", CertGrpMark = 1
            };

            await client.BindCertificateAsync(svc, "my-cert", "KF-webssl-443", certGrpMark: 1);

            secondBody.Should().Contain("my-cert");
            secondBody.Should().Contain("KF-webssl-443");
            fifthBody.Should().Contain("1"); // ServCertGrpMark = 1
        }

        [Fact]
        public async Task BindCertificate_SecondPartFailure_ThrowsException()
        {
            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Put, $"{SecondUrl}/webssl/1")
                   .Respond(HttpStatusCode.InternalServerError, "application/json",
                       AlteonResponseFactory.ErrorResponse("internal error"));

            var client = BuildClient(handler);
            var svc = new ResolvedVirtService
            {
                ServIndex = "webssl", Index = 1, VirtPort = 443,
                ServCert = "", SSLpol = "", CertGrpMark = 1
            };

            Func<Task> act = () => client.BindCertificateAsync(svc, "my-cert", "KF-webssl-443");
            await act.Should().ThrowAsync<Exception>();
        }

        // ── EnsureSslPolicyAsync ──────────────────────────────────────────────

        [Fact]
        public async Task EnsureSslPolicy_PolicyDoesNotExist_CreatesIt()
        {
            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Get, $"{PolicyUrl}/KF-webssl-443")
                   .Respond(HttpStatusCode.MethodNotAllowed, "application/json",
                       AlteonResponseFactory.ErrorResponse("not found"));

            handler.When(HttpMethod.Post, $"{PolicyUrl}/KF-webssl-443")
                   .Respond("application/json", AlteonResponseFactory.OkResponse());

            var client = BuildClient(handler);
            await client.EnsureSslPolicyAsync("KF-webssl-443");

            handler.VerifyNoOutstandingExpectation();
        }

        [Fact]
        public async Task EnsureSslPolicy_PolicyExists_DoesNotRecreate()
        {
            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Get, $"{PolicyUrl}/KF-webssl-443")
                   .Respond("application/json",
                       AlteonResponseFactory.SslPolicyTableResponse(new[]
                       {
                           AlteonResponseFactory.SslPolicyEntry("KF-webssl-443")
                       }));

            // No POST should fire — MockHttp would throw on unexpected request
            var client = BuildClient(handler);
            await client.EnsureSslPolicyAsync("KF-webssl-443");
        }

        // ── Cert group management ─────────────────────────────────────────────

        [Fact]
        public async Task AddCertToGroup_NotAlreadyMember_AddsIt()
        {
            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Get, $"{GroupUrl}/my-group")
                   .Respond("application/json",
                       AlteonResponseFactory.CertGroupTableResponse(new[]
                       {
                           AlteonResponseFactory.CertGroupEntry("my-group", "existing-cert", "existing-cert")
                       }));

            handler.When(HttpMethod.Put, $"{GroupUrl}/my-group")
                   .Respond("application/json", AlteonResponseFactory.OkResponse());

            var client = BuildClient(handler);
            await client.AddCertToGroupAsync("my-group", "new-cert");

            handler.VerifyNoOutstandingExpectation();
        }

        [Fact]
        public async Task AddCertToGroup_AlreadyMember_IsIdempotent()
        {
            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Get, $"{GroupUrl}/my-group")
                   .Respond("application/json",
                       AlteonResponseFactory.CertGroupTableResponse(new[]
                       {
                           AlteonResponseFactory.CertGroupEntry(
                               "my-group", "existing-cert", "existing-cert", "new-cert")
                       }));

            // PUT should NOT fire — cert already a member
            var client = BuildClient(handler);
            await client.AddCertToGroupAsync("my-group", "new-cert");
        }

        [Fact]
        public async Task RemoveCertFromGroup_IsDefaultCert_ThrowsInvalidOperation()
        {
            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Get, $"{GroupUrl}/my-group")
                   .Respond("application/json",
                       AlteonResponseFactory.CertGroupTableResponse(new[]
                       {
                           AlteonResponseFactory.CertGroupEntry(
                               "my-group", "target-cert", "target-cert", "other-cert")
                       }));

            var client = BuildClient(handler);

            Func<Task> act = () => client.RemoveCertFromGroupAsync("my-group", "target-cert");
            await act.Should().ThrowAsync<InvalidOperationException>()
                     .WithMessage("*default cert*");
        }

        [Fact]
        public async Task RemoveCertFromGroup_NonDefaultMember_RemovesIt()
        {
            var handler = new MockHttpMessageHandler();

            handler.When(HttpMethod.Get, $"{GroupUrl}/my-group")
                   .Respond("application/json",
                       AlteonResponseFactory.CertGroupTableResponse(new[]
                       {
                           AlteonResponseFactory.CertGroupEntry(
                               "my-group", "default-cert", "default-cert", "target-cert")
                       }));

            handler.When(HttpMethod.Put, $"{GroupUrl}/my-group")
                   .Respond("application/json", AlteonResponseFactory.OkResponse());

            var client = BuildClient(handler);
            await client.RemoveCertFromGroupAsync("my-group", "target-cert");

            handler.VerifyNoOutstandingExpectation();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static MockHttpMessageHandler SetupThreePartTables(
            MockHttpMessageHandler handler,
            IEnumerable<object> mainEntries,
            IEnumerable<object> secondEntries,
            IEnumerable<object> fifthEntries)
        {
            handler.When(HttpMethod.Get, MainUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceTableResponse(mainEntries));
            handler.When(HttpMethod.Get, SecondUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceSecondPartTableResponse(secondEntries));
            handler.When(HttpMethod.Get, FifthUrl)
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceFifthPartTableResponse(fifthEntries));
            return handler;
        }

        private static AlteonLoadBalancerClient BuildClient(MockHttpMessageHandler handler) =>
            new AlteonLoadBalancerClient(
                BaseUrl, Username, Password,
                NullLogger.Instance,
                handler.ToHttpClient());
    }
}
