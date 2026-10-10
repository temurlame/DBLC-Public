using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DBLC;

public sealed class GmRequestDiagnostics
{
    private static readonly object FileSync = new();
    private const long MaxLogBytes = 16L * 1024 * 1024;
    private static long RenderCount, RenderTicks, SaveCount, SaveTicks, SavedBytes;
    private readonly long _renderCount = Interlocked.Read(ref RenderCount), _renderTicks = Interlocked.Read(ref RenderTicks);
    private readonly long _saveCount = Interlocked.Read(ref SaveCount), _saveTicks = Interlocked.Read(ref SaveTicks), _savedBytes = Interlocked.Read(ref SavedBytes);
    private readonly Stopwatch _turn = Stopwatch.StartNew();
    private int _inferencePasses;
    private int _toolCalls;
    private int _steers;
    private int _passesWithoutPromptUsage;
    private int _passesWithoutGenerationUsage;
    private long _promptTokens;
    private long _completionTokens;
    private long _reasoningTokens;
    private readonly string _path = AppDataPaths.File("gm-performance.jsonl");

    public Guid TurnId { get; } = Guid.NewGuid();
    public string LogPath => _path;

    // Aggregate local work without logging text or writing a diagnostics line per UI frame.
    public static void RecordRender(TimeSpan elapsed) { Interlocked.Increment(ref RenderCount); Interlocked.Add(ref RenderTicks, elapsed.Ticks); }
    public static void RecordPersistence(TimeSpan elapsed, long bytes) { Interlocked.Increment(ref SaveCount); Interlocked.Add(ref SaveTicks, elapsed.Ticks); Interlocked.Add(ref SavedBytes, bytes); }
    public void RecordStage(string name, TimeSpan elapsed, int? bytes = null) => Write(new
    {
        event_name = "request_stage", name, elapsed_ms = Round(elapsed.TotalMilliseconds), bytes
    });

