// Copyright 2026 Keyfactor
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Tests
{
    /// <summary>
    /// Regression tests for the 406 NotAcceptable bug on Apply and Save.
    ///
    /// Root cause: ApplyChanges and SaveChanges sent Accept: application/json,
    /// but the Alteon /config?action=apply|save endpoints return plain text (or
    /// an empty body), so the device rejected every call with 406 NotAcceptable.
    ///
    /// Fix: both headers changed to Accept: */*.
    ///
    /// These tests verify:
    ///   1. The outgoing request carries Accept: */* (not application/json).
    ///   2. A plain-text response body is handled without throwing.
    /// </summary>
    public class AlteonClientApplySaveTests
    {
        private const string BaseUrl  = "https://192.168.1.168";
        private const string Username = "admin";
        private const string Password = "admin";
        private const string ConfigUrl = $"{BaseUrl}/config";

        // ── ApplyChanges ──────────────────────────────────────────────────────

        [Fact]
        public async Task ApplyChanges_SendsAcceptWildcard_NotJson()
        {
            string? capturedAccept = null;

            var mock = new MockHttpMessageHandler();
            mock.When(HttpMethod.Post, ConfigUrl)
                .With(req =>
                {
                    capturedAccept = req.Headers
                        .Accept
                        .FirstOrDefault()?.MediaType;
                    return true;
                })
                .Respond(HttpStatusCode.OK, "text/plain", "OK");

            var client = BuildClient(mock);
            await client.Invoking(c => c.ApplyChanges())
                        .Should().NotThrowAsync();

            capturedAccept.Should().Be("*/*",
                because: "Alteon /config?action=apply returns plain text, not JSON — " +
                         "sending Accept: application/json causes a 406 NotAcceptable");
        }

        [Fact]
        public async Task ApplyChanges_PlainTextResponseBody_DoesNotThrow()
        {
            var mock = new MockHttpMessageHandler();
            mock.When(HttpMethod.Post, ConfigUrl)
                .Respond(HttpStatusCode.OK, "text/plain", "Apply OK");

            var client = BuildClient(mock);
            await client.Invoking(c => c.ApplyChanges())
                        .Should().NotThrowAsync(
                            because: "a plain-text response body must not cause a deserialization failure");
        }

        // ── SaveChanges ───────────────────────────────────────────────────────

        [Fact]
        public async Task SaveChanges_SendsAcceptWildcard_NotJson()
        {
            string? capturedAccept = null;

            var mock = new MockHttpMessageHandler();
            mock.When(HttpMethod.Post, ConfigUrl)
                .With(req =>
                {
                    capturedAccept = req.Headers
                        .Accept
                        .FirstOrDefault()?.MediaType;
                    return true;
                })
                .Respond(HttpStatusCode.OK, "text/plain", "OK");

            var client = BuildClient(mock);
            await client.Invoking(c => c.SaveChanges())
                        .Should().NotThrowAsync();

            capturedAccept.Should().Be("*/*",
                because: "Alteon /config?action=save returns plain text, not JSON — " +
                         "sending Accept: application/json causes a 406 NotAcceptable");
        }

        [Fact]
        public async Task SaveChanges_PlainTextResponseBody_DoesNotThrow()
        {
            var mock = new MockHttpMessageHandler();
            mock.When(HttpMethod.Post, ConfigUrl)
                .Respond(HttpStatusCode.OK, "text/plain", "Save OK");

            var client = BuildClient(mock);
            await client.Invoking(c => c.SaveChanges())
                        .Should().NotThrowAsync(
                            because: "a plain-text response body must not cause a deserialization failure");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static AlteonLoadBalancerClient BuildClient(MockHttpMessageHandler mock) =>
            new AlteonLoadBalancerClient(
                BaseUrl, Username, Password,
                NullLogger.Instance,
                mock.ToHttpClient());
    }
}
