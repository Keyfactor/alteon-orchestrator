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
    public class ManagementBindingTests
    {
        private const string BaseUrl    = "https://192.168.1.168";
        private const string CertAlias  = "my-cert";
        private const string MainUrl    = $"{BaseUrl}/config/SlbNewCfgEnhVirtServicesTable";
        private const string SecondUrl  = $"{BaseUrl}/config/SlbNewCfgEnhVirtServicesSecondPartTable";
        private const string FifthUrl   = $"{BaseUrl}/config/SlbNewCfgEnhVirtServicesFifthPartTable";
        private const string ServerUrl  = $"{BaseUrl}/config/SlbNewCfgEnhVirtServerTable";
        private const string GroupUrl   = $"{BaseUrl}/config/SlbNewSslCfgGroupsTable";
        private const string PolicyUrl  = $"{BaseUrl}/config/SlbNewSslCfgSSLPolTable";

        // ── Add: non-SNI, unbound service, no existing policy ─────────────────

        [Fact]
        public async Task Add_NonSni_UnboundService_CreatesBindingAndPolicy()
        {
            var mock = new MockHttpMessageHandler();
            SetupVirtServerValidation(mock, "webssl");
            SetupThreePartTables(mock,
                main:   new[] { AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443) },
                second: new[] { AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1) },
                fifth:  new[] { AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, 1) });

            // Policy doesn't exist
            mock.When(HttpMethod.Get, $"{PolicyUrl}/KF-webssl-443")
                .Respond(HttpStatusCode.MethodNotAllowed, "application/json",
                    AlteonResponseFactory.ErrorResponse("not found"));
            mock.When(HttpMethod.Post, $"{PolicyUrl}/KF-webssl-443")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            // Binding — second and fifth part
            mock.When(HttpMethod.Put, $"{SecondUrl}/webssl/1")
                .Respond("application/json", AlteonResponseFactory.OkResponse());
            mock.When(HttpMethod.Put, $"{FifthUrl}/webssl/1")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            // Apply + Save
            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "webssl:443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            mock.VerifyNoOutstandingExpectation();
        }

        // ── Add: conflict — Overwrite=false refuses ───────────────────────────

        [Fact]
        public async Task Add_NonSni_DifferentCertBound_OverwriteFalse_Fails()
        {
            var mock = new MockHttpMessageHandler();
            SetupVirtServerValidation(mock, "webssl");
            SetupThreePartTables(mock,
                main:   new[] { AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443) },
                second: new[] { AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: "different-cert") },
                fifth:  new[] { AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, 1) });

            var result = await RunAddJob(mock, "webssl:443", overwrite: false);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain("different-cert");
            result.FailureMessage.Should().Contain("Overwrite");
        }

        // ── Add: conflict — Overwrite=true replaces ───────────────────────────

        [Fact]
        public async Task Add_NonSni_DifferentCertBound_OverwriteTrue_Succeeds()
        {
            var mock = new MockHttpMessageHandler();
            SetupVirtServerValidation(mock, "webssl");
            SetupThreePartTables(mock,
                main:   new[] { AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443) },
                second: new[] { AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: "different-cert", sslPol: "KF-webssl-443") },
                fifth:  new[] { AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, 1) });

            mock.When(HttpMethod.Get, $"{PolicyUrl}/KF-webssl-443")
                .Respond("application/json",
                    AlteonResponseFactory.SslPolicyTableResponse(new[]
                    {
                        AlteonResponseFactory.SslPolicyEntry("KF-webssl-443")
                    }));
            mock.When(HttpMethod.Put, $"{SecondUrl}/webssl/1")
                .Respond("application/json", AlteonResponseFactory.OkResponse());
            mock.When(HttpMethod.Put, $"{FifthUrl}/webssl/1")
                .Respond("application/json", AlteonResponseFactory.OkResponse());
            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "webssl:443", overwrite: true);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            mock.VerifyNoOutstandingExpectation();
        }

        // ── Add: same cert already bound — idempotent ─────────────────────────

        [Fact]
        public async Task Add_NonSni_SameCertAlreadyBound_Succeeds()
        {
            var mock = new MockHttpMessageHandler();
            SetupVirtServerValidation(mock, "webssl");
            SetupThreePartTables(mock,
                main:   new[] { AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443) },
                second: new[] { AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: CertAlias, sslPol: "KF-webssl-443") },
                fifth:  new[] { AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, 1) });

            mock.When(HttpMethod.Get, $"{PolicyUrl}/KF-webssl-443")
                .Respond("application/json",
                    AlteonResponseFactory.SslPolicyTableResponse(new[]
                    {
                        AlteonResponseFactory.SslPolicyEntry("KF-webssl-443")
                    }));
            mock.When(HttpMethod.Put, $"{SecondUrl}/webssl/1")
                .Respond("application/json", AlteonResponseFactory.OkResponse());
            mock.When(HttpMethod.Put, $"{FifthUrl}/webssl/1")
                .Respond("application/json", AlteonResponseFactory.OkResponse());
            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "webssl:443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
        }

        // ── Add: virtual service not found ────────────────────────────────────

        [Fact]
        public async Task Add_VirtualServiceNotFound_FailsWithExplanation()
        {
            var mock = new MockHttpMessageHandler();
            SetupVirtServerValidation(mock, "webssl");
            SetupThreePartTables(mock,
                main:   new List<object>(),  // empty — service doesn't exist
                second: new List<object>(),
                fifth:  new List<object>());

            var result = await RunAddJob(mock, "webssl:443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain("webssl:443");
        }

        // ── Add: no VirtualServiceBindings — succeeds silently ────────────────

        [Fact]
        public async Task Add_NoBindingsParam_SucceedsSilently()
        {
            var mock = new MockHttpMessageHandler();
            var result = await RunAddJob(mock, bindingsParam: null);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
        }

        // ── Add: SNI — existing group, add cert to it ─────────────────────────

        [Fact]
        public async Task Add_Sni_ExistingGroup_AddsCertToGroup()
        {
            var mock = new MockHttpMessageHandler();
            SetupVirtServerValidation(mock, "webssl");
            SetupThreePartTables(mock,
                main:   new[] { AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443) },
                second: new[] { AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: "existing-group") },
                fifth:  new[] { AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, certGrpMark: 2) });

            mock.When(HttpMethod.Get, $"{GroupUrl}/existing-group")
                .Respond("application/json",
                    AlteonResponseFactory.CertGroupTableResponse(new[]
                    {
                        AlteonResponseFactory.CertGroupEntry("existing-group", "other-cert", "other-cert")
                    }));
            mock.When(HttpMethod.Put, $"{GroupUrl}/existing-group")
                .Respond("application/json", AlteonResponseFactory.OkResponse());
            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "webssl:443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            mock.VerifyNoOutstandingExpectation();
        }

        // ── Remove: non-SNI bound cert — block removal ──────────────────────

        [Fact]
        public async Task Remove_NonSni_BoundCert_FailsWithExplanation()
        {
            var mock = new MockHttpMessageHandler();

            SetupThreePartTables(mock,
                main:   new[] { AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443) },
                second: new[] { AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: CertAlias) },
                fifth:  new[] { AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, 1) });

            mock.When(HttpMethod.Get, GroupUrl)
                .Respond("application/json", AlteonResponseFactory.EmptyCertGroupTableResponse());

            var result = await RunRemoveJob(mock);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain(CertAlias);
            result.FailureMessage.Should().Contain("webssl:443");
            result.FailureMessage.Should().Contain("Overwrite");
        }

        // ── Remove: cert bound to multiple services — lists all in error ───────

        [Fact]
        public async Task Remove_NonSni_BoundToMultipleServices_ListsAllInError()
        {
            var mock = new MockHttpMessageHandler();

            SetupThreePartTables(mock,
                main: new[]
                {
                    AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443),
                    AlteonResponseFactory.VirtServiceEntry("virt2",  1, 443)
                },
                second: new[]
                {
                    AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: CertAlias),
                    AlteonResponseFactory.VirtServiceSecondPartEntry("virt2",  1, servCert: CertAlias)
                },
                fifth: new[]
                {
                    AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, 1),
                    AlteonResponseFactory.VirtServiceFifthPartEntry("virt2",  1, 1)
                });

            mock.When(HttpMethod.Get, GroupUrl)
                .Respond("application/json", AlteonResponseFactory.EmptyCertGroupTableResponse());

            var result = await RunRemoveJob(mock);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain("webssl:443");
            result.FailureMessage.Should().Contain("virt2:443");
        }

        // ── Remove: SNI default cert — refuse ─────────────────────────────────

        [Fact]
        public async Task Remove_SniDefaultCert_FailsWithExplanation()
        {
            var mock = new MockHttpMessageHandler();

            SetupThreePartTables(mock,
                main:   new[] { AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443) },
                second: new[] { AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: "my-group") },
                fifth:  new[] { AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, certGrpMark: 2) });

            mock.When(HttpMethod.Get, GroupUrl)
                .Respond("application/json",
                    AlteonResponseFactory.CertGroupTableResponse(new[]
                    {
                        AlteonResponseFactory.CertGroupEntry("my-group", CertAlias, CertAlias, "other-cert")
                    }));

            var result = await RunRemoveJob(mock);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain("default cert");
            result.FailureMessage.Should().Contain("my-group");
        }

        // ── Remove: SNI non-default — removes from group ──────────────────────

        [Fact]
        public async Task Remove_SniNonDefaultCert_RemovesFromGroup()
        {
            var mock = new MockHttpMessageHandler();

            SetupThreePartTables(mock,
                main:   new[] { AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443) },
                second: new[] { AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: "my-group") },
                fifth:  new[] { AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, certGrpMark: 2) });

            mock.When(HttpMethod.Get, GroupUrl)
                .Respond("application/json",
                    AlteonResponseFactory.CertGroupTableResponse(new[]
                    {
                        AlteonResponseFactory.CertGroupEntry("my-group", "default-cert", "default-cert", CertAlias)
                    }));
            mock.When(HttpMethod.Put, $"{GroupUrl}/my-group")
                .Respond("application/json", AlteonResponseFactory.OkResponse());
            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunRemoveJob(mock);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            mock.VerifyNoOutstandingExpectation();
        }

        // ── Remove: cert not bound to any service — succeeds ─────────────────

        [Fact]
        public async Task Remove_CertNotBoundToAnyService_Succeeds()
        {
            var mock = new MockHttpMessageHandler();

            SetupThreePartTables(mock,
                main:   new[] { AlteonResponseFactory.VirtServiceEntry("webssl", 1, 443) },
                second: new[] { AlteonResponseFactory.VirtServiceSecondPartEntry("webssl", 1, servCert: "different-cert") },
                fifth:  new[] { AlteonResponseFactory.VirtServiceFifthPartEntry("webssl", 1, 1) });

            mock.When(HttpMethod.Get, GroupUrl)
                .Respond("application/json", AlteonResponseFactory.EmptyCertGroupTableResponse());

            var result = await RunRemoveJob(mock);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SetupVirtServerValidation(MockHttpMessageHandler mock,
                                                       string virtServerName)
        {
            mock.When(HttpMethod.Get, ServerUrl)
                .Respond("application/json",
                    AlteonResponseFactory.VirtServerTableResponse(new[]
                    {
                        AlteonResponseFactory.VirtServerEntry(virtServerName)
                    }));
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

        private static async Task<JobResult> RunAddJob(MockHttpMessageHandler mock,
                                                        string? bindingsParam,
                                                        string alias = CertAlias,
                                                        bool overwrite = false)
        {
            var job = BuildManagementJob(mock, overwrite);
            return await job.PerformBindingsForAddAsync(alias, bindingsParam, 1, overwrite);
        }

        private static async Task<JobResult> RunRemoveJob(MockHttpMessageHandler mock,
                                                           string alias = CertAlias)
        {
            var job = BuildManagementJob(mock);
            return await job.PerformBindingsForRemoveAsync(alias, 1);
        }

        private static Management BuildManagementJob(MockHttpMessageHandler mock,
                                                      bool overwrite = false)
        {
            var resolverMock = new Mock<Keyfactor.Orchestrators.Extensions.Interfaces.IPAMSecretResolver>();
            resolverMock.Setup(r => r.Resolve(It.IsAny<string>())).Returns<string>(s => s);

            var job = new Management(resolverMock.Object, BaseUrl, "admin", "admin",
                NullLogger<Management>.Instance, mock.ToHttpClient());
            job.Overwrite = overwrite;
            return job;
        }
    }
}
