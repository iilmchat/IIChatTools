using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Тесты <see cref="DomCoordinateProvider"/> (KI-161).
    /// Fake CDP-сессия — без реального Chrome.
    /// </summary>
    public class DomCoordinateProviderTests
    {
        // ============================================================
        // Конвертация координат (главное)
        // ============================================================

        [Fact]
        public async Task ResolveAsync_ElementFound_NoOffsets_IdentityConversion()
        {
            // CDP вернул (200,100) в viewport, смещений нет — scale=1.
            var cdp = new FakeCdp
            {
                NextFindResult = new CdpElementResult
                {
                    Found = true,
                    ViewportX = 200,
                    ViewportY = 100,
                    Width = 80,
                    Height = 24,
                    MatchedBy = "label",
                    Selector = "button#search"
                },
                NextViewportInfo = new CdpViewportInfo
                {
                    DevicePixelRatio = 1.0,
                    WindowScreenX = 0,
                    WindowScreenY = 0,
                    ChromeUiWidth = 0,
                    ChromeUiHeight = 0
                }
            };

            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);
            var request = new CoordinateRequest
            {
                TargetId = "search_button",
                TargetLabel = "Найти",
                TargetType = "button",
                ScreenshotScaleX = 1.0,
                ScreenshotScaleY = 1.0
            };

            var result = await provider.ResolveAsync(request);

            Assert.True(result.Found);
            Assert.True(result.FromDom);
            Assert.Equal(200, result.X);
            Assert.Equal(100, result.Y);
            Assert.Equal("button#search", result.Selector);
        }

        [Fact]
        public async Task ResolveAsync_ElementFound_ConvertsViewportToScreen()
        {
            // CDP: viewport (200,100), окно на (100,50),
            // chrome UI 16×140, DPR=1, scale=1.
            // Ожидание:
            //   cssX = 200 + 100 + 8 = 308
            //   cssY = 100 + 50 + 140 = 290
            //   screenPx = cssX * DPR = 308, 290
            //   shotX = 308 / 1 = 308
            var cdp = new FakeCdp
            {
                NextFindResult = new CdpElementResult
                {
                    Found = true,
                    ViewportX = 200,
                    ViewportY = 100,
                    MatchedBy = "label"
                },
                NextViewportInfo = new CdpViewportInfo
                {
                    DevicePixelRatio = 1.0,
                    WindowScreenX = 100,
                    WindowScreenY = 50,
                    ChromeUiWidth = 16,
                    ChromeUiHeight = 140
                }
            };

            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);
            var result = await provider.ResolveAsync(new CoordinateRequest
            {
                TargetId = "btn",
                TargetType = "button",
                ScreenshotScaleX = 1.0,
                ScreenshotScaleY = 1.0
            });

            Assert.True(result.Found);
            Assert.Equal(308, result.X);
            Assert.Equal(290, result.Y);
        }

        [Fact]
        public async Task ResolveAsync_ElementFound_WithDpr_ScaledByDpr()
        {
            // DPR=1.25 (масштаб 125% в Windows). screenPx = cssX * 1.25.
            var cdp = new FakeCdp
            {
                NextFindResult = new CdpElementResult
                {
                    Found = true,
                    ViewportX = 400,
                    ViewportY = 300
                },
                NextViewportInfo = new CdpViewportInfo
                {
                    DevicePixelRatio = 1.25,
                    WindowScreenX = 0,
                    WindowScreenY = 0,
                    ChromeUiWidth = 0,
                    ChromeUiHeight = 0
                }
            };

            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);
            var result = await provider.ResolveAsync(new CoordinateRequest
            {
                TargetId = "btn",
                TargetType = "button",
                ScreenshotScaleX = 1.0,
                ScreenshotScaleY = 1.0
            });

            Assert.True(result.Found);
            Assert.Equal(500, result.X);   // 400 * 1.25
            Assert.Equal(375, result.Y);   // 300 * 1.25
        }

        [Fact]
        public async Task ResolveAsync_ElementFound_WithScreenshotScale_Divided()
        {
            // Screenshot downscale в 2 раза: 1920 → 960.
            // screenPx=400 → shotX=200.
            var cdp = new FakeCdp
            {
                NextFindResult = new CdpElementResult
                {
                    Found = true,
                    ViewportX = 400,
                    ViewportY = 300
                },
                NextViewportInfo = new CdpViewportInfo
                {
                    DevicePixelRatio = 1.0,
                    WindowScreenX = 0,
                    WindowScreenY = 0,
                    ChromeUiWidth = 0,
                    ChromeUiHeight = 0
                }
            };

            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);
            var result = await provider.ResolveAsync(new CoordinateRequest
            {
                TargetId = "btn",
                TargetType = "button",
                ScreenshotScaleX = 2.0,
                ScreenshotScaleY = 2.0
            });

            Assert.True(result.Found);
            Assert.Equal(200, result.X);
            Assert.Equal(150, result.Y);
        }

        // ============================================================
        // Неудачи / edge cases
        // ============================================================

        [Fact]
        public async Task ResolveAsync_CdpNotConnected_ReturnsNotFound()
        {
            var cdp = new FakeCdp { IsConnected = false };
            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);

            var result = await provider.ResolveAsync(new CoordinateRequest
            {
                TargetId = "btn", TargetType = "button"
            });

            Assert.False(result.Found);
            Assert.False(result.FromDom);
            Assert.Contains("не подключена", result.Error);
        }

        [Fact]
        public async Task ResolveAsync_ElementNotFound_ReturnsNotFound()
        {
            var cdp = new FakeCdp
            {
                NextFindResult = new CdpElementResult
                {
                    Found = false,
                    Error = "Нет подходящих элементов"
                }
            };
            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);

            var result = await provider.ResolveAsync(new CoordinateRequest
            {
                TargetId = "btn", TargetType = "button"
            });

            Assert.False(result.Found);
            Assert.Equal("Нет подходящих элементов", result.Error);
        }

        [Fact]
        public async Task ResolveAsync_CdpThrows_ReturnsNotFound_NotThrowing()
        {
            var cdp = new FakeCdp
            {
                ThrowOnFind = new InvalidOperationException("boom")
            };
            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);

            var result = await provider.ResolveAsync(new CoordinateRequest
            {
                TargetId = "btn", TargetType = "button"
            });

            Assert.False(result.Found);
            Assert.Contains("boom", result.Error);
        }

        [Fact]
        public async Task ResolveAsync_NullRequest_Throws()
        {
            var cdp = new FakeCdp();
            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => provider.ResolveAsync(null));
        }

        [Fact]
        public async Task ResolveAsync_Cancellation_Propagates()
        {
            var cdp = new FakeCdp
            {
                ThrowOnFind = new OperationCanceledException()
            };
            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => provider.ResolveAsync(new CoordinateRequest
                {
                    TargetId = "btn", TargetType = "button"
                }));
        }

        [Fact]
        public async Task ResolveAsync_ZeroDpr_DefaultsToOne()
        {
            // Защита: если CDP вернул DPR=0 (теоретически возможно),
            // не падаем и не обнуляем координаты.
            var cdp = new FakeCdp
            {
                NextFindResult = new CdpElementResult
                {
                    Found = true,
                    ViewportX = 100,
                    ViewportY = 100
                },
                NextViewportInfo = new CdpViewportInfo
                {
                    DevicePixelRatio = 0,
                    WindowScreenX = 0,
                    WindowScreenY = 0,
                    ChromeUiWidth = 0,
                    ChromeUiHeight = 0
                }
            };
            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);

            var result = await provider.ResolveAsync(new CoordinateRequest
            {
                TargetId = "btn", TargetType = "button",
                ScreenshotScaleX = 1.0, ScreenshotScaleY = 1.0
            });

            Assert.True(result.Found);
            Assert.Equal(100, result.X);
            Assert.Equal(100, result.Y);
        }

        [Fact]
        public async Task ResolveAsync_ZeroScreenshotScale_DefaultsToOne()
        {
            var cdp = new FakeCdp
            {
                NextFindResult = new CdpElementResult
                {
                    Found = true, ViewportX = 200, ViewportY = 150
                },
                NextViewportInfo = new CdpViewportInfo()
            };
            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);

            var result = await provider.ResolveAsync(new CoordinateRequest
            {
                TargetId = "btn", TargetType = "button",
                ScreenshotScaleX = 0, ScreenshotScaleY = 0
            });

            Assert.True(result.Found);
            Assert.Equal(200, result.X);
            Assert.Equal(150, result.Y);
        }

        [Fact]
        public async Task ResolveAsync_PropagatesLabelAndTypeToCdp()
        {
            var cdp = new FakeCdp
            {
                NextFindResult = new CdpElementResult
                {
                    Found = true, ViewportX = 10, ViewportY = 20
                },
                NextViewportInfo = new CdpViewportInfo()
            };
            var provider = new DomCoordinateProvider(cdp, NullLogger.Instance);

            await provider.ResolveAsync(new CoordinateRequest
            {
                TargetId = "x",
                TargetLabel = "Найти",
                TargetType = "button"
            });

            Assert.NotNull(cdp.LastQuery);
            Assert.Equal("Найти", cdp.LastQuery.Label);
            Assert.Equal("button", cdp.LastQuery.Type);
        }

        // ============================================================
        // Fake
        // ============================================================

        /// <summary>
        /// Fake <see cref="IChromeCdpSession"/> — управляемые ответы,
        /// без реального Chrome.
        /// </summary>
        private sealed class FakeCdp : IChromeCdpSession
        {
            public bool IsConnected { get; set; } = true;
            public CdpElementResult NextFindResult { get; set; } = new CdpElementResult { Found = false };
            public CdpViewportInfo NextViewportInfo { get; set; } = new CdpViewportInfo();
            public CdpElementQuery LastQuery { get; private set; }
            public Exception ThrowOnFind { get; set; }

            public Task<bool> ConnectAsync(string browserUrl, CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public Task<CdpElementResult> FindElementAsync(
                CdpElementQuery query, CancellationToken cancellationToken = default)
            {
                LastQuery = query;
                if (ThrowOnFind != null) throw ThrowOnFind;
                return Task.FromResult(NextFindResult);
            }

            public Task<CdpViewportInfo> GetViewportInfoAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(NextViewportInfo);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}