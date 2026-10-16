using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenTranslator.Core;

public sealed class BaiduTranslator
{
    public const int MaximumTextBytes = 6000;
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly Uri Endpoint = new("https://fanyi-api.baidu.com/api/trans/vip/translate");
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    public BaiduTranslator(HttpClient httpClient, TimeSpan? requestTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
        if (_requestTimeout <= TimeSpan.Zero || _requestTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "Request timeout must be positive and finite.");
    }

    public async Task<string> TranslateAsync(
        string text,
        TranslationCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text))
            throw new TranslationException("未识别到可翻译的文字，请重新选择文字区域。");
        if (Encoding.UTF8.GetByteCount(text) > MaximumTextBytes)
            throw new TranslationException("文字过长（最多 6000 个 UTF-8 字节），请缩小识别区域。");
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.AppId) ||
            string.IsNullOrWhiteSpace(credentials.SecretKey))
            throw new TranslationException("请先在设置中填写百度翻译 App ID 和密钥。");

        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = text,
                ["from"] = "en",
                ["to"] = "zh",
                ["appid"] = credentials.AppId,
                ["salt"] = salt,
                ["sign"] = CreateSignature(credentials.AppId, text, salt, credentials.SecretKey)
            })
        };

        // HttpClient.Timeout ends at response headers with ResponseHeadersRead; cover the body too.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        var requestToken = deadline.Token;

        try
        {
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new TranslationException(HttpErrorMessage(response.StatusCode));

            using var body = await ReadBoundedResponseAsync(response.Content, requestToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(body,
                new JsonDocumentOptions { MaxDepth = 32 }, requestToken).ConfigureAwait(false);
            return ParseTranslation(document.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new TranslationException("翻译请求超时，请检查网络后重试。");
        }
        catch (HttpRequestException)
        {
            // Do not preserve transport exception messages/inner exceptions: they may contain request data.
            throw new TranslationException("无法连接百度翻译服务，请检查网络连接后重试。");
        }
        catch (IOException)
        {
            throw new TranslationException("读取翻译结果失败，请检查网络后重试。");
        }
        catch (JsonException)
        {
            throw new TranslationException("翻译服务返回了无法解析的数据，请稍后重试。");
        }
    }

    /// <summary>Baidu's required MD5 signature over the original UTF-8 text, before form encoding.</summary>
    public static string CreateSignature(string appId, string text, string salt, string secretKey)
    {
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(secretKey);
        // MD5 is required by this API's signing protocol; it is not used for storing credentials.
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(appId + text + salt + secretKey)))
            .ToLowerInvariant();
    }

    private static async Task<MemoryStream> ReadBoundedResponseAsync(
        HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
            throw new TranslationException("翻译服务返回的数据过大，请稍后重试。");

        var result = new MemoryStream();
        try
        {
            await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (result.Length + read > MaximumResponseBytes)
                    throw new TranslationException("翻译服务返回的数据过大，请稍后重试。");
                result.Write(buffer, 0, read);
            }
            result.Position = 0;
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static string ParseTranslation(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw InvalidResponse();

        if (root.TryGetProperty("error_code", out var error))
        {
            var code = error.ValueKind switch
            {
                JsonValueKind.String => error.GetString(),
                JsonValueKind.Number when error.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
                _ => null
            };
            if (code != "0" && code != "52000")
                throw new TranslationException(ApiErrorMessage(code));
        }

        if (!root.TryGetProperty("trans_result", out var translations) ||
            translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() == 0)
            throw InvalidResponse();

        var lines = new List<string>();
        foreach (var item in translations.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("dst", out var destination) ||
                destination.ValueKind != JsonValueKind.String)
                throw InvalidResponse();
            lines.Add(destination.GetString()!);
        }

        var result = string.Join('\n', lines);
        if (string.IsNullOrWhiteSpace(result))
            throw InvalidResponse();
        return result;
    }

    private static TranslationException InvalidResponse() =>
        new("翻译服务未返回有效译文，请稍后重试。");

    private static string HttpErrorMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "翻译服务拒绝了请求，请检查网络、App ID、密钥及服务权限。",
        HttpStatusCode.TooManyRequests => "翻译请求过于频繁，请稍等片刻后重试。",
        _ => "翻译服务暂时不可用，请稍后重试。"
    };

    private static string ApiErrorMessage(string? code) => code switch
    {
        "52001" => "百度翻译请求超时，请稍后重试。",
        "52002" => "百度翻译服务暂时异常，请稍后重试。",
        "52003" => "百度翻译 App ID 无效，请检查设置中的 App ID。",
        "54000" => "翻译请求参数不完整，请重新配置翻译服务。",
        "54001" => "百度翻译签名验证失败，请检查 App ID 与密钥是否匹配。",
        "54003" => "百度翻译请求过于频繁，请增加自动翻译间隔后重试。",
        "54004" => "百度翻译账户余额不足，请前往百度翻译开放平台检查账户。",
        "54005" => "长文本翻译请求过于频繁，请稍等片刻后重试。",
        "58000" => "百度翻译不允许当前 IP，请检查开放平台的 IP 白名单设置。",
        "58001" => "百度翻译不支持当前语言方向，请检查服务设置。",
        "58002" => "百度翻译服务已关闭，请在开放平台启用通用文本翻译服务。",
        "90107" => "百度翻译认证未通过或服务未开通，请检查开放平台账户状态。",
        _ => "百度翻译未能完成请求，请检查开放平台的账户和服务状态后重试。"
    };
}
