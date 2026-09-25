using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Реализация <see cref="IChunkingStrategyResolver"/>
    /// (v1.5.0, KI-083, Шаг 3C).
    ///
    /// <para>
    /// Собирает все зарегистрированные <see cref="IChunkingStrategy"/> через
    /// <see cref="IEnumerable{T}"/> и строит словарь <c>Name → Strategy</c>.
    /// Имена — case-insensitive (<c>RECURSIVE</c> = <c>recursive</c>).
    /// </para>
    /// </summary>
    public sealed class ChunkingStrategyResolver : IChunkingStrategyResolver
    {
        /// <summary>Имя default-стратегии (Rag:Chunking:Strategy default).</summary>
        private const string DefaultStrategyName = "recursive";

        private readonly Dictionary<string, IChunkingStrategy> _strategies;

        /// <summary>
        /// Создаёт резолвер.
        /// </summary>
        /// <param name="strategies">
        /// Все зарегистрированные стратегии. Должен содержать
        /// <c>recursive</c> (default).
        /// </param>
        /// <exception cref="ArgumentNullException">Если <paramref name="strategies"/> = null</exception>
        /// <exception cref="InvalidOperationException">
        /// Если стратегия <c>recursive</c> не зарегистрирована
        /// </exception>
        public ChunkingStrategyResolver(IEnumerable<IChunkingStrategy> strategies)
        {
            if (strategies == null)
                throw new ArgumentNullException(nameof(strategies));

            _strategies = strategies.ToDictionary(
                s => s.Name,
                StringComparer.OrdinalIgnoreCase);

            if (!_strategies.ContainsKey(DefaultStrategyName))
            {
                throw new InvalidOperationException(
                    $"Default-стратегия '{DefaultStrategyName}' не зарегистрирована. " +
                    $"Доступные: {string.Join(", ", _strategies.Keys)}");
            }
        }

        /// <inheritdoc />
        public IChunkingStrategy Resolve(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)
                && _strategies.TryGetValue(name, out var strategy))
            {
                return strategy;
            }

            // Fallback на default — не падаем на опечатке в конфиге.
            return _strategies[DefaultStrategyName];
        }

        /// <inheritdoc />
        public IChunkingStrategy GetDefault() => _strategies[DefaultStrategyName];
    }
}