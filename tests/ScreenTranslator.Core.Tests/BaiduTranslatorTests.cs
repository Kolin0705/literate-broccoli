using System.Net;
using System.Text;
using ScreenTranslator.Core;
using Xunit;

namespace ScreenTranslator.Core.Tests;

public sealed class BaiduTranslatorTests
{
    private static readonly TranslationCredentials Credentials = new("test-app-id", "do-not-leak-secret");

    [Theory]
    [InlineData("2015063000000001", "apple", "1435660288", "12345678", "f89f9594663708c1605f3d736d01d2d4")]
    [InlineData("test-app", "Café 🐉 中文 & + ?", "deadbeef", "secret-key", "692b9c89b73655caf53732115929f8c2")]
    public void SignatureUsesOriginalUtf8Text(string appId, string text, string salt, string key, string expected)
    {
        Assert.Equal(expected, BaiduTranslator.CreateSignature(appId, text, salt, key));
    }

    [Fact]
    public async Task PostsEncodedTextAndSignedParametersToHttpsEndpoint()
    {
        const string original = "Café 🐉 中文 & + ?\nDon't panic.";
        var salts = new List<string>();
        using var client = CreateClient(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://fanyi-api.baidu.com/api/trans/vip/translate", request.RequestUri!.AbsoluteUri);
            Assert.Equal(string.Empty, request.RequestUri.Query);
            Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
            var form = ParseForm(await request.Content.ReadAsStringAsync(token));
            Assert.Equal(6, form.Count);
            Assert.Equal(original, form["q"]);
            Assert.Equal("en", form["from"]);
            Assert.Equal("zh", form["to"]);
            Assert.Equal(Credentials.AppId, form["appid"]);
            Assert.Matches("^[0-9a-f]{32}$", form["salt"]);
            Assert.Equal(BaiduTranslator.CreateSignature(Credentials.AppId, original, form["salt"], Credentials.SecretKey), form["sign"]);
            Assert.DoesNotContain(Credentials.SecretKey, await request.Content.ReadAsStringAsync(token));
            salts.Add(form["salt"]);
            return Json("{\"trans_result\":[{\"src\":\"hello\",\"dst\":\"你好\"},{\"dst\":\"不要惊慌。\"}]}");
        });
        var translator = new BaiduTranslator(client);

