using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stove.Net.Core.Reporting;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Trace.V1;
using Grpc.Core;
using OtlpStatus = OpenTelemetry.Proto.Trace.V1.Status;

namespace Stove.Net.Tracing;

/// <summary>
/// Lightweight OTLP gRPC receiver that listens on a random port and parses incoming
/// ExportTraceServiceRequest messages into StoveSpan records.
/// Modeled after Kotlin Stove's OTLPSpanReceiver.
/// </summary>
public sealed class OtlpReceiver : TraceService.TraceServiceBase, IAsyncDisposable
{
    private WebApplication? _app;
    private readonly StoveTraceCollector _collector;
    private readonly int _requestedPort;

    /// <summary>
    /// The actual endpoint the receiver is listening on (e.g., "http://localhost:54321").
    /// Only available after <see cref="StartAsync"/> is called.
    /// </summary>
    public string? Endpoint { get; private set; }

    public OtlpReceiver(StoveTraceCollector collector, int port = 0)
    {
        _collector = collector;
        _requestedPort = port;
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(this);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, _requestedPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        _app = builder.Build();
        _app.MapGrpcService<OtlpReceiver>();
        await _app.StartAsync();

        var address = _app.Urls.FirstOrDefault()
            ?? $"http://localhost:{_requestedPort}";
        Endpoint = address;
    }

    public override Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request, ServerCallContext context)
    {
        foreach (var resourceSpans in request.ResourceSpans)
        {
            var serviceName = ExtractServiceName(resourceSpans.Resource);

            foreach (var scopeSpans in resourceSpans.ScopeSpans)
            {
                foreach (var span in scopeSpans.Spans)
                {
                    var traceId = ToHex(span.TraceId);
                    var spanId = ToHex(span.SpanId);
                    var parentSpanId = span.ParentSpanId.IsEmpty
                        ? string.Empty
                        : ToHex(span.ParentSpanId);

                    // Filter out internal gRPC spans to avoid feedback loops
                    if (IsInternalGrpcSpan(span, scopeSpans.Scope))
                        continue;

                    var attributes = ExtractAttributes(span.Attributes);
                    var exception = ExtractException(span);
                    var status = MapStatus(span.Status, exception);

                    var stoveSpan = new StoveSpan
                    {
                        TraceId = traceId,
                        SpanId = spanId,
                        ParentSpanId = parentSpanId,
                        OperationName = span.Name,
                        ServiceName = serviceName,
                        Start = FromUnixNano(span.StartTimeUnixNano),
                        End = FromUnixNano(span.EndTimeUnixNano),
                        Status = status,
                        Attributes = attributes,
                        Exception = exception
                    };

                    _collector.RecordSpan(stoveSpan);
                }
            }
        }

        return Task.FromResult(new ExportTraceServiceResponse());
    }

    private static string ExtractServiceName(
        OpenTelemetry.Proto.Resource.V1.Resource? resource)
    {
        if (resource == null) return "unknown";
        foreach (var attr in resource.Attributes)
        {
            if (attr.Key == "service.name")
                return attr.Value.StringValue;
        }

        return "unknown";
    }

    private static Dictionary<string, string> ExtractAttributes(
        Google.Protobuf.Collections.RepeatedField<KeyValue> attributes)
    {
        var result = new Dictionary<string, string>();
        foreach (var attr in attributes)
        {
            result[attr.Key] = AnyValueToString(attr.Value);
        }

        return result;
    }

    private static string AnyValueToString(AnyValue? value)
    {
        if (value == null) return string.Empty;
        return value.ValueCase switch
        {
            AnyValue.ValueOneofCase.StringValue => value.StringValue,
            AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(),
            AnyValue.ValueOneofCase.BoolValue => value.BoolValue.ToString(),
            AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString(),
            AnyValue.ValueOneofCase.ArrayValue => string.Join(", ",
                value.ArrayValue.Values.Select(AnyValueToString)),
            AnyValue.ValueOneofCase.KvlistValue => string.Join(", ",
                value.KvlistValue.Values.Select(kv => $"{kv.Key}={AnyValueToString(kv.Value)}")),
            AnyValue.ValueOneofCase.BytesValue =>
                Convert.ToHexString(value.BytesValue.ToByteArray()),
            _ => string.Empty
        };
    }

    private static StoveExceptionInfo? ExtractException(Span span)
    {
        // OTLP convention: exception events have name "exception"
        foreach (var evt in span.Events)
        {
            if (evt.Name != "exception") continue;

            string type = "", message = "", stacktrace = "";
            foreach (var attr in evt.Attributes)
            {
                switch (attr.Key)
                {
                    case "exception.type":
                        type = attr.Value.StringValue;
                        break;
                    case "exception.message":
                        message = attr.Value.StringValue;
                        break;
                    case "exception.stacktrace":
                        stacktrace = attr.Value.StringValue;
                        break;
                }
            }

            if (!string.IsNullOrEmpty(type) || !string.IsNullOrEmpty(message))
            {
                return new StoveExceptionInfo(type, message,
                    stacktrace.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        // Fallback: use span status message
        if (span.Status?.Code == OtlpStatus.Types.StatusCode.Error
            && !string.IsNullOrEmpty(span.Status.Message))
        {
            return new StoveExceptionInfo("Error", span.Status.Message, []);
        }

        return null;
    }

    private static string MapStatus(OtlpStatus? status, StoveExceptionInfo? exception)
    {
        if (status?.Code == OtlpStatus.Types.StatusCode.Error || exception != null)
            return "ERROR";
        if (status?.Code == OtlpStatus.Types.StatusCode.Ok)
            return "OK";
        return "UNSET";
    }

    private static bool IsInternalGrpcSpan(Span span,
        OpenTelemetry.Proto.Common.V1.InstrumentationScope? scope)
    {
        // Filter spans from our own gRPC service to avoid self-referencing noise
        if (scope?.Name?.Contains("Grpc.AspNetCore") == true)
            return true;
        if (span.Name.Contains("opentelemetry.proto.collector"))
            return true;
        return false;
    }

    private static string ToHex(Google.Protobuf.ByteString bytes)
        => Convert.ToHexString(bytes.Span).ToLowerInvariant();

    private static DateTimeOffset FromUnixNano(ulong nanos)
        => DateTimeOffset.UnixEpoch.AddTicks((long)(nanos / 100));

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
