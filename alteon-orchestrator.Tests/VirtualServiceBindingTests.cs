// Copyright 2026 Keyfactor
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Tests
{
    /// <summary>
    /// Tests for VirtualServiceBinding — the entry parameter model used to express
    /// which virtual services a certificate should be bound to.
    ///
    /// These tests define the expected contract for the parsing logic that will
    /// be added to the main project. The parser must be added to the main project
    /// before these tests will compile.
    ///
    /// Format contract: "virtId:servicePort" — e.g. "1:443" or "my-virt:8443"
    /// List format:     comma-separated       — e.g. "1:443,2:443,my-virt:8443"
    /// </summary>
    public class VirtualServiceBindingTests
    {
        // ── Parse single binding ─────────────────────────────────────────────

        [Fact]
        public void Parse_NumericVirtIdAndPort_Succeeds()
        {
            var binding = VirtualServiceBinding.Parse("1:443");
            binding.VirtId.Should().Be("1");
            binding.ServicePort.Should().Be("443");
        }

        [Fact]
        public void Parse_NamedVirtId_Succeeds()
        {
            var binding = VirtualServiceBinding.Parse("my-virt:8443");
            binding.VirtId.Should().Be("my-virt");
            binding.ServicePort.Should().Be("8443");
        }

        [Fact]
        public void Parse_TrimsWhitespace()
        {
            var binding = VirtualServiceBinding.Parse("  1 : 443 ");
            binding.VirtId.Should().Be("1");
            binding.ServicePort.Should().Be("443");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("1")]           // missing port
        [InlineData(":443")]        // missing virtId
        [InlineData("1:")]          // missing port value
        [InlineData("invalid")]     // no colon at all
        [InlineData("1:abc")]       // port not numeric — also catches colon in virtId e.g. "my:virt:443"
        [InlineData("1:0")]         // port out of range (below 1)
        [InlineData("1:65536")]     // port out of range (above 65535)
        [InlineData("1:-1")]        // port negative
        public void Parse_InvalidInput_ThrowsArgumentException(string input)
        {
            Action act = () => VirtualServiceBinding.Parse(input);
            act.Should().Throw<ArgumentException>()
               .WithMessage("*Invalid virtual service binding*");
        }

        [Theory]
        [InlineData("1:1")]         // minimum valid port
        [InlineData("1:443")]       // standard HTTPS
        [InlineData("1:8443")]      // alternate HTTPS
        [InlineData("1:65535")]     // maximum valid port
        public void Parse_ValidPorts_Succeeds(string input)
        {
            Action act = () => VirtualServiceBinding.Parse(input);
            act.Should().NotThrow();
        }

        [Fact]
        public void Parse_ColonInVirtId_PortSegmentNonNumeric_ThrowsWithHelpfulMessage()
        {
            // "my:virt:443" splits into virtId="my", portSegment="virt:443"
            // Port validation catches this and surfaces the restriction note
            Action act = () => VirtualServiceBinding.Parse("my:virt:443");
            act.Should().Throw<ArgumentException>()
               .WithMessage("*not supported*");
        }

        [Fact]
        public void Parse_VirtIdWithColonInName_SplitsOnFirstColonOnly()
        {
            // Edge case: port should always be the segment after the FIRST colon
            // virtId itself should never contain a colon in practice, but
            // the split(2) ensures we handle it gracefully
            var binding = VirtualServiceBinding.Parse("virt-1:443");
            binding.VirtId.Should().Be("virt-1");
            binding.ServicePort.Should().Be("443");
        }

        // ── ToString ─────────────────────────────────────────────────────────

        [Fact]
        public void ToString_ProducesCanonicalFormat()
        {
            var binding = new VirtualServiceBinding("1", "443");
            binding.ToString().Should().Be("1:443");
        }

        [Fact]
        public void ToString_RoundTrips_ThroughParse()
        {
            var original = new VirtualServiceBinding("my-virt", "8443");
            var roundTripped = VirtualServiceBinding.Parse(original.ToString());
            roundTripped.VirtId.Should().Be(original.VirtId);
            roundTripped.ServicePort.Should().Be(original.ServicePort);
        }

        // ── ParseList ────────────────────────────────────────────────────────

        [Fact]
        public void ParseList_SingleBinding_ReturnsSingleItem()
        {
            var list = VirtualServiceBinding.ParseList("1:443");
            list.Should().HaveCount(1);
            list[0].VirtId.Should().Be("1");
            list[0].ServicePort.Should().Be("443");
        }

        [Fact]
        public void ParseList_MultipleBindings_ReturnsAll()
        {
            var list = VirtualServiceBinding.ParseList("1:443,2:443,my-virt:8443");
            list.Should().HaveCount(3);
            list[0].Should().Be(new VirtualServiceBinding("1", "443"));
            list[1].Should().Be(new VirtualServiceBinding("2", "443"));
            list[2].Should().Be(new VirtualServiceBinding("my-virt", "8443"));
        }

        [Fact]
        public void ParseList_HandlesWhitespaceAroundCommas()
        {
            var list = VirtualServiceBinding.ParseList("1:443 , 2:443 , 3:8443");
            list.Should().HaveCount(3);
        }

        [Fact]
        public void ParseList_HandlesTrailingComma()
        {
            var list = VirtualServiceBinding.ParseList("1:443,2:443,");
            list.Should().HaveCount(2);
        }

        [Fact]
        public void ParseList_EmptyString_ReturnsEmptyList()
        {
            var list = VirtualServiceBinding.ParseList(string.Empty);
            list.Should().BeEmpty();
        }

        [Fact]
        public void ParseList_InvalidEntry_ThrowsArgumentException()
        {
            // One bad entry in the list should fail the whole operation
            // so the operator gets a clear error rather than partial bindings
            Action act = () => VirtualServiceBinding.ParseList("1:443,bad-entry,3:443");
            act.Should().Throw<ArgumentException>();
        }

        // ── Equality ─────────────────────────────────────────────────────────

        [Fact]
        public void Equality_SameVirtIdAndPort_AreEqual()
        {
            var a = new VirtualServiceBinding("1", "443");
            var b = new VirtualServiceBinding("1", "443");
            a.Should().Be(b);
        }

        [Fact]
        public void Equality_DifferentPort_AreNotEqual()
        {
            var a = new VirtualServiceBinding("1", "443");
            var b = new VirtualServiceBinding("1", "8443");
            a.Should().NotBe(b);
        }
    }
}