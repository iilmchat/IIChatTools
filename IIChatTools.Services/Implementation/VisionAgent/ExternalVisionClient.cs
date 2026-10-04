using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Скелет External Vision LLM — мультимодальный вызов внешних провайдеров
    /// (OpenAI GPT-4o, Anthropic Claude, Google Gemini).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф5.3). См. DESIGN § 3.2, § 5.1.
    /// </para>
    /// <para>
    /// <b>НЕ реализован в v1.12.0.</b> Причина: <see cref="IExternalLlmClient"/>
    /// (v1.8.1, KI-109) — <b>text-only</b>. Поддержка изображений требует
    /// расширения <c>ExternalLlmRequest</c> полем <c>ImageBase64DataUrl</c> +
    /// <b>трёх разных билдеров</b> в <c>ExternalLlmClient</c>
    /// (у OpenAI / Anthropic / Gemini — свои форматы multimodal content).
    /// Это отдельная фича → заведён <b>KI-141</b> (Planned, v1.12.x).
    /// </para>
    /// <para>
    /// <b>Поведение сейчас:</b> <see cref="DescribeAsync"/> бросает
    /// <see cref="NotSupportedException"/>. <c>IsReady = false</c>, чтобы
    /// <c>AutoVisionClient</c> (Ф5.4) не тратил время на попытку.
    /// </para>
    /// </remarks>
    public sealed class ExternalVisionClient : IVisionLlmClient
    {
        /// <inheritdoc />
        public bool IsReady => false;

        /// <inheritdoc />
        public Task<ScreenDescriptionDto> DescribeAsync(
            byte[] screenshotPng,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "External Vision LLM (multimodal) не поддерживается в v1.12.0. " +
                "IExternalLlmClient работает только с текстом. " +
                "Отслеживается в KI-141 (Planned, v1.12.x). " +
                "Используйте Provider=lmstudio для локальной VL-модели.");
        }
    }
}