using Xunit;

namespace Tests.Integration;

/// <summary>Проверка, что после T2-набора не остаётся временных stage-файлов.</summary>
[Trait("Category", "T2")]
[Collection(PostgresCollection.Name)]
public sealed class TempStageLeakTests(PostgresFixture fixture)
{
    [Fact]
    public void PQ15_NoStagedFilesRemainAfterSuite()
    {
        // Фикстура фиксирует счётчики на старте; здесь проверяем текущее значение
        // относительно старта набора, чтобы не зависеть от файлов других процессов.
        Assert.True(TempStageTracker.CountStagedFiles() <= fixture.StagedFilesOnStart,
            $"После прогона T2 появились файлы databy-*.csv: было {fixture.StagedFilesOnStart}, стало {TempStageTracker.CountStagedFiles()}");
    }
}
