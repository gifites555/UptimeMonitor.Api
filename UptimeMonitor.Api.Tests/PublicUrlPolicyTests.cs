using System.Net;
using UptimeMonitor.Api.Services;

namespace UptimeMonitor.Api.Tests;

public class PublicUrlPolicyTests
{
    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.1")]
    [InlineData("http://2130706433")]
    [InlineData("http://[::1]")]
    [InlineData("http://[::ffff:127.0.0.1]")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://172.20.0.1")]
    [InlineData("http://100.64.0.1")]
    [InlineData("http://service.local")]
    [InlineData("http://host.internal")]
    [InlineData("https://user:password@example.com")]
    [InlineData("https://example.com:8443")]
    [InlineData("https://example.com/#fragment")]
    [InlineData("ftp://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url")]
    public void RejectsUnsafeUrls(string url) => Assert.False(PublicUrlPolicy.TryValidate(url, out _));

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://www.youtube.com/watch?v=demo")]
    [InlineData("http://1.1.1.1")]
    [InlineData("https://[2606:4700:4700::1111]")]
    public void AcceptsPublicUrls(string url) => Assert.True(PublicUrlPolicy.TryValidate(url, out _));

    [Theory]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("224.0.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("203.0.113.1")]
    public void RejectsNonPublicAddresses(string address) => Assert.False(PublicUrlPolicy.IsPublicAddress(IPAddress.Parse(address)));
}