        Assert.Equal("你好\n不要惊慌。", await translator.TranslateAsync(original, Credentials));
        Assert.Equal("你好\n不要惊慌。", await translator.TranslateAsync(original, Credentials));
        Assert.NotEqual(salts[0], salts[1]);
    }

    [Theory]
    [InlineData("52003", "App ID")]
    [InlineData("54001", "密钥")]
    [InlineData("54003", "频繁")]
    [InlineData("54004", "余额")]
    [InlineData("54005", "频繁")]
    [InlineData("58000", "IP")]
    [InlineData("58002", "启用")]
    [InlineData("90107", "认证")]
    [InlineData("999999", "账户和服务")]
    public async Task MapsApiErrorsWithoutEchoingServiceTextOrSecrets(string code, string expected)
    {
        using var client = CreateClient((_, _) => Task.FromResult(Json(
            $$"""{"error_code":"{{code}}","error_msg":"{{Credentials.SecretKey}} {{Credentials.AppId}}"}""")));

        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            new BaiduTranslator(client).TranslateAsync("hello", Credentials));

        Assert.Contains(expected, error.Message);
        AssertSafe(error);
    }

    [Fact]
    public async Task AcceptsNumericApiErrorCodes()
    {
        using var client = CreateClient((_, _) => Task.FromResult(Json("{\"error_code\":54003}")));
        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            new BaiduTranslator(client).TranslateAsync("hello", Credentials));
        Assert.Contains("频繁", error.Message);
    }

    [Theory]
    [InlineData(403, "拒绝")]
    [InlineData(429, "频繁")]
    [InlineData(503, "暂时不可用")]
    public async Task MapsHttpFailures(int status, string expected)
    {
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(Credentials.SecretKey)
        }));
        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            new BaiduTranslator(client).TranslateAsync("hello", Credentials));
        Assert.Contains(expected, error.Message);
        AssertSafe(error);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"trans_result\":[]}")]
    [InlineData("{\"trans_result\":[null]}")]
    [InlineData("{\"trans_result\":[{\"dst\":42}]}")]
    [InlineData("{\"trans_result\":[{\"dst\":\" \"}]}")]
    public async Task RejectsMalformedOrEmptyResults(string body)
    {
        using var client = CreateClient((_, _) => Task.FromResult(Json(body)));
        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            new BaiduTranslator(client).TranslateAsync("hello", Credentials));
        AssertSafe(error);
    }

    [Fact]
    public async Task RejectsOverlargeResponse()
    {
        using var client = CreateClient((_, _) => Task.FromResult(Json(new string('x', 1024 * 1024 + 1))));
        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            new BaiduTranslator(client).TranslateAsync("hello", Credentials));
        Assert.Contains("过大", error.Message);
    }

    [Fact]
    public async Task NetworkFailuresDoNotLeakExceptionContents()
    {
        using var client = CreateClient((_, _) => throw new HttpRequestException(Credentials.SecretKey));
        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            new BaiduTranslator(client).TranslateAsync("hello", Credentials));
        Assert.Contains("网络", error.Message);
        AssertSafe(error);
    }

    [Fact]
    public async Task TimeoutsHaveActionableMessage()
    {
        using var client = CreateClient((_, _) => throw new TaskCanceledException(Credentials.SecretKey));
        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            new BaiduTranslator(client).TranslateAsync("hello", Credentials));
        Assert.Contains("超时", error.Message);
        AssertSafe(error);
    }

    [Fact]
    public async Task CallerCancellationIsNotWrappedAsTranslationError()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = CreateClient(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json("{}");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BaiduTranslator(client).TranslateAsync("hello", Credentials, cancellation.Token));
    }

    [Fact]
    public async Task DeadlineCoversStalledResponseBodyAfterHeadersArrive()
    {
        using var stream = new StalledResponseStream();
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        }));
        var translator = new BaiduTranslator(client, TimeSpan.FromMilliseconds(100));

        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            translator.TranslateAsync("hello", Credentials).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Contains("超时", error.Message);
        AssertSafe(error);
        Assert.True(stream.ReadStarted.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CallerCanCancelWhileResponseBodyIsStalled()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new StalledResponseStream();
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        }));
        var translation = new BaiduTranslator(client).TranslateAsync("hello", Credentials, cancellation.Token);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            translation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        AssertSafe(error);
    }

    [Fact]
    public async Task AlreadyCancelledRequestDoesNotContactService()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = ClientThatMustNotBeCalled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BaiduTranslator(client).TranslateAsync("hello", Credentials, cancellation.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public async Task BlankTextDoesNotContactService(string text)
    {
        using var client = ClientThatMustNotBeCalled();
        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            new BaiduTranslator(client).TranslateAsync(text, Credentials));
        Assert.Contains("未识别", error.Message);
    }

    [Fact]
    public async Task LimitIsMeasuredInUtf8Bytes()
    {
        using var client = ClientThatMustNotBeCalled();
        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            new BaiduTranslator(client).TranslateAsync(new string('中', 2001), Credentials));
        Assert.Contains("6000", error.Message);
    }

    [Fact]
    public async Task AllowsExactly6000Utf8Bytes()
    {
        using var client = CreateClient((_, _) => Task.FromResult(Json("{\"trans_result\":[{\"dst\":\"结果\"}]}")));
        Assert.Equal("结果", await new BaiduTranslator(client).TranslateAsync(new string('中', 2000), Credentials));
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("app", " ")]
    public async Task MissingCredentialsDoNotContactService(string appId, string key)
    {
        using var client = ClientThatMustNotBeCalled();
        var error = await Assert.ThrowsAsync<TranslationException>(() =>
            new BaiduTranslator(client).TranslateAsync("hello", new(appId, key)));
        Assert.Contains("设置", error.Message);
    }

    [Fact]
    public void CredentialRecordDoesNotPrintSecrets()
    {
        Assert.DoesNotContain(Credentials.SecretKey, Credentials.ToString());
        Assert.DoesNotContain(Credentials.AppId, Credentials.ToString());
    }

    private static void AssertSafe(Exception exception)
    {
        Assert.DoesNotContain(Credentials.SecretKey, exception.ToString());
        Assert.DoesNotContain(Credentials.AppId, exception.ToString());
        Assert.Null(exception.InnerException);
    }

    private static Dictionary<string, string> ParseForm(string form) =>
        form.Split('&').Select(part => part.Split('=', 2)).ToDictionary(
            pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpClient ClientThatMustNotBeCalled() =>
        CreateClient((_, _) => throw new Xunit.Sdk.XunitException("Unexpected API request."));

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new FakeHandler(handler));

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class StalledResponseStream : Stream
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // A transport-level message must never leak through cancellation either.
                throw new OperationCanceledException(Credentials.SecretKey, cancellationToken);
            }
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
