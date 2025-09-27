// 依赖: Newtonsoft.Json (Install-Package Newtonsoft.Json)
// 目标: C# 7.x 兼容 (不使用 IAsyncEnumerable 或 C# 8 特性)

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MinorShift.Emuera;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ChatClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private string _endpoint;
    private bool _disposed;

    public ChatClient(HttpMessageHandler handler = null)
    {
        if (!GlobalStatic.AiConfig.UseAi)
        {
            throw new Exception("Should not use ai! Please check ai_config.txt");
        }  
        
        // if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("apiKey required", nameof(apiKey));
        if (string.IsNullOrEmpty(GlobalStatic.AiConfig.Url))
        {
            throw new Exception("Ai url not found! Please check ai_config.txt");
        }
        _endpoint = GlobalStatic.AiConfig.Url;

        _httpClient = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + GlobalStatic.AiConfig.Token);
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _httpClient.Dispose();
        _disposed = true;
    }

    private string BuildRequestBody(object[] messages, bool stream)
    {
        var body = new JObject
        {
            ["model"] = "nalang-xl-10",
            ["messages"] = JArray.FromObject(messages),
            ["stream"] = stream,
            ["temperature"] = 0.7,
            ["max_tokens"] = 800,
            ["top_p"] = 0.35,
            ["repetition_penalty"] = 1.05
        };
        return body.ToString(Formatting.None);
    }

    public async Task<string> GetAllAsync(object[] messages, CancellationToken cancellationToken = default)
    {
        var requestJson = BuildRequestBody(messages, stream: true); // 使用流式以便兼容增量输出的服务
        using (var content = new StringContent(requestJson, Encoding.UTF8, "application/json"))
        using (var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            // 读取响应流并完整解析所有 data: 行，汇总 content 字段
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var sbTotal = new StringBuilder();
                var sbLine = new StringBuilder();
                var buffer = new char[4096];

                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0) break;
                    sbLine.Append(buffer, 0, read);

                    int newlineIndex;
                    while ((newlineIndex = IndexOfNewline(sbLine)) >= 0)
                    {
                        var line = sbLine.ToString(0, newlineIndex).TrimEnd('\r');
                        sbLine.Remove(0, newlineIndex + 1);

                        if (string.IsNullOrWhiteSpace(line)) continue;
                        const string prefix = "data: ";
                        if (line.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            var payload = line.Substring(prefix.Length).Trim();
                            if (string.IsNullOrEmpty(payload) || payload == "[DONE]") continue;
                            try
                            {
                                var json = JObject.Parse(payload);
                                var completedToken = json["completed"];
                                if (completedToken != null && completedToken.Type == JTokenType.Boolean &&
                                    completedToken.Value<bool>())
                                {
                                    // 完成事件，直接返回已累积的结果
                                    return sbTotal.ToString();
                                }

                                var contentToken = json.SelectToken("choices[0].delta.content") ??
                                                   json.SelectToken("choices[0].message.content");
                                if (contentToken != null)
                                {
                                    var contentStr = contentToken.ToString();
                                    if (!string.IsNullOrEmpty(contentStr)) sbTotal.Append(contentStr);
                                }
                            }
                            catch (JsonException)
                            {
                                // 忽略无法解析的片段
                            }
                        }
                        else
                        {
                            // 若服务直接返回 JSON 行，则尝试解析并提取 content
                            try
                            {
                                var json = JObject.Parse(line);
                                var contentToken = json.SelectToken("choices[0].delta.content") ??
                                                   json.SelectToken("choices[0].message.content");
                                if (contentToken != null)
                                {
                                    var contentStr = contentToken.ToString();
                                    if (!string.IsNullOrEmpty(contentStr)) sbTotal.Append(contentStr);
                                }
                            }
                            catch (JsonException)
                            {
                                // 忽略
                            }
                        }
                    }
                }

                // 处理剩余缓冲区
                var remaining = sbLine.ToString().Trim();
                if (!string.IsNullOrEmpty(remaining))
                {
                    foreach (var line in remaining.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var l = line.Trim('\r');
                        if (l.StartsWith("data: ", StringComparison.Ordinal))
                        {
                            var payload = l.Substring(6).Trim();
                            if (string.IsNullOrEmpty(payload) || payload == "[DONE]") continue;
                            try
                            {
                                var json = JObject.Parse(payload);
                                var contentToken = json.SelectToken("choices[0].delta.content") ??
                                                   json.SelectToken("choices[0].message.content");
                                if (contentToken != null)
                                {
                                    var contentStr = contentToken.ToString();
                                    if (!string.IsNullOrEmpty(contentStr)) sbTotal.Append(contentStr);
                                }
                            }
                            catch (JsonException)
                            {
                            }
                        }
                    }
                }

                return sbTotal.ToString();
            }
        }
    }


    public async Task StartStreamingAsync(object[] messages, Action<string> onChunk,
        CancellationToken cancellationToken = default)
    {
        if (onChunk == null) throw new ArgumentNullException(nameof(onChunk));
        var requestJson = BuildRequestBody(messages, stream: true);
        using (var content = new StringContent(requestJson, Encoding.UTF8, "application/json"))
        using (var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var buffer = new char[4096];
                var sb = new StringBuilder();
                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0) break;
                    sb.Append(buffer, 0, read);

                    int newlineIndex;
                    while ((newlineIndex = IndexOfNewline(sb)) >= 0)
                    {
                        var line = sb.ToString(0, newlineIndex).TrimEnd('\r');
                        sb.Remove(0, newlineIndex + 1);

                        if (string.IsNullOrWhiteSpace(line)) continue;
                        const string prefix = "data: ";
                        if (line.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            var payload = line.Substring(prefix.Length).Trim();
                            if (string.IsNullOrEmpty(payload) || payload == "[DONE]") continue;
                            try
                            {
                                var json = JObject.Parse(payload);
                                // completed 字段判断（如果存在）
                                var completedToken = json["completed"];
                                if (completedToken != null && completedToken.Type == JTokenType.Boolean &&
                                    completedToken.Value<bool>())
                                {
                                    // 不输出到控制台，直接结束
                                    return;
                                }

                                var contentToken = json.SelectToken("choices[0].delta.content");
                                if (contentToken != null)
                                {
                                    var contentStr = contentToken.ToString();
                                    if (!string.IsNullOrEmpty(contentStr))
                                    {
                                        onChunk(contentStr);
                                    }
                                }
                            }
                            catch (JsonException)
                            {
                                // 忽略无法解析的片段
                            }
                        }
                        else
                        {
                            // 有些服务可能直接返回 JSON 每行或其他格式，尝试解析整行
                            try
                            {
                                var json = JObject.Parse(line);
                                var contentToken = json.SelectToken("choices[0].delta.content") ??
                                                   json.SelectToken("choices[0].message.content");
                                if (contentToken != null)
                                {
                                    var contentStr = contentToken.ToString();
                                    if (!string.IsNullOrEmpty(contentStr)) onChunk(contentStr);
                                }
                            }
                            catch (JsonException)
                            {
                                // 忽略
                            }
                        }
                    }
                }

                // 流结束后处理剩余缓冲
                var remaining = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(remaining))
                {
                    foreach (var line in remaining.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var l = line.Trim('\r');
                        if (l.StartsWith("data: ", StringComparison.Ordinal))
                        {
                            var payload = l.Substring(6).Trim();
                            if (string.IsNullOrEmpty(payload) || payload == "[DONE]") continue;
                            try
                            {
                                var json = JObject.Parse(payload);
                                var contentToken = json.SelectToken("choices[0].delta.content");
                                if (contentToken != null)
                                {
                                    var contentStr = contentToken.ToString();
                                    if (!string.IsNullOrEmpty(contentStr)) onChunk(contentStr);
                                }
                            }
                            catch (JsonException)
                            {
                            }
                        }
                    }
                }
            }
        }
    }

    private static int IndexOfNewline(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++)
        {
            var c = sb[i];
            if (c == '\n') return i;
        }

        return -1;
    }
    
    public class StreamingChatClient
    {
        private readonly ChatClient chatClient;
        private readonly Action<string> onChunk;

        public StreamingChatClient(Action<string> onChunk ,HttpMessageHandler handler = null)
        {
            chatClient = new ChatClient(handler);
            this.onChunk = onChunk;
        }

        // 推荐：完全异步的方法，避免混合同步等待
        public async Task RunStreamingExampleAsync()
        {
        
            // 准备请求消息
            var messages = new object[]
            {
                new { role = "user", content = "请给我讲一个故事" }
            };

            // 使用TaskCompletionSource跟踪完成状态
            var tcs = new TaskCompletionSource<bool>();
        
            // 启动流式传输
            await chatClient.StartStreamingAsync(
                messages,
                // 处理每个数据块，输出到控制台
                onChunk,
                CancellationToken.None);
        }

        public void RunStreaming()
        {
            // 使用正确的上下文处理来避免死锁
            var syncContext = SynchronizationContext.Current;
            if (syncContext != null)
            {
                // 如果有同步上下文，使用Post避免阻塞
                var waitHandle = new ManualResetEventSlim(false);
                syncContext.Post(async _ =>
                {
                    try
                    {
                        await RunStreamingExampleAsync();
                    }
                    finally
                    {
                        waitHandle.Set();
                    }
                }, null);
                waitHandle.Wait();
            }
            else
            {
                // 没有同步上下文时直接等待
                RunStreamingExampleAsync().GetAwaiter().GetResult();
            }
        }
    }
}