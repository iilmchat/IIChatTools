using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Tests.Fakes
{
    /// <summary>
    /// Fake IVisionBackend для тестов VisionAgentService
    /// (v1.12.0, KI-131, Ф6.9). Управляемые ошибки + лог вызовов.
    /// </summary>
    internal sealed class FakeVisionBackend : IVisionBackend
    {
        public string Name => "fake";

        public List<string> CallLog { get; } = new();
        public byte[] ScreenshotBytes { get; set; } = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        public Exception OpenException { get; set; }
        public Exception ScreenshotException { get; set; }
        public Exception ActionException { get; set; }

        /// <summary>
        /// KI-137: ответ <see cref="ScreenshotFullResolutionAsync"/>.
        /// По умолчанию <c>null</c> — OCR-fallback активируется как
        /// «backend не поддерживает full-res» (graceful).
        /// </summary>
        public FullResolutionScreenshotDto ScreenshotFullResolution { get; set; }

        /// <summary>
        /// KI-137: если задано — <see cref="ScreenshotFullResolutionAsync"/>
        /// бросит это исключение (тестирование fallback'а).
        /// </summary>
        public Exception ScreenshotFullResolutionException { get; set; }

        public Task OpenAsync(string url, CancellationToken ct = default)
        {
            CallLog.Add($"Open({url})");
            if (OpenException != null) throw OpenException;
            return Task.CompletedTask;
        }

        public Task<byte[]> ScreenshotAsync(CancellationToken ct = default)
        {
            CallLog.Add("Screenshot");
            if (ScreenshotException != null) throw ScreenshotException;
            return Task.FromResult(ScreenshotBytes);
        }

        /// <inheritdoc />
        /// <remarks>
        /// KI-137: по умолчанию возвращает <c>null</c> — OCR-fallback
        /// активируется как «backend не поддерживает full-res». Тест может
        /// задать <see cref="ScreenshotFullResolution"/> или выбросить
        /// исключение через <see cref="ScreenshotFullResolutionException"/>.
        /// </remarks>
        public Task<FullResolutionScreenshotDto> ScreenshotFullResolutionAsync(CancellationToken ct = default)
        {
            CallLog.Add("ScreenshotFullResolution");
            if (ScreenshotFullResolutionException != null) throw ScreenshotFullResolutionException;
            return Task.FromResult(ScreenshotFullResolution);
        }

        public Task ClickAsync(int x, int y, CancellationToken ct = default)
        {
            CallLog.Add($"Click({x},{y})");
            if (ActionException != null) throw ActionException;
            return Task.CompletedTask;
        }

        public Task DoubleClickAsync(int x, int y, CancellationToken ct = default)
        {
            CallLog.Add($"DoubleClick({x},{y})");
            if (ActionException != null) throw ActionException;
            return Task.CompletedTask;
        }

        public Task RightClickAsync(int x, int y, CancellationToken ct = default)
        {
            CallLog.Add($"RightClick({x},{y})");
            if (ActionException != null) throw ActionException;
            return Task.CompletedTask;
        }

        public Task MoveMouseAsync(int x, int y, CancellationToken ct = default)
        {
            CallLog.Add($"MoveMouse({x},{y})");
            if (ActionException != null) throw ActionException;
            return Task.CompletedTask;
        }

        public Task TypeAsync(string text, CancellationToken ct = default)
        {
            CallLog.Add($"Type({text})");
            if (ActionException != null) throw ActionException;
            return Task.CompletedTask;
        }

        public Task PressKeyAsync(string key, CancellationToken ct = default)
        {
            CallLog.Add($"PressKey({key})");
            if (ActionException != null) throw ActionException;
            return Task.CompletedTask;
        }

        public Task HotkeyAsync(IReadOnlyList<string> keys, CancellationToken ct = default)
        {
            CallLog.Add($"Hotkey({string.Join("+", keys)})");
            if (ActionException != null) throw ActionException;
            return Task.CompletedTask;
        }

        public Task ScrollAsync(int deltaY, CancellationToken ct = default)
        {
            CallLog.Add($"Scroll({deltaY})");
            if (ActionException != null) throw ActionException;
            return Task.CompletedTask;
        }

        public Task WaitAsync(int milliseconds, CancellationToken ct = default)
        {
            CallLog.Add($"Wait({milliseconds})");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}