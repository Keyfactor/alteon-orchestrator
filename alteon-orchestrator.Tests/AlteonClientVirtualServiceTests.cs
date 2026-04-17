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
    /// Unit tests for the new virtual-service and binding methods that will be
    /// added to AlteonLoadBalancerClient.
    ///
    /// These tests mock the HTTP layer so no live Alteon device is needed.
    /// The MockHttp library intercepts RestSharp's underlying HttpClient calls.
    ///
    /// NOTE: These tests describe the expected behaviour of methods that do not
    /// yet exist in the client. They are intentionally written first (TDD).
    /// Add the corresponding methods to AlteonLoadBalancerClient to make them pass.
    /// </summary>
    public class AlteonClientVirtualServiceTests
    {
        private const string BaseUrl  = "https://192.168.1.168";
        private const string Username = "admin";
        private const string Password = "admin";

        // ── GetAllVirtualServices ────────────────────────────────────────────

        [Fact]
        public async Task GetAllVirtualServices_ReturnsAllEntries()
        {
            var handler = new MockHttpMessageHandler();
            handler.When($"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServiceEntry("1", "443", srvCert: "my-cert"),
                           AlteonResponseFactory.VirtServiceEntry("2", "443", srvCert: "my-cert"),
                           AlteonResponseFactory.VirtServiceEntry("3", "8443")   // unbound
                       }));

            var client = BuildClient(handler);
            var result = await client.GetAllVirtualServicesAsync();

            result.Should().HaveCount(3);
            result[0].VirtIndex.Should().Be("1");
            result[0].SrvCert.Should().Be("my-cert");
            result[2].SrvCert.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task GetAllVirtualServices_EmptyTable_ReturnsEmptyList()
        {
            var handler = new MockHttpMessageHandler();
            handler.When($"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceTableResponse(new List<object>()));

            var client = BuildClient(handler);
            var result = await client.GetAllVirtualServicesAsync();

            result.Should().BeEmpty();
        }

        // ── GetVirtualService (single) ───────────────────────────────────────

        [Fact]
        public async Task GetVirtualService_NonSniBinding_ReturnsSrvCert()
        {
            var handler = new MockHttpMessageHandler();
            handler.When($"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                   .Respond("application/json",
                       AlteonResponseFactory.SingleVirtServiceResponse(
                           "1", "443", srvCert: "my-cert", sslPolName: "MY-POL"));

            var client = BuildClient(handler);
            var result = await client.GetVirtualServiceAsync("1", "443");

            result.Should().NotBeNull();
            result!.SrvCert.Should().Be("my-cert");
            result.CertGroup.Should().BeNullOrEmpty();
            result.SslPolName.Should().Be("MY-POL");
        }

        [Fact]
        public async Task GetVirtualService_SniBinding_ReturnsCertGroup()
        {
            var handler = new MockHttpMessageHandler();
            handler.When($"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                   .Respond("application/json",
                       AlteonResponseFactory.SingleVirtServiceResponse(
                           "1", "443", certGroup: "KF-GRP-1-443"));

            var client = BuildClient(handler);
            var result = await client.GetVirtualServiceAsync("1", "443");

            result!.CertGroup.Should().Be("KF-GRP-1-443");
            result.SrvCert.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task GetVirtualService_NotFound_ReturnsNull()
        {
            var handler = new MockHttpMessageHandler();
            handler.When($"{BaseUrl}/config/SlbNewCfgVirtServicesTable/99/443")
                   .Respond(HttpStatusCode.MethodNotAllowed,
                       "application/json",
                       AlteonResponseFactory.ErrorResponse("405 Method Not Allowed"));

            var client = BuildClient(handler);
            var result = await client.GetVirtualServiceAsync("99", "443");

            result.Should().BeNull();
        }

        // ── GetBindingsForCertificate ────────────────────────────────────────

        [Fact]
        public async Task GetBindingsForCertificate_NonSni_ReturnsMatchingServices()
        {
            var handler = new MockHttpMessageHandler();
            handler.When($"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServiceEntry("1", "443", srvCert: "target-cert"),
                           AlteonResponseFactory.VirtServiceEntry("2", "443", srvCert: "target-cert"),
                           AlteonResponseFactory.VirtServiceEntry("3", "443", srvCert: "other-cert"),
                           AlteonResponseFactory.VirtServiceEntry("4", "443")  // unbound
                       }));
            handler.When($"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                   .Respond("application/json",
                       AlteonResponseFactory.EmptyCertGroupTableResponse());

            var client = BuildClient(handler);
            var bindings = await client.GetBindingsForCertificateAsync("target-cert");

            bindings.Should().HaveCount(2);
            bindings.Should().Contain(b => b.VirtId == "1" && b.ServicePort == "443");
            bindings.Should().Contain(b => b.VirtId == "2" && b.ServicePort == "443");
        }

        [Fact]
        public async Task GetBindingsForCertificate_SniGroup_ReturnsGroupBindings()
        {
            var handler = new MockHttpMessageHandler();
            // No direct SrvCert bindings
            handler.When($"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServiceEntry("1", "443",
                               certGroup: "KF-GRP-1-443")
                       }));
            // Cert group contains the target cert
            handler.When($"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                   .Respond("application/json",
                       AlteonResponseFactory.CertGroupTableResponse(new[]
                       {
                           AlteonResponseFactory.CertGroupEntry(
                               "KF-GRP-1-443", "target-cert", "target-cert", "other-cert")
                       }));

            var client = BuildClient(handler);
            var bindings = await client.GetBindingsForCertificateAsync("target-cert");

            bindings.Should().HaveCount(1);
            bindings[0].VirtId.Should().Be("1");
            bindings[0].ServicePort.Should().Be("443");
        }

        [Fact]
        public async Task GetBindingsForCertificate_NoCertIdMatch_ReturnsEmpty()
        {
            var handler = new MockHttpMessageHandler();
            handler.When($"{BaseUrl}/config/SlbNewCfgVirtServicesTable")
                   .Respond("application/json",
                       AlteonResponseFactory.VirtServiceTableResponse(new[]
                       {
                           AlteonResponseFactory.VirtServiceEntry("1", "443",
                               srvCert: "completely-different-cert")
                       }));
            handler.When($"{BaseUrl}/config/SlbNewCfgSslCertGroupTable")
                   .Respond("application/json",
                       AlteonResponseFactory.EmptyCertGroupTableResponse());

            var client = BuildClient(handler);
            var bindings = await client.GetBindingsForCertificateAsync("target-cert");

            bindings.Should().BeEmpty();
        }

        // ── BindCertificate (non-SNI) ────────────────────────────────────────

        [Fact]
        public async Task BindCertificate_NonSni_PutsCorrectPayload()
        {
            string? capturedBody = null;
            var handler = new MockHttpMessageHandler();
            handler.When(HttpMethod.Put,
                         $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                   .With(req =>
                   {
                       capturedBody = req.Content?.ReadAsStringAsync().Result;
                       return true;
                   })
                   .Respond("application/json", AlteonResponseFactory.OkResponse());

            var client = BuildClient(handler);
            var binding = new VirtualServiceBinding("1", "443");
            await client.BindCertificateDirectAsync(binding, "my-cert", "KF-1-443");

            capturedBody.Should().NotBeNull();
            capturedBody.Should().Contain("my-cert");
            capturedBody.Should().Contain("KF-1-443");
        }

        [Fact]
        public async Task BindCertificate_NonSni_ApiFailure_ThrowsException()
        {
            var handler = new MockHttpMessageHandler();
            handler.When(HttpMethod.Put,
                         $"{BaseUrl}/config/SlbNewCfgVirtServicesTable/1/443")
                   .Respond(HttpStatusCode.InternalServerError,
                       "application/json",
                       AlteonResponseFactory.ErrorResponse("internal error"));

            var client = BuildClient(handler);
            var binding = new VirtualServiceBinding("1", "443");

            Func<Task> act = () => client.BindCertificateDirectAsync(binding, "my-cert", "KF-1-443");
            await act.Should().ThrowAsync<Exception>();
        }

        // ── SSL Policy management ────────────────────────────────────────────

        [Fact]
        public async Task EnsureSslPolicy_PolicyDoesNotExist_CreatesIt()
        {
            var handler = new MockHttpMessageHandler();
            // First call: GET to check existence → 405 (not found on this firmware)
            handler.When(HttpMethod.Get,
                         $"{BaseUrl}/config/SlbNewCfgSslPolicyTable/KF-1-443")
                   .Respond(HttpStatusCode.MethodNotAllowed, "application/json",
                       AlteonResponseFactory.ErrorResponse("not found"));
            // Second call: POST to create
            handler.When(HttpMethod.Post,
                         $"{BaseUrl}/config/SlbNewCfgSslPolicyTable/KF-1-443")
                   .Respond("application/json", AlteonResponseFactory.OkResponse());

            var client = BuildClient(handler);
            await client.EnsureSslPolicyAsync("KF-1-443");

            // Assert: both requests were made (existence check + creation)
            handler.VerifyNoOutstandingExpectation();
        }

        [Fact]
        public async Task EnsureSslPolicy_PolicyExists_DoesNotRecreate()
        {
            var handler = new MockHttpMessageHandler();
            // GET returns success — policy already exists
            handler.When(HttpMethod.Get,
                         $"{BaseUrl}/config/SlbNewCfgSslPolicyTable/KF-1-443")
                   .Respond("application/json",
                       AlteonResponseFactory.SslPolicyTableResponse(new[]
                       {
                           AlteonResponseFactory.SslPolicyEntry("KF-1-443")
                       }));
            // POST should NOT be called — if it is, MockHttp will throw

            var client = BuildClient(handler);
            await client.EnsureSslPolicyAsync("KF-1-443");

            // If POST was called, MockHttp would have thrown UnexpectedRequestException
            // Reaching here means only GET was called — correct behaviour
        }

        // ── Cert group management (SNI) ──────────────────────────────────────

        [Fact]
        public async Task AddCertToExistingGroup_CertNotAlreadyMember_AddsIt()
        {
            var handler = new MockHttpMessageHandler();
            // GET the existing group
            handler.When(HttpMethod.Get,
                         $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/my-group")
                   .Respond("application/json",
                       AlteonResponseFactory.CertGroupTableResponse(new[]
                       {
                           AlteonResponseFactory.CertGroupEntry(
                               "my-group", "existing-cert", "existing-cert")
                       }));
            // PUT to add the new cert to the group
            handler.When(HttpMethod.Put,
                         $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/my-group")
                   .Respond("application/json", AlteonResponseFactory.OkResponse());

            var client = BuildClient(handler);
            await client.AddCertToGroupAsync("my-group", "new-cert");

            handler.VerifyNoOutstandingExpectation();
        }

        [Fact]
        public async Task AddCertToExistingGroup_CertAlreadyMember_IsIdempotent()
        {
            var handler = new MockHttpMessageHandler();
            handler.When(HttpMethod.Get,
                         $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/my-group")
                   .Respond("application/json",
                       AlteonResponseFactory.CertGroupTableResponse(new[]
                       {
                           AlteonResponseFactory.CertGroupEntry(
                               "my-group", "existing-cert", "existing-cert", "new-cert")
                       }));
            // No PUT should be issued — cert already in group

            var client = BuildClient(handler);
            await client.AddCertToGroupAsync("my-group", "new-cert");

            // Reaching here without exception = idempotent, no PUT fired
        }

        // ── RemoveCertFromGroup ──────────────────────────────────────────────

        [Fact]
        public async Task RemoveCertFromGroup_IsDefaultCert_ThrowsWithExplanation()
        {
            var handler = new MockHttpMessageHandler();
            handler.When(HttpMethod.Get,
                         $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/my-group")
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
        public async Task RemoveCertFromGroup_IsNonDefaultMember_RemovesIt()
        {
            var handler = new MockHttpMessageHandler();
            handler.When(HttpMethod.Get,
                         $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/my-group")
                   .Respond("application/json",
                       AlteonResponseFactory.CertGroupTableResponse(new[]
                       {
                           AlteonResponseFactory.CertGroupEntry(
                               "my-group", "default-cert", "default-cert", "target-cert")
                       }));
            handler.When(HttpMethod.Put,
                         $"{BaseUrl}/config/SlbNewCfgSslCertGroupTable/my-group")
                   .Respond("application/json", AlteonResponseFactory.OkResponse());

            var client = BuildClient(handler);
            await client.RemoveCertFromGroupAsync("my-group", "target-cert");

            handler.VerifyNoOutstandingExpectation();
        }

        // ── Helper ───────────────────────────────────────────────────────────

        private static AlteonLoadBalancerClient BuildClient(MockHttpMessageHandler handler)
        {
            // AlteonLoadBalancerClient needs to accept an optional HttpMessageHandler
            // for testability.  Add an overload or constructor parameter to the main
            // project to support this.
            return new AlteonLoadBalancerClient(
                BaseUrl, Username, Password,
                NullLogger.Instance,
                handler.ToHttpClient());
        }
    }
}