    public void RecordChatMessageOrderingWarning(string operation, int messageIndex) => Write(new
    {
        event_name = "chat_message_order_warning", operation, system_message_index = messageIndex
    });
    public void RecordMobileCircuitError(string category, int eventId, Exception exception) => Write(new
    {
        event_name = "mobile_circuit_error", category, event_id = eventId,
        exception_type = exception.GetType().FullName, stack = exception.StackTrace,
        inner_type = exception.InnerException?.GetType().FullName,
        inner_stack = exception.InnerException?.StackTrace
    });
    public void RecordMobileHostEvent(string phase) => Write(new { event_name = "mobile_host", phase });
    public void RecordMobileConnectionEvent(string phase, string circuitTag, double? elapsedMs = null) => Write(new
    {
        event_name = "mobile_connection", phase, circuit_tag = circuitTag, elapsed_ms = elapsedMs is null ? (double?)null : Round(elapsedMs.Value)
    });
    public void RecordMobileSnapshot(string phase, TimeSpan elapsed, int lineCount) => Write(new
    {
        event_name = "mobile_chronicle_snapshot", phase, elapsed_ms = Round(elapsed.TotalMilliseconds), line_count = lineCount
    });
    public void RecordMobileTransport(string method, int status, bool webSocket, TimeSpan elapsed, string? exceptionType = null) => Write(new
    {
        event_name = "mobile_transport", method, status, websocket = webSocket,
        elapsed_ms = Round(elapsed.TotalMilliseconds), exception_type = exceptionType
    });
    public void RecordMobileTransportWarning(string category, int eventId, Exception? exception) => Write(new
    {
        event_name = "mobile_transport_warning", category, event_id = eventId,
        exception_type = exception?.GetType().FullName
    });
    public void RecordContext(System.Text.Json.Nodes.JsonArray messages, TimeSpan elapsed)
    {
        var system = 0; var user = 0; var assistant = 0; var tool = 0; var images = 0;
        foreach (var message in messages.OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var chars = 0;
            if (message["content"] is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text)) chars = text.Length;
            else if (message["content"] is System.Text.Json.Nodes.JsonArray parts)
                foreach (var part in parts.OfType<System.Text.Json.Nodes.JsonObject>())
                    if ((string?)part["type"] == "image_url") images++;
                    else chars += ((string?)part["text"])?.Length ?? 0;
            switch ((string?)message["role"])
            {
                case "system": system += chars; break;
                case "user": user += chars; break;
                case "assistant": assistant += chars; break;
                case "tool": tool += chars; break;
            }
        }
        Write(new { event_name = "context_assembled", elapsed_ms = Round(elapsed.TotalMilliseconds), message_count = messages.Count,
            system_characters = system, user_characters = user, assistant_characters = assistant, tool_characters = tool, image_count = images });
    }

    public void RecordPreflight(string name, TimeSpan elapsed) => Write(new
    {
        event_name = "model_discovery",
        name,
        elapsed_ms = Round(elapsed.TotalMilliseconds)
    });

    public int StartInferencePass(string model, string phase, int messageCount, int toolDefinitionCount)
    {
        var pass = Interlocked.Increment(ref _inferencePasses);
        Write(new { event_name = "inference_started", pass, model, phase, message_count = messageCount, tool_definition_count = toolDefinitionCount });
        return pass;
    }

    public void FinishInferencePass(int pass, string phase, TimeSpan wallTime, int? promptTokens, int? generationTokens,
        double? promptEvaluationMs, double? generationMs, int resultCharacters, IReadOnlyList<string> toolNames, string outcome, int? cachedPromptTokens = null, int? reasoningTokens = null)
    {
        if (promptTokens is { } prompt) Interlocked.Add(ref _promptTokens, prompt);
        else Interlocked.Increment(ref _passesWithoutPromptUsage);
        if (generationTokens is { } generated) Interlocked.Add(ref _completionTokens, generated);
        else Interlocked.Increment(ref _passesWithoutGenerationUsage);
        if (reasoningTokens is { } reasoning) Interlocked.Add(ref _reasoningTokens, reasoning);
        Write(new
        {
            event_name = "inference_finished",
            pass,
            phase,
            wall_ms = Round(wallTime.TotalMilliseconds),
            prompt_tokens = promptTokens,
            cached_prompt_tokens = cachedPromptTokens,
            prompt_evaluation_ms = promptEvaluationMs,
            generation_tokens = generationTokens,
            reasoning_tokens = reasoningTokens,
            generation_ms = generationMs,
            generation_tokens_per_second = generationTokens is > 0 && generationMs is > 0 ? (double?)Round(generationTokens.Value * 1000d / generationMs.Value) : null,
            result_characters = resultCharacters,
            tool_calls = toolNames,
            outcome
        });
    }

    public void RecordTool(int pass, string name, string arguments, TimeSpan elapsed, string? result, bool succeeded, bool executed = true)
    {
        Interlocked.Increment(ref _toolCalls);
        var characters = result?.Length ?? 0;
        Write(new
        {
            event_name = "tool_finished",
            pass,
            name,
            arguments_sha256 = Hash(arguments),
            executed,
            elapsed_ms = Round(elapsed.TotalMilliseconds),
            result_characters = characters,
            result_utf8_bytes = result is null ? 0 : Encoding.UTF8.GetByteCount(result),
            estimated_result_tokens = characters == 0 ? 0 : (characters + 3) / 4,
            succeeded
        });
    }

    public void RecordSteer(int characters)
    {
        var count = Interlocked.Increment(ref _steers);
        Write(new { event_name = "steer_received", steer = count, characters });
    }

    public void RecordToolPreambleRetained(int characters) => Write(new
    {
        event_name = "tool_preamble_retained",
        characters
    });

    public void RecordPortrayalCueMatch(int matched, int characters, string source) => Write(new
    {
        event_name = "npc_portrayal_cues_matched",
        matched,
        cue_characters = characters,
        match_source = source
    });

    public void RecordOpenThreadReminder(bool fired, int openThreadCount, int? interval, string? visibility, Guid? threadId) => Write(new
    {
        event_name = "open_thread_reminder",
        fired,
        open_thread_count = openThreadCount,
        reminder_interval_beats = interval,
        thread_visibility = visibility,
        thread_id = threadId?.ToString("N")
    });

    public void RecordAuxiliary(string name, TimeSpan elapsed, int? tokens = null, bool modelInference = false)
    {
        if (modelInference) Interlocked.Increment(ref _inferencePasses);
        Write(new
        {
            event_name = modelInference ? "auxiliary_inference_finished" : "auxiliary_request",
            name,
            elapsed_ms = Round(elapsed.TotalMilliseconds),
            tokens
        });
    }

    public void RecordAuxiliaryInference(string name, TimeSpan elapsed, int? promptTokens, int? generationTokens, double? promptEvaluationMs, double? generationMs)
    {
        Interlocked.Increment(ref _inferencePasses);
        if (promptTokens is { } prompt) Interlocked.Add(ref _promptTokens, prompt);
        else Interlocked.Increment(ref _passesWithoutPromptUsage);
        if (generationTokens is { } generated) Interlocked.Add(ref _completionTokens, generated);
        else Interlocked.Increment(ref _passesWithoutGenerationUsage);
        Write(new
        {
            event_name = "auxiliary_inference_finished",
            name,
            elapsed_ms = Round(elapsed.TotalMilliseconds),
            prompt_tokens = promptTokens,
            prompt_evaluation_ms = promptEvaluationMs,
            generation_tokens = generationTokens,
            generation_ms = generationMs
        });
    }

    public void RecordTurnFailure(string category) => Write(new { event_name = "turn_failure", category });

    public void Complete(string outcome) => Write(new
    {
        event_name = "turn_finished",
        outcome,
        total_wall_ms = Round(_turn.Elapsed.TotalMilliseconds),
        model_inference_passes = _inferencePasses,
        tool_calls = _toolCalls,
        steers = _steers,
        reported_prompt_tokens_across_passes = _promptTokens,
        reported_generation_tokens_across_passes = _completionTokens,
        reported_reasoning_tokens_across_passes = _reasoningTokens,
        passes_without_prompt_usage = _passesWithoutPromptUsage,
        passes_without_generation_usage = _passesWithoutGenerationUsage,
        rendered_text_blocks = Interlocked.Read(ref RenderCount) - _renderCount,
        markdown_render_ms = Round(TimeSpan.FromTicks(Interlocked.Read(ref RenderTicks) - _renderTicks).TotalMilliseconds),
        state_saves = Interlocked.Read(ref SaveCount) - _saveCount,
        state_save_ms = Round(TimeSpan.FromTicks(Interlocked.Read(ref SaveTicks) - _saveTicks).TotalMilliseconds),
        state_saved_bytes = Interlocked.Read(ref SavedBytes) - _savedBytes
    });

    private void Write(object entry)
    {
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.Now,
                turn_id = TurnId,
                entry
            }) + Environment.NewLine;
            lock (FileSync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length + Encoding.UTF8.GetByteCount(line) > MaxLogBytes)
                    File.Move(_path, _path + ".previous", overwrite: true);
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
        }
        catch { }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? "")));
    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
