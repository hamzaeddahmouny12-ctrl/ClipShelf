using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClipShelf.Services;
using System.IO;

namespace ClipShelf.Tests
{
    [TestClass]
    public class ParserTests
    {
        [TestMethod]
        public void ParseBoundary_HandlesQuotedAndExtra()
        {
            Assert.AreEqual("abc", SyncWebServer.ParseBoundary("multipart/form-data; boundary=abc"));
            Assert.AreEqual("abc", SyncWebServer.ParseBoundary("multipart/form-data; boundary=\"abc\""));
            Assert.AreEqual("abc", SyncWebServer.ParseBoundary("multipart/form-data; boundary=abc; charset=utf-8"));
        }

        [TestMethod]
        public void IsLocalNetwork_RecognizesPrivateIps()
        {
            Assert.IsTrue(SyncWebServer.IsLocalNetwork("127.0.0.1"));
            Assert.IsTrue(SyncWebServer.IsLocalNetwork("192.168.1.1"));
            Assert.IsTrue(SyncWebServer.IsLocalNetwork("10.0.0.5"));
            Assert.IsTrue(SyncWebServer.IsLocalNetwork("172.16.5.4"));
            Assert.IsFalse(SyncWebServer.IsLocalNetwork("8.8.8.8"));
        }

        [TestMethod]
        public void GeneratePin_Returns6Digits()
        {
            var server = new SyncWebServer(1234, Path.GetTempPath());
            var pin = server.GeneratePin();
            Assert.IsNotNull(pin);
            Assert.AreEqual(6, pin.Length);
            Assert.IsTrue(int.TryParse(pin, out _));
        }
    }
}
