using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MinorShift.Emuera;
using Newtonsoft.Json.Linq;
using System.Net.Http;

public class ChatClient : IDisposable
{
    private readonly WebRequestHandler _handler;
    private string _endpoint;
    private bool _disposed;
    private int _timeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds; // 超时时间（毫秒）

    public ChatClient(WebRequestHandler handler = null)
    {
        if (!GlobalStatic.AiConfig.UseAi)
        {
            throw new Exception("Should not use ai! Please check ai_config.txt");
        }  

        if (string.IsNullOrEmpty(GlobalStatic.AiConfig.Url))
        {
            throw new Exception("Ai url not found! Please check ai_config.txt");
        }
        _endpoint = GlobalStatic.AiConfig.Url;

        _handler = handler ?? new WebRequestHandler();
    }

    // 添加超时设置属性
    public int Timeout
    {
        get { return _timeout; }
        set { _timeout = value; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _handler.Dispose();
        _disposed = true;
    }

    private string BuildRequestBody(object[] messages, bool stream)
    {
        var body = new JObject();
        foreach (KeyValuePair<string, JToken> kv in GlobalStatic.AiConfig.bodyMap)
        {
            body[kv.Key] = kv.Value;
        }

        body["stream"] = stream;
        body["messages"] = JArray.FromObject(messages);
        return body.ToString(Newtonsoft.Json.Formatting.None);
    }

    public Task<string> GetAllAsync(object[] messages, CancellationToken cancellationToken = default(CancellationToken))
    {
        var tcs = new TaskCompletionSource<string>();
        
        if (cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled();
            return tcs.Task;
        }

        var requestJson = BuildRequestBody(messages, stream: true);
        
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(_endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers["Authorization"] = "Bearer " + GlobalStatic.AiConfig.Token;
            request.Timeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;

            // 写入请求内容
            using (var streamWriter = new StreamWriter(request.GetRequestStream()))
            {
                streamWriter.Write(requestJson);
            }

            // 开始异步请求
            request.BeginGetResponse(result =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.SetCanceled();
                        return;
                    }

                    using (HttpWebResponse response = (HttpWebResponse)request.EndGetResponse(result))
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            tcs.SetException(new WebException($"请求失败: {response.StatusCode}"));
                            return;
                        }

                        using (var stream = response.GetResponseStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            var sbTotal = new StringBuilder();
                            var sbLine = new StringBuilder();
                            var buffer = new char[4096];

                            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                            {
                                int read = reader.Read(buffer, 0, buffer.Length);
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
                                                tcs.SetResult(sbTotal.ToString());
                                                return;
                                            }

                                            var contentToken = json.SelectToken("choices[0].delta.content") ??
                                                               json.SelectToken("choices[0].message.content");
                                            if (contentToken != null)
                                            {
                                                var contentStr = contentToken.ToString();
                                                if (!string.IsNullOrEmpty(contentStr)) sbTotal.Append(contentStr);
                                            }
                                        }
                                        catch (Newtonsoft.Json.JsonException)
                                        {
                                            // 忽略无法解析的片段
                                        }
                                    }
                                    else
                                    {
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
                                        catch (Newtonsoft.Json.JsonException)
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
                                        catch (Newtonsoft.Json.JsonException)
                                        {
                                        }
                                    }
                                }
                            }

                            tcs.SetResult(sbTotal.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            // 注册取消令牌
            cancellationToken.Register(() =>
            {
                try
                {
                    request.Abort();
                }
                catch { }
                tcs.TrySetCanceled();
            });
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }

        return tcs.Task;
    }

    public Task StartStreamingAsync(object[] messages, Action<string> onChunk,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        if (onChunk == null)
            throw new ArgumentNullException(nameof(onChunk));

        var tcs = new TaskCompletionSource<object>();
        var requestJson = BuildRequestBody(messages, stream: true);

        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(_endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers["Authorization"] = "Bearer " + GlobalStatic.AiConfig.Token;
            request.Timeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;

            // 写入请求内容
            using (var streamWriter = new StreamWriter(request.GetRequestStream()))
            {
                streamWriter.Write(requestJson);
            }

            // 开始异步请求
            request.BeginGetResponse(result =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.SetCanceled();
                        return;
                    }

                    using (HttpWebResponse response = (HttpWebResponse)request.EndGetResponse(result))
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            tcs.SetException(new WebException($"请求失败: {response.StatusCode}"));
                            return;
                        }

                        using (var stream = response.GetResponseStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            var buffer = new char[4096];
                            var sb = new StringBuilder();
                            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                            {
                                int read = reader.Read(buffer, 0, buffer.Length);
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
                                            var completedToken = json["completed"];
                                            if (completedToken != null && completedToken.Type == JTokenType.Boolean &&
                                                completedToken.Value<bool>())
                                            {
                                                tcs.SetResult(null);
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
                                        catch (Newtonsoft.Json.JsonException)
                                        {
                                            // 忽略无法解析的片段
                                        }
                                    }
                                    else
                                    {
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
                                        catch (Newtonsoft.Json.JsonException)
                                        {
                                            // 忽略
                                        }
                                    }
                                }
                            }

                            // 处理剩余缓冲区
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
                                        catch (Newtonsoft.Json.JsonException)
                                        {
                                        }
                                    }
                                }
                            }

                            tcs.SetResult(null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            // 注册取消令牌
            cancellationToken.Register(() =>
            {
                try
                {
                    request.Abort();
                }
                catch { }
                tcs.TrySetCanceled();
            });
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }

        return tcs.Task;
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

        public StreamingChatClient(Action<string> onChunk, WebRequestHandler handler = null)
        {
            chatClient = new ChatClient(handler);
            this.onChunk = onChunk;
        }

        public Task RunStreamingExampleAsync()
        {
            // 准备请求消息
            var messages = new object[]
            {
                new { role = "user", content = "请给我讲一个故事" }
            };

            // 启动流式传输
            return chatClient.StartStreamingAsync(
                messages,
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
                syncContext.Post(_ =>
                {
                    try
                    {
                        RunStreamingExampleAsync().ContinueWith(task =>
                        {
                            waitHandle.Set();
                        });
                    }
                    finally
                    {
                        // 确保等待句柄被设置
                    }
                }, null);
                waitHandle.Wait();
            }
            else
            {
                // 没有同步上下文时直接等待
                RunStreamingExampleAsync().Wait();
            }
        }
    }
}
