using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Tests.Fakes
{
    /// <summary>
    /// Fake IPlannerLlmClient для тестов VisionAgentService.
    /// </summary>
    internal sealed class FakePlannerLlmClient : IPlannerLlmClient
    {
        public bool IsReady { get; set; } = true;
        public Queue<VisionActionDto> Responses { get; } = new();
        public VisionActionDto DefaultResponse { get; set; } = new VisionActionDto { Action = "done", Reason = "task complete" };
        public Exception Throws { get; set; }
        public List<int> UserIdsReceived { get; } = new();

        public Task<VisionActionDto> PlanNextAsync(
            string task,
            IReadOnlyList<VisionStepDto> history,
            ScreenDescriptionDto screen,
            IReadOnlyList<string> plan,
            int userId = 0,
            CancellationToken cancellationToken = default)
        {
            UserIdsReceived.Add(userId);
            if (Throws != null) throw Throws;
            var r = Responses.Count > 0 ? Responses.Dequeue() : DefaultResponse;
            return Task.FromResult(r);
        }
    }
}