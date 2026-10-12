using LifeOS.Api.Services;
using Microsoft.AspNetCore.Http;

namespace LifeOS.Tests;

public sealed class InterpretInputLimitTests
{
    [Fact]
    public async Task Input_over_limit_is_rejected_without_calling_provider()
    {
        var interpreter = new RecordingInterpreter();
        var request = new InterpretInputRequest
        {
            Input = new string('x', InterpretEndpoint.MaximumInputLength + 1)
        };

        var result = await InterpretEndpoint.HandleAsync(
            request,
            interpreter,
            Guid.NewGuid(),
            db: null!);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
        Assert.False(interpreter.WasCalled);
    }

    private sealed class RecordingInterpreter : IInterpreter
    {
        public bool WasCalled { get; private set; }

        public Task<InterpretResult> InterpretAsync(
            InterpretRequest request,
            CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(new InterpretResult { Intent = "Unsupported" });
        }
    }
}
