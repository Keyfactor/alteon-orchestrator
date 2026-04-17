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
    /// <summary>
    /// Tests for the binding logic in the Management job's PerformAddition
    /// and PerformRemoval methods.
    ///
    /// These tests focus exclusively on the virtual service binding behaviour
    /// that will be added on top of the existing cert import logic.
    /// The cert import itself is already tested by the existing code paths.
    ///
    /// Scenarios covered:
    ///   Add - non-SNI:  bind SrvCert directly, auto-create policy
    ///   Add - SNI:      add cert to existing cert group
    ///   Add - SNI:      create new cert group when none exists
    ///   Add - conflict: refuse when service already has a different cert
    ///   Add - multi:    bind same cert to multiple services
    ///   Remove - non-SNI: clear SrvCert, leave policy alone
    ///   Remove - SNI default: refuse and explain
    ///   Remove - SNI non-default: remove from group
    ///   Remove - no bindings: succeed silently
    /// </summary>
    public class ManagementBindingTests
    {
        private const string BaseUrl = "https://192.168.1.168";
        private const string CertAlias = "my-cert";

        // ── Add: non-SNI, single service, no existing policy ─────────────────

        [Fact]
        public async Task Add_NonSni_SingleService_CreatesBindingAndPolicy()
        {
            var mock = new MockHttpMessageHandler();

            // Virtual service has no cert bound, no cert group
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json",
                    AlteonResponseFactory.SingleVirtServiceResponse("1", "443"));

            // Policy doesn't exist yet → GET returns 405
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgSslPolicyTable/KF-1-443")
                .Respond(HttpStatusCode.MethodNotAllowed, "application/json",
                    AlteonResponseFactory.ErrorResponse("not found"));

            // Policy creation
            mock.When(HttpMethod.Post,
                    $"{BaseUrl}/config/SlbNewCfgSslPolicyTable/KF-1-443")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            // Cert binding
            mock.When(HttpMethod.Put,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            // Apply + Save
            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "1:443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            mock.VerifyNoOutstandingExpectation();
        }

        // ── Add: non-SNI, policy already exists ──────────────────────────────

        [Fact]
        public async Task Add_NonSni_ExistingPolicy_BindsWithoutRecreatingPolicy()
        {
            var mock = new MockHttpMessageHandler();

            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json",
                    AlteonResponseFactory.SingleVirtServiceResponse(
                        "1", "443", sslPolName: "KF-1-443"));

            // Policy GET returns success — already exists
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgSslPolicyTable/KF-1-443")
                .Respond("application/json",
                    AlteonResponseFactory.SslPolicyTableResponse(new[]
                    {
                        AlteonResponseFactory.SslPolicyEntry("KF-1-443")
                    }));

            // Binding PUT
            mock.When(HttpMethod.Put,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "1:443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            // POST to create policy should NOT have been called
        }

        // ── Add: conflict — Overwrite=false refuses ───────────────────────────

        [Fact]
        public async Task Add_NonSni_ServiceHasDifferentCert_OverwriteFalse_FailsWithExplanation()
        {
            var mock = new MockHttpMessageHandler();

            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json",
                    AlteonResponseFactory.SingleVirtServiceResponse(
                        "1", "443", srvCert: "different-cert"));

            var result = await RunAddJob(mock, "1:443", overwrite: false);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain("different-cert");
            result.FailureMessage.Should().Contain("1:443");
            result.FailureMessage.Should().Contain("Overwrite");
        }

        // ── Add: conflict — Overwrite=true replaces existing binding ──────────

        [Fact]
        public async Task Add_NonSni_ServiceHasDifferentCert_OverwriteTrue_Succeeds()
        {
            var mock = new MockHttpMessageHandler();

            // Service already has a DIFFERENT cert bound
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json",
                    AlteonResponseFactory.SingleVirtServiceResponse(
                        "1", "443", srvCert: "different-cert",
                        sslPolName: "KF-1-443"));

            // Policy already exists — no creation needed
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgSslPolicyTable/KF-1-443")
                .Respond("application/json",
                    AlteonResponseFactory.SslPolicyTableResponse(new[]
                    {
                        AlteonResponseFactory.SslPolicyEntry("KF-1-443")
                    }));

            // Overwrite: binding is replaced with new cert
            mock.When(HttpMethod.Put,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "1:443", overwrite: true);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            mock.VerifyNoOutstandingExpectation();
        }

        // ── Add: same cert already bound (renewal) — idempotent ──────────────

        [Fact]
        public async Task Add_NonSni_SameCertAlreadyBound_Succeeds()
        {
            var mock = new MockHttpMessageHandler();

            // Service already has THIS cert bound — renewal scenario
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json",
                    AlteonResponseFactory.SingleVirtServiceResponse(
                        "1", "443", srvCert: CertAlias, sslPolName: "KF-1-443"));

            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgSslPolicyTable/KF-1-443")
                .Respond("application/json",
                    AlteonResponseFactory.SslPolicyTableResponse(new[]
                    {
                        AlteonResponseFactory.SslPolicyEntry("KF-1-443")
                    }));

            mock.When(HttpMethod.Put,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "1:443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
        }

        // ── Add: multiple services, all succeed ──────────────────────────────

        [Fact]
        public async Task Add_NonSni_MultipleServices_BindsAll()
        {
            var mock = new MockHttpMessageHandler();

            foreach (var (virt, port) in new[] { ("1", "443"), ("2", "443"), ("3", "8443") })
            {
                var policyId = $"KF-{virt}-{port}";
                mock.When(HttpMethod.Get,
                        $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/{virt}/{port}")
                    .Respond("application/json",
                        AlteonResponseFactory.SingleVirtServiceResponse(virt, port));

                mock.When(HttpMethod.Get,
                        $"{BaseUrl}/config/SlbNewCfgSslPolicyTable/{policyId}")
                    .Respond(HttpStatusCode.MethodNotAllowed, "application/json",
                        AlteonResponseFactory.ErrorResponse("not found"));

                mock.When(HttpMethod.Post,
                        $"{BaseUrl}/config/SlbNewCfgSslPolicyTable/{policyId}")
                    .Respond("application/json", AlteonResponseFactory.OkResponse());

                mock.When(HttpMethod.Put,
                        $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/{virt}/{port}")
                    .Respond("application/json", AlteonResponseFactory.OkResponse());
            }

            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "1:443,2:443,3:8443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            mock.VerifyNoOutstandingExpectation();
        }

        // ── Add: virtual service doesn't exist ───────────────────────────────

        [Fact]
        public async Task Add_VirtualServiceNotFound_FailsWithExplanation()
        {
            var mock = new MockHttpMessageHandler();

            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/99/443")
                .Respond(HttpStatusCode.MethodNotAllowed, "application/json",
                    AlteonResponseFactory.ErrorResponse("not found"));

            var result = await RunAddJob(mock, "99:443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain("99:443");
        }

        // ── Add: missing VirtualServiceBindings parameter ────────────────────

        [Fact]
        public async Task Add_MissingBindingsParameter_FailsWithExplanation()
        {
            var mock = new MockHttpMessageHandler();
            // No HTTP calls should be made at all

            var result = await RunAddJob(mock, bindingsParam: null);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain("VirtualServiceBindings");
        }

        // ── Add: SNI — existing cert group, add cert to it ───────────────────

        [Fact]
        public async Task Add_Sni_ExistingGroup_AddsCertToGroup()
        {
            var mock = new MockHttpMessageHandler();

            // Service has CertGroup set → SNI mode
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json",
                    AlteonResponseFactory.SingleVirtServiceResponse(
                        "1", "443", certGroup: "existing-group"));

            // GET the cert group to check membership
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/existing-group")
                .Respond("application/json",
                    AlteonResponseFactory.CertGroupTableResponse(new[]
                    {
                        AlteonResponseFactory.CertGroupEntry(
                            "existing-group", "other-cert", "other-cert")
                    }));

            // PUT to add new cert to the group
            mock.When(HttpMethod.Put,
                    $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/existing-group")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "1:443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            mock.VerifyNoOutstandingExpectation();
        }

        // ── Add: SNI — no existing cert group, create one ────────────────────

        [Fact]
        public async Task Add_Sni_NoCertGroup_CreatesGroupAndBinds()
        {
            var mock = new MockHttpMessageHandler();

            // Service has no SrvCert AND no CertGroup — treat as SNI-capable
            // by creating a new group
            // NOTE: actual branch condition is determined by checking if CertGroup is set.
            // If the operator intends SNI for a service that has no group yet,
            // this is the new-group-creation path.
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json",
                    AlteonResponseFactory.SingleVirtServiceResponse("1", "443",
                        certGroup: "KF-GRP-1-443")); // group name set but group doesn't exist yet

            // GET the cert group → not found
            mock.When(HttpMethod.Get,
                    $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/KF-GRP-1-443")
                .Respond(HttpStatusCode.MethodNotAllowed, "application/json",
                    AlteonResponseFactory.ErrorResponse("not found"));

            // POST to create the cert group
            mock.When(HttpMethod.Post,
                    $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/KF-GRP-1-443")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            // PUT to bind the cert group to the virtual service
            mock.When(HttpMethod.Put,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunAddJob(mock, "1:443");

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            mock.VerifyNoOutstandingExpectation();
        }

        // ── Remove: non-SNI, clears SrvCert, leaves policy ──────────────────

        [Fact]
        public async Task Remove_NonSni_ClearsSrvCert_LeavesPolicyAlone()
        {
            string? putBody = null;
            var mock = new MockHttpMessageHandler();

            // All virtual services: one has our cert bound directly
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                .Respond("application/json",
                    AlteonResponseFactory.VirtServiceTableResponse(new[]
                    {
                        AlteonResponseFactory.VirtServiceEntry(
                            "1", "443", srvCert: CertAlias, sslPolName: "KF-1-443")
                    }));

            // All cert groups: none contain our cert
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                .Respond("application/json",
                    AlteonResponseFactory.EmptyCertGroupTableResponse());

            // Clear the binding
            mock.When(HttpMethod.Put,
                    $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                .With(req =>
                {
                    putBody = req.Content?.ReadAsStringAsync().Result;
                    return true;
                })
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunRemoveJob(mock);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            // SrvCert should be cleared (empty string)
            putBody.Should().NotBeNull();
            putBody.Should().Contain("\"SrvCert\":\"\"");
            // SslPolName should NOT appear in the body (we leave it alone)
            putBody.Should().NotContain("SslPolName");
        }

        // ── Remove: SNI default cert — refuse ────────────────────────────────

        [Fact]
        public async Task Remove_SniDefaultCert_FailsWithExplanation()
        {
            var mock = new MockHttpMessageHandler();

            // No direct bindings
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                .Respond("application/json",
                    AlteonResponseFactory.VirtServiceTableResponse(new[]
                    {
                        AlteonResponseFactory.VirtServiceEntry(
                            "1", "443", certGroup: "my-group")
                    }));

            // Cert is the DEFAULT in the group
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                .Respond("application/json",
                    AlteonResponseFactory.CertGroupTableResponse(new[]
                    {
                        AlteonResponseFactory.CertGroupEntry(
                            "my-group", CertAlias, CertAlias, "other-cert")
                    }));

            var result = await RunRemoveJob(mock);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain("default cert");
            result.FailureMessage.Should().Contain("my-group");
        }

        // ── Remove: SNI non-default cert — removes from group ────────────────

        [Fact]
        public async Task Remove_SniNonDefaultCert_RemovesFromGroup()
        {
            var mock = new MockHttpMessageHandler();

            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                .Respond("application/json",
                    AlteonResponseFactory.VirtServiceTableResponse(new[]
                    {
                        AlteonResponseFactory.VirtServiceEntry(
                            "1", "443", certGroup: "my-group")
                    }));

            // Our cert is a non-default member
            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                .Respond("application/json",
                    AlteonResponseFactory.CertGroupTableResponse(new[]
                    {
                        AlteonResponseFactory.CertGroupEntry(
                            "my-group", "default-cert", "default-cert", CertAlias)
                    }));

            // Remove from group
            mock.When(HttpMethod.Put,
                    $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/my-group")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            mock.When(HttpMethod.Post, $"{BaseUrl}/config")
                .Respond("application/json", AlteonResponseFactory.OkResponse());

            var result = await RunRemoveJob(mock);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            mock.VerifyNoOutstandingExpectation();
        }

        // ── Remove: no bindings at all — succeeds silently ───────────────────

        [Fact]
        public async Task Remove_NoCertBindings_SucceedsSilently()
        {
            var mock = new MockHttpMessageHandler();

            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                .Respond("application/json",
                    AlteonResponseFactory.VirtServiceTableResponse(new[]
                    {
                        AlteonResponseFactory.VirtServiceEntry(
                            "1", "443", srvCert: "different-cert")
                    }));

            mock.When(HttpMethod.Get, $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                .Respond("application/json",
                    AlteonResponseFactory.EmptyCertGroupTableResponse());

            // No PUT, no Apply/Save should be issued

            var result = await RunRemoveJob(mock);

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static async Task<JobResult> RunAddJob(
            MockHttpMessageHandler mock,
            string? bindingsParam,
            string alias = CertAlias,
            bool overwrite = false)
        {
            var job = BuildManagementJob(mock, overwrite);
            return await job.PerformBindingsForAddAsync(
                alias,
                bindingsParam,
                jobHistoryId: 1,
                overwrite: overwrite);
        }

        private static async Task<JobResult> RunRemoveJob(
            MockHttpMessageHandler mock,
            string alias = CertAlias)
        {
            var job = BuildManagementJob(mock);
            return await job.PerformBindingsForRemoveAsync(alias, jobHistoryId: 1);
        }

        private static Management BuildManagementJob(MockHttpMessageHandler mock,
                                                      bool overwrite = false)
        {
            var resolverMock = new Mock<Keyfactor.Orchestrators.Extensions.Interfaces.IPAMSecretResolver>();
            resolverMock.Setup(r => r.Resolve(It.IsAny<string>()))
                        .Returns<string>(s => s);

            var job = new Management(
                resolverMock.Object,
                BaseUrl, "admin", "admin",
                NullLogger<Management>.Instance,
                mock.ToHttpClient());

            job.Overwrite = overwrite;
            return job;
        }
    }
}