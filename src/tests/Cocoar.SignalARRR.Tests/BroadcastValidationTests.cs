using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace Cocoar.SignalARRR.Tests {

    public class BroadcastValidationTests {

        [Fact]
        public void Invoke_StreamReturn_ThrowsNotSupported() {
            var helper = CreateHelper();

            Assert.Throws<NotSupportedException>(() =>
                helper.Invoke<Stream>("TestMethod", Array.Empty<object>(), Array.Empty<string>()));
        }

        [Fact]
        public async Task InvokeAsync_StreamReturn_ThrowsNotSupported() {
            var helper = CreateHelper();

            await Assert.ThrowsAsync<NotSupportedException>(() =>
                helper.InvokeAsync<Stream>("TestMethod", Array.Empty<object>(), Array.Empty<string>()));
        }

        [Fact]
        public void Send_StreamArgument_ThrowsNotSupported() {
            var helper = CreateHelper();

            var stream = new MemoryStream();
            Assert.Throws<NotSupportedException>(() =>
                helper.Send("TestMethod", new object[] { stream }, Array.Empty<string>()));
        }

        [Fact]
        public async Task SendAsync_StreamArgument_ThrowsNotSupported() {
            var helper = CreateHelper();

            var stream = new MemoryStream();
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                helper.SendAsync("TestMethod", new object[] { stream }, Array.Empty<string>()));
        }

        [Fact]
        public void Invoke_StreamArgument_ThrowsNotSupported() {
            var helper = CreateHelper();

            var stream = new MemoryStream();
            Assert.Throws<NotSupportedException>(() =>
                helper.Invoke<string>("TestMethod", new object[] { stream }, Array.Empty<string>()));
        }

        [Fact]
        public void Send_NoStream_Works() {
            var helper = CreateHelper();

            // Should NOT throw
            helper.Send("TestMethod", new object[] { "hello", 42 }, Array.Empty<string>());
        }

        [Fact]
        public void Invoke_NonStreamReturn_LogsWarningButWorks() {
            var helper = CreateHelper();

            // Should NOT throw — returns default, logs warning
            var result = helper.Invoke<string>("TestMethod", Array.Empty<object>(), Array.Empty<string>());
            Assert.Null(result);
        }

        [Fact]
        public void StreamAsync_ThrowsNotSupported() {
            var helper = CreateHelper();

            Assert.Throws<NotSupportedException>(() =>
                helper.StreamAsync<int>("TestMethod", Array.Empty<object>(), Array.Empty<string>()));
        }

        private static BroadcastProxyCreatorHelper CreateHelper() {
            return new BroadcastProxyCreatorHelper(new FakeClientProxy());
        }

        private class FakeClientProxy : IClientProxy {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default) =>
                Task.CompletedTask;
        }
    }
}
