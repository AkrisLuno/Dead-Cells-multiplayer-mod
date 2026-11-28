using Xunit;

namespace DeadCellsMultiplayerMod;

public sealed class CoopSavesTests
{
    [Fact]
    public void NewRunKeepsTheSelectedSlotAndSidecar()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var slot = Path.Combine(directory.FullName, "user_0.dat");
            Assert.Null(CoopSaves.BackUpSlot(slot));
            File.WriteAllText(slot, "previous run");
            File.WriteAllText(Path.Combine(directory.FullName, "user_0.modded.json"), "sidecar");
            File.WriteAllText(Path.Combine(directory.FullName, "user_1.dat"), "different slot");

            var backup = Assert.IsType<string>(CoopSaves.BackUpSlot(slot));
            File.WriteAllText(slot, "new run");

            Assert.Equal("previous run", File.ReadAllText(Path.Combine(backup, "user_0.dat")));
            Assert.Equal("sidecar", File.ReadAllText(Path.Combine(backup, "user_0.modded.json")));
            Assert.Equal(2, Directory.GetFiles(backup).Length);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
