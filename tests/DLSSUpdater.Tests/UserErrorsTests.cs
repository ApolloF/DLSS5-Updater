using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

public class UserErrorsTests
{
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(FileNotFoundException))]
    public void Curated_exceptions_show_their_own_message(Type type)
    {
        var ex = (Exception)Activator.CreateInstance(type, "Pick a build first.")!;
        Assert.True(UserErrors.IsCurated(ex));
        Assert.Equal("Pick a build first.", UserErrors.Message(ex));
    }

    [Fact]
    public void App_exceptions_keep_their_message()
    {
        Assert.Equal("game.exe is running. Close the game first.", UserErrors.Message(new GameRunningException("game.exe")));
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(ObjectDisposedException))]
    public void Other_exceptions_hide_the_technical_text(Type type)
    {
        var ex = type == typeof(ObjectDisposedException) ? new ObjectDisposedException("secret") : (Exception)Activator.CreateInstance(type, "secret path failed")!;
        Assert.False(UserErrors.IsCurated(ex));
        Assert.Equal(UserErrors.Generic, UserErrors.Message(ex));
    }

    [Fact]
    public void Empty_message_falls_back_to_the_generic_text()
    {
        Assert.Equal(UserErrors.Generic, UserErrors.Message(new InvalidOperationException("")));
    }
}
